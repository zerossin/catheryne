from copy import deepcopy
import json
from pathlib import Path
import tempfile
import threading
import time
import unittest

from story_control.director import StoryDirector
from story_control.service import StoryService, HostLock


PLAN = dict(id='test', steps=[dict(id='a', title='Test step',
    modes=['navigation', 'dialogue', 'combat', 'stealth', 'interaction', 'escape'],
    tools=['sequence', 'computer_use'], completion='visible next objective',
    checkpoint='instance', failure_cost='lost attempt')])


class FakeExecutor:
    def __init__(self):
        self.actions = []
        self.cancel = threading.Event()
    def act(self, **step):
        self.actions.append(step)
        self.cancel.wait(step['seconds'])
    def stop(self):
        self.cancel.set()


class DirectorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name)/'events.jsonl'
        self.now = [100.]
        self.executors = []
        def factory():
            executor = FakeExecutor()
            self.executors.append(executor)
            return executor
        self.factory = factory
        self.director = StoryDirector(PLAN, self.path, factory, clock=lambda: self.now[0],
                                      wall=lambda: self.now[0], threaded=False)
        self.addCleanup(self.director.close)

    def test_no_progress_budget_survives_revisions(self):
        self.observe()
        self.director.set_control(target='agent')
        for attempt in range(6):
            self.observe()
            self.director.run(steps=[dict(keys=['w'], seconds=.05)],
                              intent='synthetic approach', expected='visible progress',
                              plan_revision='different method %s' % attempt)
            self.join()
            report = self.director.result(outcome='failure', evidence='same obstacle remains')
            self.assertEqual(report['steps']['a']['no_progress_failures'], attempt + 1)
        self.assertEqual(report['control'], 'user')
        self.assertEqual(report['alert'], 'no_progress_limit')
        self.assertIsNone(report['owner'])
        self.assertRaisesRegex(RuntimeError, 'no_progress_limit', self.director.run,
            steps=[dict(keys=['w'], seconds=.05)], intent='reworded', expected='progress',
            plan_revision='another description')

    def test_progress_resets_streak_but_unknown_wait_does_not_count(self):
        self.observe()
        self.director.set_control(target='agent')
        for outcome in ('failure', 'success', 'unknown', 'success'):
            self.observe()
            self.director.run(steps=[dict(keys=['w'], seconds=.05)],
                              intent='synthetic action', expected='visible next position')
            self.join()
            report = self.director.result(outcome=outcome, evidence='fresh fixture evidence')
            self.assertEqual(report['steps']['a'].get('no_progress_failures', 0),
                             1 if outcome == 'failure' else 0)
            if outcome == 'unknown':
                self.assertIsNotNone(report['pending_result'])
                self.director.result(outcome='success', evidence='loading finished, verified position')

    def observe(self, **kwargs):
        return self.director.observe(stage='a', mode=kwargs.pop('mode', 'navigation'),
            evidence='test fixture, not a game screen', captured_wall=self.now[0], **kwargs)

    def join(self):
        end = time.perf_counter()+2
        while self.director.worker and self.director.worker.is_alive() and time.perf_counter()<end:
            self.director.worker.join(.1)
        self.assertIsNone(self.director.state['owner'])

    def start_dialogue(self):
        self.observe(mode='dialogue')
        return self.director.run(steps=[dict(keys=['attack'], seconds=.05),
            dict(keys=[], seconds=.4)], intent='advance text and choices',
            expected='dialogue ends', repeat_dialogue=True)

    def test_paused_menu_can_close_without_claiming_world_remains_paused(self):
        self.observe(mode='paused', paused=True)
        self.director.run(steps=[dict(keys=['escape'], seconds=.05)],
                          intent='close observed menu', expected='next screen')
        self.join()
        self.assertEqual(self.executors[0].actions[0]['keys'], ['escape'])
        self.assertFalse(self.director.report()['paused'])
        self.assertIsNotNone(self.director.report()['pending_result'])

    def test_menu_toggle_disarms_watchdog_until_new_observation(self):
        for key in ('b', 'escape'):
            self.observe(mode='interaction', pause_available=True)
            before = len(self.executors)
            self.director.run(steps=[dict(keys=[key], seconds=.05)],
                              intent='open menu', expected='paused menu')
            self.join()
            self.now[0] += 11
            self.director.tick()
            self.join()
            self.assertEqual(len(self.executors), before + 1)
            self.assertFalse(self.director.report()['pause_available'])
            self.assertFalse(self.director.report()['paused'])
            self.assertFalse(self.director.report()['stopped'])
            self.director.result(outcome='success', evidence='menu observed later')
        # A later urgent field observation can arm protection again.
        self.observe(mode='interaction', pause_available=True, urgent=True)
        self.now[0] += 11
        self.director.tick()
        self.join()
        self.assertEqual(self.executors[-1].actions[0]['keys'], ['b'])

    def test_paused_map_allows_one_positioned_drag(self):
        self.observe(mode='paused', paused=True)
        self.director.run(steps=[dict(keys=['attack'], seconds=.1, dx=200, dy=100,
            cursor=dict(x=300, y=200, width=1280, height=720))],
            intent='pan observed map', expected='map reveals destination')
        self.join()
        self.assertEqual(self.executors[0].actions[0]['dx'], 200)
        self.assertIsNotNone(self.director.report()['pending_result'])

    def test_paused_menu_rejects_gameplay_and_chained_inputs(self):
        self.observe(mode='paused', paused=True)
        for steps in ([dict(keys=['w'], seconds=.05)],
                      [dict(keys=['attack'], seconds=.05)],
                      [dict(keys=['escape'], seconds=.05)] * 2):
            with self.assertRaisesRegex(ValueError, 'short menu action'):
                self.director.run(steps=steps, intent='invalid input', expected='none')
        self.director.stop('user')
        with self.assertRaisesRegex(RuntimeError, 'Fresh playable'):
            self.director.run(steps=[dict(keys=['escape'], seconds=.05)],
                              intent='must remain stopped', expected='none')
        self.assertEqual(len(self.executors), 0)

    def test_failed_or_unknown_result_cannot_complete_stage(self):
        for outcome in ('failure', 'unknown'):
            self.observe()
            self.director.run(steps=[dict(keys=['w'], seconds=.05)],
                              intent='test move', expected='target visible')
            self.join()
            self.director.result(outcome=outcome, evidence='target not verified')
            with self.assertRaisesRegex(RuntimeError, 'not successful|Record the action result'):
                self.director.complete(stage='a', evidence='unsupported completion')
        # Resolve the pending unknown from a later frame without replaying input.
        count = len(self.executors)
        self.now[0] += 1
        self.observe()
        self.director.result(outcome='success', evidence='target visible')
        self.assertEqual(len(self.executors), count)
        self.director.complete(stage='a', evidence='target verified')
        self.assertEqual(self.director.report()['progress']['completed'], 1)

    def test_dialogue_continues_during_capture_and_stops_on_end(self):
        self.start_dialogue()
        time.sleep(.5)
        self.assertGreaterEqual(len(self.executors[0].actions), 3)
        self.director.capture_started()
        self.director.capture_finished()
        self.now[0] += 12
        self.director.tick()
        self.assertEqual(self.director.report()['owner'], 'dialogue')
        self.assertFalse(self.director.report()['stopped'])
        self.director.check_dialogue(active=False, evidence='HUD returned', captured_wall=100.)
        self.join()
        count = len(self.executors[0].actions)
        time.sleep(.1)
        self.assertEqual(count, len(self.executors[0].actions))
        self.assertIsNotNone(self.director.report()['pending_result'])
        self.assertEqual(self.director.report()['progress']['completed'], 0)

    def test_dialogue_mode_stays_on_without_model_renewals(self):
        self.start_dialogue()
        self.now[0] = 400
        self.director.tick()
        time.sleep(.1)
        self.assertIsNone(self.director.deadline)
        self.assertEqual(self.director.report()['owner'], 'dialogue')
        self.assertFalse(self.executors[0].cancel.is_set())
        self.director.check_dialogue(active=False, evidence='mode off', captured_wall=0)
        self.join()
        self.assertTrue(self.executors[0].cancel.is_set())

    def test_optional_dialogue_observation_cannot_revive_stopped_mode(self):
        self.start_dialogue()
        self.now[0] = 125
        self.director.check_dialogue(active=True, evidence='ordinary dialogue', captured_wall=120.)
        self.assertIsNone(self.director.deadline)
        self.director.stop()
        with self.assertRaises(RuntimeError):
            self.director.check_dialogue(active=True, evidence='stopped', captured_wall=125.)
        self.join()

    def test_normal_dialogue_cancellation_is_not_input_failure(self):
        from concurrent.futures import CancelledError
        executor = FakeExecutor()
        def act(**step):
            executor.cancel.wait(1)
            if executor.cancel.is_set():
                raise CancelledError('Session stop requested')
        executor.act = act
        self.director.factory = lambda: executor
        self.start_dialogue()
        self.director.check_dialogue(active=False, evidence='mode off', captured_wall=0)
        self.join()
        self.assertEqual(self.director.report()['alert'], 'dialogue_ended_or_uncertain')
        events = [json.loads(line) for line in self.path.read_text().splitlines()]
        released = next(e for e in reversed(events) if e['kind'] == 'input_released')
        self.assertEqual(released['data']['outcome'], 'cancelled')
        self.assertIsNone(released['data']['error'])

    def test_dialogue_does_not_allow_movement_or_combat(self):
        self.observe(mode='dialogue')
        with self.assertRaises(ValueError):
            self.director.run(steps=[dict(keys=['w'], seconds=.1)], intent='bad',
                              expected='bad', repeat_dialogue=True)
        self.observe(mode='combat')
        with self.assertRaises(ValueError):
            self.director.run(steps=[dict(keys=['attack'], seconds=.05),dict(keys=[],seconds=.4)],
                              intent='bad', expected='bad', repeat_dialogue=True)
        self.assertEqual(self.executors, [])

    def test_dialogue_choice_anchor_repeats_without_model_renewal(self):
        self.observe(mode='dialogue')
        anchor=dict(x=900,y=390,width=1280,height=720)
        self.director.run(steps=[dict(keys=['attack'],seconds=.05,cursor=anchor),
            dict(keys=[],seconds=.4)],intent='ordinary story choice',
            expected='dialogue continues',repeat_dialogue=True)
        time.sleep(.55)
        self.director.capture_started()
        self.director.capture_finished()
        self.now[0] += 300
        self.director.tick()
        self.assertEqual(self.director.report()['owner'],'dialogue')
        clicks=[s for s in self.executors[0].actions if s['keys']]
        self.assertGreaterEqual(len(clicks),2)
        self.assertTrue(all(s['cursor']==anchor for s in clicks))
        self.director.set_control(target='user')
        self.join()
        count=len(self.executors[0].actions)
        time.sleep(.1)
        self.assertEqual(len(self.executors[0].actions),count)

    def test_dialogue_anchor_cannot_move_on_release_or_outside_client(self):
        self.observe(mode='dialogue')
        for steps in ([dict(keys=['attack'],seconds=.05),
                       dict(keys=[],seconds=.4,cursor=dict(x=20,y=20,width=1280,height=720))],
                      [dict(keys=['attack'],seconds=.05,cursor=dict(x=1280,y=20,width=1280,height=720)),
                       dict(keys=[],seconds=.4)]):
            with self.assertRaises(ValueError):
                self.director.run(steps=steps,intent='invalid',expected='invalid',repeat_dialogue=True)
        self.assertEqual(self.executors,[])

    def test_input_journal_keeps_timings_without_repeated_state_and_recovers(self):
        self.observe()
        self.director.run(steps=[dict(keys=['f'],seconds=.05)],intent='interact',expected='dialogue')
        self.join()
        events=[json.loads(line) for line in self.path.read_text().splitlines()]
        step_events=[e for e in events if e['kind'].startswith('input_step_')]
        self.assertEqual(len(step_events),2)
        self.assertTrue(all('state' not in e for e in step_events))
        self.assertIn('duration_seconds',step_events[-1]['data'])
        # A process could stop after writing a step event. Replay must retain the
        # preceding full state and never resume input from this partial suffix.
        last_step=events.index(step_events[-1])
        recovery=self.path.with_name('recovery.jsonl')
        recovery.write_text('\n'.join(json.dumps(e) for e in events[:last_step+1])+'\n')
        restored=StoryDirector(PLAN,recovery,self.factory,clock=lambda:100.,wall=lambda:100.,threaded=False)
        try:
            self.assertEqual(restored.report()['pending_result']['intent'],'interact')
            self.assertTrue(restored.report()['stopped'])
            self.assertIsNone(restored.report()['owner'])
        finally:
            restored.close()

    def test_map_observation_preserves_pause_and_exclusive_reservation(self):
        self.observe(mode='paused', paused=True)
        with self.assertRaises(RuntimeError):
            self.director.begin_external(task_id='move', intent='move', expected='destination')
        self.director.begin_external(task_id='map', intent='read map center', expected='coordinates', observation_only=True)
        self.assertTrue(self.director.report()['paused'])
        self.assertEqual(self.executors, [])
        with self.assertRaises(RuntimeError):
            self.director.run(steps=[dict(keys=['w'], seconds=.05)], intent='competing', expected='blocked')
        self.director.end_external(task_id='map')
        self.assertTrue(self.director.report()['paused'])
        self.assertIsNotNone(self.director.report()['pending_result'])
        self.director.result(outcome='success', evidence='coordinates returned for observed center')
        self.assertTrue(self.director.report()['paused'])

    def test_delegation_keeps_exclusive_owner_until_adapter_confirms_exit(self):
        self.observe()
        self.director.begin_external(task_id='adapter-1',intent='registered route',expected='destination visible')
        self.now[0]+=120
        self.director.tick()
        self.assertEqual(self.director.report()['owner'],'external')
        self.observe(mode='navigation')
        self.assertEqual(self.director.report()['external_task'], 'adapter-1')
        self.assertFalse(self.director.report()['stopped'])
        with self.assertRaises(RuntimeError):
            self.director.run(steps=[dict(keys=['w'],seconds=.05)],intent='competing',expected='bad')
        with self.assertRaises(ValueError):
            self.director.end_external(task_id='other-task')
        self.director.set_control(target='user')
        self.assertEqual(self.director.report()['owner'],'external')
        self.assertTrue(self.director.report()['stopped'])
        self.director.end_external(task_id='adapter-1')
        self.assertIsNone(self.director.report()['owner'])
        self.assertIsNotNone(self.director.report()['pending_result'])
        self.assertEqual(self.director.report()['control'],'user')

    def test_dialogue_observation_keeps_continuous_executor_and_requires_release_to_exit(self):
        self.start_dialogue()
        worker = self.director.worker
        self.now[0] += 120
        self.director.tick()
        self.observe(mode='dialogue')
        self.assertIs(self.director.worker, worker)
        with self.assertRaises(RuntimeError):
            self.observe(mode='dialogue', dialogue_executor='bettergi')
        self.assertEqual(self.director.report()['owner'], 'dialogue')
        self.assertFalse(self.director.report()['stopped'])
        with self.assertRaises(RuntimeError):
            self.observe(mode='navigation')
        self.director.stop('dialogue_state_changed')
        self.join()
        self.director.result(outcome='success', evidence='dialogue ended on fresh observation')
        self.observe(mode='navigation')
        self.assertIsNone(self.director.report()['owner'])

    def test_dialogue_executor_choice_survives_observation_until_state_exit(self):
        self.observe(mode='dialogue', dialogue_executor='manual')
        self.observe(mode='dialogue')
        self.assertEqual(self.director.report()['dialogue_executor'], 'manual')
        self.observe(mode='dialogue', dialogue_executor='bettergi')
        self.observe(mode='dialogue')
        self.assertEqual(self.director.report()['dialogue_executor'], 'bettergi')
        self.observe(mode='navigation')
        self.observe(mode='dialogue')
        self.assertEqual(self.director.report()['dialogue_executor'], 'repeat')
        with self.assertRaises(ValueError):
            self.observe(mode='dialogue', dialogue_executor='unknown')

    def test_replan_preserves_task_and_requires_old_execution_to_release(self):
        self.observe()
        self.director.begin_external(task_id='route-1', intent='travel', expected='arrival')
        self.director.stop('agent_replan')
        self.assertEqual(self.director.report()['control'], 'agent')
        self.assertEqual(self.director.report()['owner'], 'external')
        with self.assertRaises(RuntimeError):
            self.director.begin_external(task_id='route-2', intent='detour', expected='arrival')
        self.director.end_external(task_id='route-1', error='interrupted for replanning')
        self.director.result(outcome='failure', evidence='route blocked')
        self.observe()
        self.director.begin_external(task_id='route-2', intent='detour', expected='arrival')
        self.assertEqual(self.director.report()['external_task'], 'route-2')
        self.assertFalse(self.director.report()['stopped'])

    def test_delegation_requires_fresh_evidence_and_enabled_input(self):
        self.observe()
        with self.assertRaises(ValueError):
            StoryService(self.director).call('begin_external',dict(task_id='a',intent='route',expected='destination'))
        self.now[0]+=31
        with self.assertRaises(ValueError):
            self.director.begin_external(task_id='a',intent='route',expected='destination')
        self.assertIsNone(self.director.report()['owner'])

    def test_external_cannot_start_from_paused_menu(self):
        self.observe(paused=True)
        with self.assertRaises(RuntimeError):
            self.director.begin_external(task_id='route', intent='travel', expected='destination')
        self.assertIsNone(self.director.report()['owner'])

    def test_batched_movement_returns_before_execution_without_competing_input(self):
        self.observe()
        start=time.perf_counter()
        self.director.run(steps=[dict(keys=['w'],seconds=.4),dict(keys=['d'],seconds=.4)],intent='bounded movement',expected='checkpoint')
        self.assertLess(time.perf_counter()-start,.3)
        self.assertEqual(self.director.report()['owner'],'sequence')
        worker = self.director.worker
        self.observe()
        self.assertIs(self.director.worker, worker)
        with self.assertRaises(RuntimeError):
            self.director.run(steps=[dict(keys=['a'],seconds=.1)],intent='competing',expected='bad')
        self.join()
        self.assertEqual([s['keys'] for s in self.executors[0].actions],[['w'],['d']])
        progress = StoryService(self.director).call('status', {}, view='compact')['input_progress']
        self.assertEqual(progress['completed_steps'], 2)
        self.assertEqual(progress['state'], 'awaiting_verification')
        report = StoryService(self.director).call('status', {}, view='compact')
        self.assertEqual(report['plan_id'], PLAN['id'])
        self.assertEqual(report['stage'], 'a')
        self.assertEqual(report['plan_steps'][0]['id'], 'a')
        self.assertEqual(report['steps']['a']['status'], 'active')
        self.assertIsNotNone(self.director.report()['pending_result'])

    def test_extended_navigation_is_continuous_observable_and_interruptible(self):
        self.observe()
        self.director.run(steps=[dict(keys=['w'],seconds=5),dict(keys=['w','d'],seconds=5)],
                          intent='visible-ground route', expected='next checkpoint')
        worker = self.director.worker
        self.observe()
        self.assertIs(self.director.worker, worker)
        self.assertEqual(self.director.report()['owner'], 'sequence')
        self.director.stop('agent_replan')
        self.join()
        self.assertIsNone(self.director.report()['owner'])
        self.assertEqual(self.director.report()['control'], 'agent')
        self.assertEqual(self.director.report()['pending_result']['intent'], 'visible-ground route')

    def test_extended_navigation_does_not_expand_interactions_or_urgent_input(self):
        for mode, urgent, keys, count in [('navigation',False,['b'],2),
                ('navigation',False,['attack'],2), ('interaction',False,['w'],2),
                ('navigation',True,['w'],2), ('navigation',False,['w'],7)]:
            with self.subTest(mode=mode, urgent=urgent, keys=keys, count=count):
                self.observe(mode=mode, urgent=urgent)
                with self.assertRaises(ValueError):
                    self.director.run(steps=[dict(keys=keys,seconds=5)]*count,
                                      intent='invalid long action', expected='rejected')
                self.assertIsNone(self.director.report()['owner'])

    def test_dialogue_executor_error_stops_repetition(self):
        def fail(**step):
            raise RuntimeError('synthetic focus loss')
        def factory():
            executor = FakeExecutor()
            executor.act = fail
            return executor
        self.director.factory = factory
        self.start_dialogue()
        self.join()
        self.assertTrue(self.director.report()['stopped'])
        self.assertEqual(self.director.report()['alert'], 'input_error')
        self.assertIsNotNone(self.director.report()['pending_result'])

    def test_dialogue_exclusive_ownership_and_user_stop(self):
        self.start_dialogue()
        with self.assertRaises(RuntimeError):
            self.director.begin_direct(intent='other click', expected='bad')
        with self.assertRaises(RuntimeError):
            self.observe()
        self.director.stop()
        with self.assertRaises(RuntimeError):
            self.director.check_dialogue(active=True, evidence='cannot resume', captured_wall=101.)
        self.join()
        self.assertTrue(self.executors[0].cancel.is_set())
        with self.assertRaises(ValueError):
            StoryService(self.director).call('run', {'repeat_dialogue':True})

    def test_focus_uses_enabled_host_target_and_rejects_active_input(self):
        focused = []
        callback = lambda: focused.append(True)
        service = StoryService(self.director, focus_target=callback)
        with self.assertRaises(ValueError):
            service.call('focus', {})
        service = StoryService(self.director, True, callback)
        with self.assertRaises(ValueError):
            service.call('focus', {'target_exe':'other.exe'})
        self.director.state['owner'] = 'dialogue'
        with self.assertRaises(ValueError):
            service.call('focus', {})
        self.assertEqual(focused, [])
        self.director.state['owner'] = None
        self.assertTrue(service.call('focus', {})['focused'])
        self.assertEqual(focused, [True])

    def test_compact_reply_retains_safety_and_progress_without_history(self):
        self.observe(mode='dialogue')
        service = StoryService(self.director)
        full = service.call('status', {})
        compact = service.call('status', {}, view='compact')
        for key in ('owner', 'stopped', 'alert', 'pending_result', 'progress', 'watchdog', 'input_enabled'):
            self.assertEqual(compact[key], full[key])
        self.assertNotIn('events', compact)
        self.assertEqual(compact['steps'], {'a': {'status': 'active'}})
        self.assertEqual(compact['stage_status']['status'], 'active')
        # The full report and append-only evidence remain available.
        self.assertTrue(service.call('report', {})['events'])
        self.assertLess(len(json.dumps(compact)), len(json.dumps(full)))
        before = self.director.sequence
        with self.assertRaises(ValueError):
            service.call('stop', {}, view='invalid')
        self.assertEqual(before, self.director.sequence)

    def test_compact_action_response_keeps_pending_result_and_error_alert(self):
        self.observe()
        service = StoryService(self.director, enable_input=True)
        reply = service.call('run', dict(steps=[dict(keys=['f'],seconds=.05)],
            intent='interact', expected='dialogue visible'), view='compact')
        self.assertEqual(reply['pending_result']['expected'], 'dialogue visible')
        self.join()
        reply = service.call('stop', {}, view='compact')
        self.assertTrue(reply['stopped'])
        self.assertEqual(reply['alert'], 'user_stop')
        self.assertIsNotNone(reply['pending_result'])

    def test_watchdog_requires_no_supervisor_call(self):
        self.observe(mode='combat', urgent=True)
        self.now[0] += 2.1
        self.director.tick()
        report = self.director.report()
        self.assertTrue(report['stopped'])
        self.assertFalse(report['paused'])
        self.assertIn('supervisor_overdue', [e['kind'] for e in report['events']])

    def test_nonurgent_wait_never_opens_menu_or_stops_task(self):
        for mode in ('navigation', 'interaction', 'combat', 'recovery', 'stealth', 'escape'):
            with self.subTest(mode=mode):
                self.observe(mode=mode, pause_available=True)
                self.now[0] += 60
                self.director.tick()
                report = self.director.report()
                self.assertTrue(StoryService(self.director).call('status', {}, view='compact')['supervisor_overdue'])
                self.assertFalse(report['stopped'])
                self.assertIsNone(report['owner'])
                self.assertFalse(self.executors)
                self.assertIn('supervisor_overdue', [e['kind'] for e in report['events']])
                self.assertNotIn('pause_requested', [e['kind'] for e in report['events']])

    def test_new_observation_clears_overdue_signal(self):
        self.observe(mode='navigation')
        self.now[0] += 60
        self.director.tick()
        self.assertTrue(self.director.report()['supervisor_overdue'])
        self.observe(mode='navigation')
        self.assertFalse(self.director.report()['supervisor_overdue'])

    def test_status_poll_does_not_reset_thinking_clock(self):
        self.observe()
        self.now[0] += 5
        self.director.report()
        self.now[0] += 5
        self.assertEqual(self.director.report()['supervisor_wait_seconds'], 10)
        self.observe()
        self.assertEqual(self.director.report()['gap_max_seconds'], 10)

    def test_pause_requested_once_never_assumed_success(self):
        self.observe(mode='combat', pause_available=True, urgent=True)
        self.director.think()
        self.join()
        self.now[0] += 10
        self.director.tick()
        self.now[0] += 10
        self.director.tick()
        self.assertEqual(len(self.executors), 1)
        self.assertEqual(self.executors[0].actions[0]['keys'], ['b'])
        self.assertFalse(self.director.report()['paused'])
        self.assertTrue(self.director.report()['stopped'])

    def test_urgent_chunk_requests_pause_before_model_returns(self):
        self.observe(mode='combat', pause_available=True, urgent=True)
        self.director.run(steps=[dict(keys=['e'], seconds=.05)], intent='test', expected='cooldown')
        self.join()
        self.assertEqual([e.actions[0]['keys'] for e in self.executors], [['e'], ['b']])

    def test_context_switch_refused_until_input_released(self):
        self.observe()
        self.director.run(steps=[dict(keys=['w'], seconds=.3)], intent='walk', expected='closer')
        with self.assertRaises(RuntimeError):
            self.observe(mode='dialogue')
        self.director.stop()
        self.join()
        self.assertTrue(self.executors[0].cancel.is_set())

    def test_action_return_is_not_stage_completion(self):
        self.observe()
        self.director.run(steps=[dict(keys=['f'], seconds=.05)], intent='interact', expected='dialogue')
        self.join()
        self.assertEqual(self.director.report()['progress']['completed'], 0)
        self.director.result(outcome='success', evidence='dialogue appeared')
        self.assertEqual(self.director.report()['progress']['completed'], 0)
        self.director.complete(stage='a', evidence='next objective shown')
        self.assertEqual(self.director.report()['progress']['completed'], 1)
        self.assertIsNone(self.director.report()['progress']['time_percentage'])

    def test_repeat_failure_requires_revised_plan(self):
        self.observe()
        for _ in range(2):
            self.director.run(steps=[dict(keys=['w'], seconds=.05)], intent='pass', expected='other side')
            self.join()
            self.director.result(outcome='failure', evidence='same wall')
        with self.assertRaisesRegex(ValueError, 'revised plan'):
            self.director.run(steps=[dict(keys=['w'], seconds=.05)], intent='pass', expected='other side')

    def test_rollback_preserves_attempt_history_and_actor(self):
        self.observe(actor='user')
        self.now[0] += 12
        self.director.complete(stage='a', evidence='user reached next room', actor='user')
        self.director.rollback(stages=['a'], evidence='instance restart shown')
        report = self.director.report()
        self.assertEqual(report['steps']['a']['status'], 'lost')
        self.assertEqual(report['steps']['a']['actor'], 'user')
        self.assertEqual(report['steps']['a']['seconds'], 12)
        self.observe()
        self.assertEqual(self.director.report()['steps']['a']['attempts'], 2)

    def test_restart_restores_progress_but_never_input_or_pause(self):
        self.observe(paused=True)
        self.director.complete(stage='a', evidence='done')
        restored = StoryDirector(PLAN, self.path, self.factory, threaded=False)
        self.addCleanup(restored.close)
        self.assertEqual(restored.report()['progress']['completed'], 1)
        self.assertTrue(restored.report()['stopped'])
        self.assertFalse(restored.report()['paused'])

    def test_different_plan_cannot_reuse_ledger(self):
        changed = deepcopy(PLAN)
        changed['id'] = 'other'
        with self.assertRaises(ValueError):
            StoryDirector(changed, self.path, self.factory, threaded=False)

    def test_default_host_cannot_start_input_or_pause(self):
        service = StoryService(self.director)
        with self.assertRaises(ValueError):
            service.call('run', {})
        with self.assertRaises(ValueError):
            service.call('observe', {'pause_available': True})
        self.assertEqual(self.executors, [])

    def test_direct_input_and_worker_share_ownership(self):
        self.observe()
        self.director.begin_direct(intent='click', expected='menu')
        with self.assertRaises(RuntimeError):
            self.director.run(steps=[], intent='bad', expected='bad')
        self.now[0] += 11
        self.director.tick()
        self.assertEqual(self.director.report()['owner'], 'computer_use')
        self.director.end_direct(error='timeout')
        self.assertTrue(self.director.report()['stopped'])

    def test_duplicate_host_lock_refused_and_released(self):
        path = Path(self.temp.name)/'host.lock'
        with HostLock(path):
            with self.assertRaises(RuntimeError):
                with HostLock(path):
                    pass
        with HostLock(path):
            pass

    def test_bad_sequence_sends_no_input(self):
        self.observe()
        with self.assertRaises(ValueError):
            self.director.run(steps=[dict(keys=['w'], seconds=10)], intent='bad', expected='bad')
        self.assertEqual(self.executors, [])

    def test_capture_and_classification_time_are_separate(self):
        self.director.capture_started()
        self.now[0] += .4
        self.director.capture_finished()
        self.now[0] += 8
        self.observe()
        report = self.director.report()
        capture = next(e for e in report['events'] if e['kind']=='capture_finished')
        self.assertAlmostEqual(capture['data']['capture_seconds'], .4)
        self.assertAlmostEqual(report['gap_max_seconds'], 8)

    def test_unverified_result_cannot_be_overwritten(self):
        self.observe()
        self.director.run(steps=[dict(keys=['w'], seconds=.05)], intent='walk', expected='closer')
        self.join()
        with self.assertRaisesRegex(RuntimeError, 'previous action result'):
            self.director.run(steps=[dict(keys=['w'], seconds=.05)], intent='walk', expected='closer')

    def test_resume_does_not_turn_earlier_unknown_steps_into_next_steps(self):
        plan = deepcopy(PLAN)
        later = deepcopy(plan['steps'][0])
        later['id'] = 'b'
        plan['steps'].append(later)
        director = StoryDirector(plan, Path(self.temp.name)/'resume.jsonl', self.factory, threaded=False)
        self.addCleanup(director.close)
        director.observe(stage='b', mode='navigation', evidence='fixture', captured_wall=time.time())
        report = director.report()
        self.assertEqual(report['next_steps'][0]['id'], 'b')
        self.assertEqual(report['unverified_earlier_steps'], ['a'])
        self.assertEqual(report['progress']['completed'], 0)


if __name__ == '__main__':
    unittest.main()
