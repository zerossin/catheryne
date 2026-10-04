"""Lightweight supervisor for human/model-directed story play.

No screen classifier, pathfinder, enemy detector, network model, or input on import.
One durable ledger owns progress; the watchdog runs without supervisor calls.
"""
from concurrent.futures import CancelledError
from copy import deepcopy
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import threading
import time


MODES = {
    'idle': (30, ()), 'navigation': (15, ('sequence',)),
    'dialogue': (10, ('sequence',)), 'interaction': (10, ('sequence',)),
    'puzzle': (20, ('sequence',)), 'stealth': (2, ('sequence',)),
    'escape': (2, ('sequence',)), 'combat': (2, ('sequence',)),
    'cutscene': (30, ()), 'paused': (60, ('sequence',)), 'recovery': (2, ('sequence',)),
}


def nonempty(value, label):
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f'{label} is required')
    return value


def validate_plan(plan):
    if not isinstance(plan, dict):
        raise ValueError('Plan must be an object')
    if type(plan.get('draft', False)) is not bool:
        raise ValueError('draft must be boolean')
    if plan.get('draft', False):
        raise ValueError('Draft plan: fill task-specific evidence and clear draft before use')
    nonempty(plan.get('id'), 'plan id')
    steps = plan.get('steps', [])
    ids = set()
    if not isinstance(steps, list) or not steps:
        raise ValueError('Plan needs ordered steps')
    for step in steps:
        if not isinstance(step, dict):
            raise ValueError('Step must be an object')
        ident = nonempty(step.get('id'), 'step id')
        if ident in ids:
            raise ValueError('Duplicate step id')
        ids.add(ident)
        for key in ('title', 'completion', 'checkpoint', 'failure_cost'):
            nonempty(step.get(key), key)
        if not isinstance(step.get('modes'), list) or not step['modes'] or any(m not in MODES for m in step['modes']):
            raise ValueError('Step needs supported modes')
        if not isinstance(step.get('tools'), list) or any(t not in ('sequence','computer_use') for t in step['tools']):
            raise ValueError('Step needs an explicit tool list')
    return deepcopy(plan)


NO_PROGRESS_FAILURE_LIMIT = 6

class StoryDirector:
    def __init__(self, plan, journal, executor_factory, *, clock=time.perf_counter,
                 wall=time.time, period=.05, threaded=True, assistance_factory=None):
        self.plan = validate_plan(plan)
        self.steps = {s['id']: s for s in self.plan['steps']}
        self.clock, self.wall, self.period = clock, wall, period
        self.journal = Path(journal)
        self.journal.parent.mkdir(parents=True, exist_ok=True)
        self.factory = executor_factory
        self.assistance_factory = assistance_factory
        self.lock = threading.RLock()
        self.cancel = threading.Event()
        self.worker = None
        self.executor = None
        self.closed = threading.Event()
        self.started = self.clock()
        self.tick_count = 0
        self.max_tick_gap = 0
        self.last_tick = self.started
        self.scene_at = self.started
        self.waiting_since = None
        self.wait_warned = False
        self.deadline = None
        self.capture_since = None
        self.input_progress = None
        self.sequence = 0
        self.events = []
        self.fingerprint = hashlib.sha256(json.dumps(plan, sort_keys=True).encode()).hexdigest()
        self.state = dict(plan_id=plan['id'], mode='idle', stage=None, owner=None,
            paused=False, pause_available=False, urgent=False, stopped=True,
            evidence=None, observation_wall=None, alert=None, pending_result=None,
            steps={s['id']: dict(status='pending', attempts=0, seconds=0, failures=0,
                                actor=None, evidence=None) for s in plan['steps']},
            supervisor_gaps=[], session_seconds=0, unprotected_seconds=0,
            mode_seconds={mode: 0 for mode in MODES})
        if self.journal.exists():
            # Corrupt/truncated journals are errors, never silently reset progress.
            for line in self.journal.read_text(encoding='utf-8').splitlines():
                event = json.loads(line)
                if event['plan_hash'] != self.fingerprint:
                    raise ValueError('Journal belongs to a different plan revision')
                if 'state' in event:
                    self.state = event['state']
                self.sequence = event['sequence']
            self.state.setdefault('mode_seconds', {mode: 0 for mode in MODES})
            self.state.setdefault('unprotected_seconds', 0)
            # Prior process monotonic timestamps and input leases cannot resume.
            self.state.update(owner=None, external_task=None, paused=False, pause_available=False,
                              stopped=True, mode='idle', observation_wall=None,
                              alert='restart_requires_observation')
        self.state.setdefault('control', 'agent')
        self.state.setdefault('settings', dict(combat='auto', failure_limit=2, notifications=True))
        self._event('host_started')
        self.timer = threading.Thread(target=self._watch, daemon=True) if threaded else None
        if self.timer:
            self.timer.start()

    def _event(self, kind, **data):
        self.sequence += 1
        event = dict(sequence=self.sequence, at=datetime.now(timezone.utc).isoformat(),
                     elapsed_seconds=self.clock()-self.started, kind=kind, data=data,
                     plan_hash=self.fingerprint)
        # Step timing does not mutate domain state. Preserve every input event,
        # but store its shared state once at the surrounding state transition.
        if kind not in ('input_step_started', 'input_step_returned'):
            event['state'] = deepcopy(self.state)
        # Append-only source; no full-screen images, tokens, or per-frame writes.
        with self.journal.open('a', encoding='utf-8') as stream:
            stream.write(json.dumps(event, ensure_ascii=False, allow_nan=False)+'\n')
            stream.flush()
        self.events.append({k: v for k, v in event.items() if k not in ('state', 'plan_hash')})
        self.events = self.events[-500:]

    def _account(self):
        now = self.clock()
        elapsed = now-self.scene_at
        if self.state['stage'] and self.state['steps'][self.state['stage']]['status'] == 'active':
            self.state['steps'][self.state['stage']]['seconds'] += elapsed
        self.state['mode_seconds'][self.state['mode']] += elapsed
        if self.state['urgent'] and not self.state['paused']:
            self.state['unprotected_seconds'] += elapsed
        self.state['session_seconds'] += elapsed
        self.scene_at = now

    def _reply(self, kind):
        if self.waiting_since is not None:
            elapsed = self.clock()-self.waiting_since
            self.state['supervisor_gaps'] = (self.state['supervisor_gaps']+[elapsed])[-200:]
            self._event('supervisor_returned', command=kind, gap_seconds=elapsed)
        self.waiting_since = None
        self.wait_warned = False

    def _idle_input(self):
        if self.state['owner'] is not None or (self.worker and self.worker.is_alive()):
            raise RuntimeError('Input is owned; stop and wait for release before changing context')

    def capture_started(self):
        with self.lock:
            if self.capture_since is not None:
                raise RuntimeError('Capture already outstanding')
            self._reply('capture_started')
            self.capture_since = self.clock()
            self.waiting_since = self.capture_since
            self._event('capture_started')
            return dict(cursor=self.sequence)

    def capture_finished(self, error=None):
        with self.lock:
            if self.capture_since is None:
                raise ValueError('No outstanding capture')
            self._event('capture_finished', capture_seconds=self.clock()-self.capture_since, error=error)
            self.capture_since = None
            # Classification/model time starts after the capture tool returns.
            self.waiting_since = self.clock()
            self.wait_warned = False
            return dict(cursor=self.sequence)

    def observe(self, *, stage, mode, evidence, captured_wall, actor='agent',
                paused=False, pause_available=False, urgent=False, dialogue_executor=None):
        with self.lock:
            maintaining_execution = ((self.state['owner'] in ('dialogue', 'external', 'combat') or
                self.state['owner'] == 'sequence' and mode == 'navigation') and
                self.state['mode'] == mode and self.state['stage'] == stage
                and not paused and urgent == self.state['urgent'] and not self.state['stopped']
                and (dialogue_executor is None or mode != 'dialogue'
                     or dialogue_executor == self.state.get('dialogue_executor', 'repeat')))
            if not maintaining_execution:
                self._idle_input()
            if self.state['pending_result'] and stage != self.state['stage']:
                raise RuntimeError('Record the prior action result before changing stages')
            if stage not in self.steps or mode not in self.steps[stage]['modes']+['paused', 'recovery']:
                raise ValueError('Stage/mode is outside the registered plan')
            if dialogue_executor is not None and dialogue_executor not in ('repeat', 'manual', 'bettergi'):
                raise ValueError('Unknown dialogue executor')
            selected_executor = dialogue_executor or (
                self.state.get('dialogue_executor', 'repeat') if self.state['mode'] == mode == 'dialogue' else 'repeat')
            nonempty(evidence, 'fresh screen evidence')
            if actor not in ('agent', 'user') or any(type(x) is not bool for x in (paused, pause_available, urgent)):
                raise ValueError('Invalid actor or scene flags')
            if type(captured_wall) not in (float, int) or not 0 <= self.wall()-captured_wall <= 60:
                raise ValueError('Observation must have its real capture time, no older than 60s')
            if mode == 'paused' and not paused:
                raise ValueError('Paused mode needs observed pause evidence')
            self._account()
            self._reply('observe')
            record = self.state['steps'][stage]
            if record['status'] in ('pending', 'lost'):
                record['status'] = 'active'
                record['attempts'] += 1
            record.update(actor=actor, evidence=evidence)
            self.state.update(stage=stage, mode=mode, evidence=evidence, observation_wall=captured_wall,
                              paused=paused, pause_available=pause_available,
                              urgent=urgent, dialogue_executor=selected_executor, stopped=self.state['control']=='user',
                              alert=self.state['alert'] if self.state['control']=='user' else None)
            self.waiting_since = self.clock()
            self._event('scene_registered', actor=actor)
            if mode == 'combat' and self.state['settings']['combat'] == 'manual':
                self.set_control(target='user', reason='combat_manual')
            return self.report()

    def _require_agent(self):
        if self.state['stage'] and self.state['steps'][self.state['stage']].get('no_progress_failures', 0) >= NO_PROGRESS_FAILURE_LIMIT:
            raise RuntimeError('no_progress_limit: user intervention or verified progress required')
        if self.state['control'] != 'agent':
            raise RuntimeError('User owns control; explicitly return control before agent input')

    def set_control(self, *, target, reason='user_request'):
        with self.lock:
            if target not in ('agent', 'user'):
                raise ValueError('Control must be agent or user')
            nonempty(reason, 'control reason')
            if self.state['control'] == target:
                return self.report()
            if target == 'user':
                self.state['control'] = 'user'
                self.stop(reason)
            else:
                self._idle_input()
                if self.state['alert'] == 'no_progress_limit' and self.state['stage']:
                    self.state['steps'][self.state['stage']]['no_progress_failures'] = 0
                self.state.update(control='agent', stopped=True, observation_wall=None,
                                  pause_available=False, alert='resume_requires_observation')
            self._event('control_changed', target=target, reason=reason)
            return self.report()

    def configure(self, *, combat, failure_limit, notifications):
        with self.lock:
            if combat not in ('auto', 'manual', 'on_failure'):
                raise ValueError('Unsupported combat policy')
            if type(failure_limit) is not int or not 1 <= failure_limit <= 10:
                raise ValueError('failure_limit must be 1..10')
            if type(notifications) is not bool:
                raise ValueError('notifications must be boolean')
            self.state['settings'] = dict(combat=combat, failure_limit=failure_limit,
                                          notifications=notifications)
            self._event('settings_changed', **self.state['settings'])
            if combat == 'manual' and self.state['mode'] == 'combat':
                self.set_control(target='user', reason='combat_manual')
            return self.report()

    def think(self):
        with self.lock:
            self._idle_input()
            if self.state['stopped']:
                raise RuntimeError('Fresh observation required')
            self._require_agent()
            self._reply('think')
            self.waiting_since = self.clock()
            self._event('thinking_started')
            if self.state['urgent'] and not self.state['paused']:
                self._protect('urgent_thinking')
            return self.report()

    def _protect(self, reason):
        if self.state['control'] == 'user':
            return
        self.state['alert'] = reason
        if self.state['paused']:
            self._event('attention_required', reason=reason, world_paused=True)
        elif self.state['pause_available'] and self.state['owner'] is None:
            # This only requests B. Screen evidence is still needed to call it paused.
            self.state.update(stopped=True, pause_available=False)
            self._launch([dict(keys=['b'], seconds=.08)], 'pause')
            self._event('pause_requested', reason=reason, world_paused=None)
        else:
            self.state['stopped'] = True
            self._event('attention_required', reason=reason, world_paused=None)

    def run(self, *, steps, intent, expected, plan_revision=None, repeat_dialogue=False):
        from .actions import validate_sequence
        with self.lock:
            self._idle_input()
            self._require_agent()
            nonempty(intent, 'action intent')
            nonempty(expected, 'expected visible result')
            if self.state['pending_result']:
                raise RuntimeError('Record the previous action result before another action')
            if self.state['stopped']:
                raise RuntimeError('Fresh playable scene required')
            stage = self.steps[self.state['stage']]
            if 'sequence' not in MODES[self.state['mode']][1] or 'sequence' not in stage['tools']:
                raise ValueError('Sequence is not registered for this scene')
            if self.state['steps'][stage['id']]['failures'] >= 2 and not plan_revision:
                raise ValueError('Repeated failure requires a concrete revised plan')
            age = self.wall()-self.state['observation_wall']
            if age > (10 if self.state['urgent'] else 30):
                raise ValueError('Screen is too old to start this action')
            navigation = self.state['mode'] == 'navigation' and not self.state['urgent'] and not self.state['paused']
            prepared = validate_sequence(steps, max_seconds=30 if navigation else 5)
            if navigation and sum(s['seconds'] for s in prepared) > 5:
                movement = {'w', 'a', 's', 'd', 'shift', 'space', 'dodge'}
                if any(set(s['keys'])-movement or s.get('cursor') for s in prepared):
                    raise ValueError('Extended navigation plans allow movement and camera steps only')
            if self.state['paused']:
                # A paused menu is still interactive. Permit one short menu action,
                # then require its new screen/result before another input.
                menu = prepared[0]
                if (len(prepared) != 1 or self.state['mode'] not in ('paused', 'interaction')
                        or menu['keys'] not in (['escape'], ['enter'], ['b'], ['attack'])
                        or menu['seconds'] > .3
                        or ((menu['dx'] or menu['dy']) and (menu['keys'] != ['attack'] or not menu.get('cursor')))
                        or (menu['keys'] == ['attack'] and not menu.get('cursor'))
                        or repeat_dialogue):
                    raise ValueError('Paused scenes allow one short menu action only')
            if type(repeat_dialogue) is not bool:
                raise ValueError('repeat_dialogue must be boolean')
            if repeat_dialogue:
                if self.state['mode'] != 'dialogue' or self.state['urgent']:
                    raise ValueError('Repetition requires a nonurgent dialogue scene')
                # One canonical click cycle; no movement, arbitrary macros or held buttons.
                if (len(prepared) != 2 or prepared[0]['keys'] != ['attack']
                        or prepared[1]['keys'] or not .05 <= prepared[0]['seconds'] <= .15
                        or not .4 <= prepared[1]['seconds'] <= 2
                        or any(s['dx'] or s['dy'] for s in prepared)
                        or prepared[1].get('cursor')):
                    raise ValueError('Dialogue needs a short click followed by a 0.4..2s release')
            self._reply('run')
            if plan_revision:
                nonempty(plan_revision, 'revised plan')
                self.state['steps'][stage['id']]['failures'] = 0
            # The action may close the menu. Do not keep claiming the world is
            # paused while its next screen has not been observed.
            if self.state['paused'] or any(
                    key in ('b', 'escape') for step in prepared for key in step['keys']):
                # Menu toggles invalidate the earlier B-to-pause observation.
                # A watchdog B could undo the menu we just opened. Re-arm only
                # after the operator observes and registers the new screen.
                self.state['pause_available'] = False
            self.state['paused'] = False
            self.state['pending_result'] = dict(intent=intent, expected=expected)
            self._event('action_planned', steps=prepared, intent=intent, expected=expected,
                        plan_revision=plan_revision, repeat_dialogue=repeat_dialogue)
            self._launch(prepared, 'dialogue' if repeat_dialogue else 'sequence')
            return self.report()

    def start_assist(self, *, slots, seconds=120, allow_approach=False, require_target=True, use_food=False):
        with self.lock:
            self._idle_input()
            self._require_agent()
            if self.assistance_factory is None:
                raise ValueError('Local HUD assistance is not configured')
            if self.state['mode'] != 'combat' or self.state['stopped'] or self.state['paused']:
                raise ValueError('Register a current playable combat scene first')
            if 'sequence' not in self.steps[self.state['stage']]['tools']:
                raise ValueError('Local input is not registered for this stage')
            if self.wall()-self.state['observation_wall'] > 30:
                raise ValueError('Fresh combat observation required')
            if self.state['pending_result']:
                raise ValueError('Record the prior action result first')
            if any(type(v) is not bool for v in (allow_approach, require_target, use_food)):
                raise ValueError('Combat scope flags must be boolean')
            session = self.assistance_factory(self.factory(), context=self.state['plan_id'],
                slots=slots, seconds=seconds, allow_approach=allow_approach,
                require_target=require_target, verified_pause=self.state['pause_available'], use_food=use_food)
            self.cancel = threading.Event()
            self.executor = session
            self.state['owner'] = 'combat'
            self.deadline = None
            self._reply('start_assist')
            try:
                session.start()
            except Exception:
                session.stop()
                self.state['owner'] = None
                raise
            self.state['pending_result'] = dict(intent='local combat assistance', expected='observed encounter outcome')
            self._event('assistance_started', slots=slots, seconds=seconds)
            self.worker = threading.Thread(target=self._watch_assist, args=(session,self.cancel), daemon=True)
            self.worker.start()
            return self.report()

    def _watch_assist(self, session, cancel):
        cursor = 0
        try:
            while True:
                if cancel.is_set():
                    session.stop()
                report = session.report(cursor)
                cursor = report['cursor']
                with self.lock:
                    self.state['assistance'] = {k: report[k] for k in ('state','frame','signals','world_paused')}
                    for event in report['events']:
                        if event['kind'] != 'observation':
                            self._event('assistance_event', event=event)
                if report['state'] != 'running' and not report['owns_input']:
                    break
                time.sleep(.05)
        except Exception as error:
            session.stop()
            with self.lock:
                self.state.update(stopped=True, alert='assistance_error')
                self._event('assistance_error', error=str(error))
        finally:
            session.join()
            with self.lock:
                self.state['owner'] = None
                self.state['stopped'] = True
                self.waiting_since = self.clock()
                self._event('assistance_released')

    def check_dialogue(self, *, active, evidence, captured_wall):
        """Optional observation or explicit mode-off; mode-on does not need renewal."""
        with self.lock:
            self._require_agent()
            if type(active) is not bool:
                raise ValueError('active must be boolean')
            nonempty(evidence, 'dialogue screen evidence')
            if self.state['owner'] != 'dialogue':
                raise RuntimeError('No active dialogue stream')
            if not active:
                return self.stop('dialogue_ended_or_uncertain')
            age = self.wall()-captured_wall if type(captured_wall) in (int, float) else -1
            if self.state['stopped'] or self.cancel.is_set():
                raise RuntimeError('Dialogue stopped; start again from a fresh scene')
            if not 0 <= age < 30 or captured_wall <= self.state['observation_wall']:
                raise ValueError('A newer dialogue capture less than 30s old is required')
            self.state.update(evidence=evidence, observation_wall=captured_wall)
            self._event('dialogue_checked', evidence=evidence, captured_wall=captured_wall)
            return self.report()

    def begin_external(self, *, task_id, intent, expected, observation_only=False):
        """Reserve the same input owner for a managed adapter task."""
        with self.lock:
            self._idle_input()
            self._require_agent()
            for value,label in ((task_id,'adapter task'),(intent,'intent'),(expected,'expected result')):
                nonempty(value,label)
            if type(observation_only) is not bool:
                raise ValueError('observation_only must be boolean')
            if self.state['stopped'] or (self.state['paused'] and not observation_only) or not self.state['stage'] or self.state['pending_result']:
                raise RuntimeError('Resolve prior input and register a fresh unpaused scene first')
            if self.wall()-self.state['observation_wall'] > 30:
                raise ValueError('Fresh observation required before delegation')
            if not observation_only and ('sequence' not in MODES[self.state['mode']][1] or 'sequence' not in self.steps[self.state['stage']]['tools']):
                raise ValueError('This stage does not permit automated execution')
            self.cancel = threading.Event()
            self.executor = None
            self.deadline = None
            self.waiting_since = None
            self.state.update(owner='external', external_task=task_id, paused=self.state['paused'] if observation_only else False,
                pending_result=dict(intent=intent,expected=expected))
            self._event('external_started',task_id=task_id,intent=intent,expected=expected,observation_only=observation_only)
            return self.report()

    def end_external(self, *, task_id, error=None):
        with self.lock:
            if self.state['owner'] != 'external' or self.state.get('external_task') != task_id:
                raise ValueError('Adapter task does not own input')
            self.state.update(owner=None,external_task=None)
            if error:
                self.state.update(stopped=True,alert='external_error')
            self.waiting_since=self.clock()
            self.wait_warned=False
            self._event('external_returned',task_id=task_id,error=error,outcome='unverified')
            return self.report()

    def begin_direct(self, *, intent, expected):
        """Reserve the same input owner for one official Computer Use action."""
        with self.lock:
            self._idle_input()
            self._require_agent()
            nonempty(intent, 'direct action intent')
            nonempty(expected, 'expected visible result')
            if self.state['stopped'] or not self.state['stage']:
                raise RuntimeError('Register a fresh scene before direct input')
            if self.state['pending_result']:
                raise RuntimeError('Record the previous action result before another action')
            if self.wall()-self.state['observation_wall'] > 30:
                raise ValueError('Fresh screen required before direct input')
            if 'computer_use' not in self.steps[self.state['stage']]['tools']:
                raise ValueError('Direct Computer Use is not registered for this stage')
            self._reply('begin_direct')
            self.state.update(owner='computer_use', paused=False,
                              pending_result=dict(intent=intent, expected=expected))
            self.deadline = self.clock()+10
            self._event('direct_input_started', intent=intent, expected=expected)
            return self.report()

    def end_direct(self, *, error=None):
        with self.lock:
            if self.state['owner'] != 'computer_use':
                raise ValueError('No direct input lease')
            self.state['owner'] = None
            self.deadline = None
            if error:
                self.state.update(stopped=True, alert='direct_input_error')
            self._event('direct_input_returned', error=error, outcome='unverified')
            self.waiting_since = self.clock()
            self.wait_warned = False
            if not error and not self.state['stopped'] and self.state['urgent']:
                self._protect('urgent_direct_input_finished')
            return self.report()

    def _launch(self, steps, owner):
        self.cancel = threading.Event()
        self.executor = self.factory()
        self.state['owner'] = owner
        self.input_progress = dict(kind=owner, state='running', total_steps=len(steps),
                                   current_step=0, completed_steps=0, cycles=0)
        self.deadline = None if owner == 'dialogue' else self.clock()+sum(s['seconds'] for s in steps)+.5
        if owner == 'dialogue':
            self.waiting_since = None
        cancel, executor = self.cancel, self.executor
        def execute():
            error = None
            started = self.clock()
            try:
                index = 0
                def cycle():
                    nonlocal index
                    for step in steps:
                        if cancel.is_set():
                            break
                        at = self.clock()
                        with self.lock:
                            self.input_progress.update(current_step=index % len(steps)+1,
                                                       completed_steps=index % len(steps), cycles=index // len(steps))
                            self._event('input_step_started', index=index, step=step, tool=owner)
                        executor.act(**step)
                        with self.lock:
                            if not cancel.is_set():
                                self.input_progress.update(completed_steps=index % len(steps)+1)
                            self._event('input_step_returned', index=index, tool=owner,
                                        duration_seconds=self.clock()-at)
                        index += 1
                if owner == 'dialogue':
                    from .dialogue_clicker import repeat_dialogue
                    # The validated cycle already releases for >=0.4s. Keep an
                    # independent period floor even if an executor returns early.
                    repeat_dialogue(cycle, cancel, interval=None, minimum_period=.4)
                else:
                    cycle()
            except CancelledError as failure:
                if not cancel.is_set():
                    error = str(failure)
            except Exception as failure:
                error = str(failure)
            finally:
                with self.lock:
                    self.state['owner'] = None
                    self.deadline = None
                    if error:
                        self.state.update(stopped=True, alert='input_error')
                    self.input_progress.update(state='failed' if error else 'cancelled' if cancel.is_set() else 'awaiting_verification')
                    self._event('input_released', tool=owner, error=error,
                                cancelled=cancel.is_set(), duration_seconds=self.clock()-started,
                                outcome='input_error' if error else 'cancelled' if cancel.is_set() else 'unverified')
                    self.waiting_since = self.clock()
                    self.wait_warned = False
                    if not error and owner == 'sequence' and self.state['urgent'] and not cancel.is_set():
                        # A finished chunk must not expose the game during model reasoning.
                        self._protect('urgent_chunk_finished')
        self.worker = threading.Thread(target=execute, daemon=True)
        self.worker.start()

    def result(self, *, outcome, evidence, actor='agent'):
        with self.lock:
            self._idle_input()
            if outcome not in ('success', 'failure', 'unknown') or actor not in ('agent', 'user'):
                raise ValueError('Invalid result')
            nonempty(evidence, 'result evidence')
            if not self.state['pending_result']:
                raise ValueError('No action awaiting a result')
            self._reply('result')
            if outcome == 'failure':
                record = self.state['steps'][self.state['stage']]
                record['failures'] += 1
                record['no_progress_failures'] = record.get('no_progress_failures', 0) + 1
            elif outcome == 'success':
                self.state['steps'][self.state['stage']]['no_progress_failures'] = 0
            action = self.state['pending_result']
            # An animation/loading frame is not a terminal result. Keep the same
            # action pending so a later observation can resolve it without new input.
            if outcome != 'unknown':
                self.state['pending_result'] = None
            self.state['steps'][self.state['stage']]['last_outcome'] = outcome
            self._event('action_result', outcome=outcome, evidence=evidence, actor=actor, action=action)
            settings = self.state['settings']
            if self.state['steps'][self.state['stage']].get('no_progress_failures', 0) >= NO_PROGRESS_FAILURE_LIMIT:
                self.set_control(target='user', reason='no_progress_limit')
            if (outcome == 'failure' and self.state['mode'] == 'combat'
                    and settings['combat'] == 'on_failure'
                    and self.state['steps'][self.state['stage']]['failures'] >= settings['failure_limit']):
                self.set_control(target='user', reason='combat_failures')
            self.waiting_since = self.clock()
            return self.report()

    def complete(self, *, stage, evidence, actor='agent'):
        with self.lock:
            self._idle_input()
            if stage != self.state['stage'] or actor not in ('agent', 'user'):
                raise ValueError('Complete the observed active stage only')
            if self.state['pending_result']:
                raise RuntimeError('Record the action result before completing its stage')
            if self.state['steps'][stage].get('last_outcome') in ('failure', 'unknown'):
                raise RuntimeError('The last action was not successful; verify a successful result before completion')
            nonempty(evidence, 'completion evidence matching plan criterion')
            self._account()
            self._reply('complete')
            self.state['steps'][stage].update(status='completed', evidence=evidence, actor=actor)
            self.state['stopped'] = True
            self._event('stage_completed', stage=stage, evidence=evidence, actor=actor)
            return self.report()

    def rollback(self, *, stages, evidence):
        with self.lock:
            self._idle_input()
            nonempty(evidence, 'rollback evidence')
            if not stages or any(s not in self.steps for s in stages):
                raise ValueError('Explicit known lost stages required')
            self._account()
            for stage in stages:
                self.state['steps'][stage].update(status='lost', evidence=evidence)
            self.state.update(stopped=True, paused=False, pause_available=False,
                              pending_result=None, alert='rollback_requires_observation')
            self._event('progress_lost', stages=stages, evidence=evidence)
            return self.report()

    def stop(self, reason='user_stop'):
        with self.lock:
            self._account()
            if reason == 'user_stop':
                self.state['control'] = 'user'
            self.cancel.set()
            if self.executor:
                self.executor.stop()
            self.state.update(stopped=True, alert=reason)
            self._event('stop_requested', reason=reason, world_paused=self.state['paused'])
            return self.report()

    def tick(self):
        with self.lock:
            now = self.clock()
            self.tick_count += 1
            self.max_tick_gap = max(self.max_tick_gap, now-self.last_tick)
            self.last_tick = now
            if self.deadline is not None and now >= self.deadline:
                self.stop('input_deadline_exceeded')
                self.deadline = None
            budget = 2 if self.state['urgent'] and not self.state['paused'] else MODES[self.state['mode']][0]
            if (self.state['owner'] not in ('dialogue', 'combat', 'external') and self.waiting_since is not None
                    and not self.wait_warned and now-self.waiting_since >= budget):
                self.wait_warned = True
                self._event('supervisor_overdue', gap_seconds=now-self.waiting_since, budget_seconds=budget)
                # Waiting alone is not evidence of danger or permission to change scenes.
                if not self.state['stopped'] and self.state['urgent']:
                    self._protect('supervisor_overdue')

    def _watch(self):
        while not self.closed.wait(self.period):
            try:
                self.tick()
            except Exception:
                self.cancel.set()
                if self.executor:
                    self.executor.stop()
                self.state.update(stopped=True, alert='watchdog_error')
                return

    def report(self, after_sequence=0):
        with self.lock:
            if type(after_sequence) is not int or after_sequence < 0:
                raise ValueError('Nonnegative cursor required')
            now = self.clock()
            state = deepcopy(self.state)
            state['session_seconds'] += now-self.scene_at
            if state['stage'] and state['steps'][state['stage']]['status'] == 'active':
                state['steps'][state['stage']]['seconds'] += now-self.scene_at
            state['mode_seconds'][state['mode']] += now-self.scene_at
            if state['urgent'] and not state['paused']:
                state['unprotected_seconds'] += now-self.scene_at
            gaps = sorted(state.pop('supervisor_gaps'))
            current = next((i for i, step in enumerate(self.plan['steps'])
                            if step['id'] == state['stage']), 0)
            return dict(**state, cursor=self.sequence, input_progress=deepcopy(self.input_progress),
                plan_title=self.plan.get('title', self.plan['id']),
                stage_title=self.steps[state['stage']]['title'] if state['stage'] else None,
                plan_steps=[dict(id=s['id'], title=s['title']) for s in self.plan['steps']],
                handoff_pending=state['control']=='user' and state['owner'] is not None,
                host_elapsed_seconds=now-self.started,
                supervisor_wait_seconds=None if self.waiting_since is None else now-self.waiting_since,
                supervisor_overdue=bool(self.wait_warned and self.waiting_since is not None and not state['stopped']),
                capture_pending_seconds=None if self.capture_since is None else now-self.capture_since,
                gap_p95_seconds=gaps[int((len(gaps)-1)*.95)] if gaps else None,
                gap_max_seconds=max(gaps, default=None),
                watchdog=dict(ticks=self.tick_count, max_gap_seconds=self.max_tick_gap,
                              alive=bool(self.timer and self.timer.is_alive())),
                progress=dict(completed=sum(s['status']=='completed' for s in state['steps'].values()),
                              total=len(self.steps), unit='registered_steps', time_percentage=None),
                next_steps=[s for s in self.plan['steps'][current:]
                            if state['steps'][s['id']]['status']!='completed'][:3],
                unverified_earlier_steps=[s['id'] for s in self.plan['steps'][:current]
                                         if state['steps'][s['id']]['status']!='completed'],
                events=[deepcopy(e) for e in self.events if e['sequence'] > after_sequence],
                history_lost=bool(self.events and after_sequence < self.events[0]['sequence']-1))

    def close(self):
        self.closed.set()
        self.stop('host_shutdown')
        if self.worker:
            self.worker.join(1)
        if self.timer:
            self.timer.join(1)
