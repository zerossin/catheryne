"""Persistent local encounter worker. No model calls or implicit game connection.

A source supplies timestamped Observation objects from one monotonic clock.
An actuator must implement execute(action, cancel_event) and cancel().
Capture, control and input run independently; only the latest frame is retained.
"""
from collections import deque
from copy import deepcopy
import threading
import time

from .game_control import ControlState, Observation
from .combat_state import CombatPlan


def monotonic_ms():
    # Windows' coarse monotonic clock can assign identical timestamps to
    # distinct frames. QPC-backed perf_counter is monotonic and high resolution.
    return time.perf_counter_ns() / 1_000_000


class CombatRuntime:
    # In-process ownership, including workers created by different tool calls.
    # Separate processes / legacy scripts are NOT covered by this lease.
    _owner = threading.Lock()

    def __init__(self, actuator, *, clock=monotonic_ms, period_ms=20,
                 observation_ttl_ms=400, feedback_ms=500, stall_ms=2000):
        for value in (period_ms, observation_ttl_ms, feedback_ms, stall_ms):
            if not isinstance(value, (int, float)) or not 0 < value < float('inf'):
                raise ValueError('Positive finite timing required')
        self.actuator, self.clock = actuator, clock
        self.period = period_ms / 1000
        self.feedback_ms, self.stall_ms = feedback_ms, stall_ms
        self.control = ControlState(observation_ttl_ms)
        self._lock = threading.RLock()
        self._cancel = threading.Event()
        self._latest = None
        self._thread = self._source_thread = self._input_thread = None
        self._owns = False
        self._state = 'new'
        self._events = deque(maxlen=10000)
        self._count = 0
        self._pending = None
        self._pending_hp = None
        self._heal_after = 0
        self._next_action = 0
        self._defeat_since = None
        self._defeat_count = 0
        self._target_hp = None
        self._target_distance = None
        self._handoff = None
        self._last_signals = {}

    def _event(self, kind, **data):
        self._count += 1
        self._events.append(dict(sequence=self._count, at_ms=self.clock(), kind=kind, **data))

    def start(self, plan, *, source=None):
        """Returns immediately. source(cancel_event) returns a fresh Observation.

        None means no new frame. Source must implement bounded/cancellable reads.
        No automatic resume, TTL renewal, or model invocation.
        """
        with self._lock:
            if self._state != 'new':
                raise RuntimeError('Create a new runtime for each authorized encounter')
            if not isinstance(plan, CombatPlan) or plan.expires_ms <= self.clock():
                raise ValueError('Fresh CombatPlan required')
            if not self._owner.acquire(blocking=False):
                raise RuntimeError('Another encounter owns this runtime input path')
            self._owns = True
            self.plan = plan
            self._started = self._last_progress = self.clock()
            self._state = 'running'
            self._event('started', context=plan.context)
            self._thread = threading.Thread(target=self._run, daemon=True)
            self._thread.start()
            if source is not None:
                self._source_thread = threading.Thread(target=self._capture, args=(source,), daemon=True)
                self._source_thread.start()
        return self.status()

    def publish(self, observation):
        if not isinstance(observation, Observation):
            raise ValueError('Expected Observation')
        now = self.clock()
        if observation.captured_ms > now:
            raise ValueError('Future frame')
        with self._lock:
            if self._state != 'running':
                return False
            if self._latest and (observation.frame <= self._latest.frame or
                                 observation.captured_ms <= self._latest.captured_ms):
                self._event('frame_discarded', reason='out_of_order')
                return False
            self._latest = deepcopy(observation)
            return True

    def _capture(self, source):
        try:
            while not self._cancel.is_set():
                obs = source(self._cancel)
                if obs is not None:
                    self.publish(obs)
                self._cancel.wait(self.period)
        except Exception as error:
            with self._lock:
                self._finish('failed', 'capture_error', error=str(error))

    def _finish(self, state, reason, **data):
        if self._state != 'running':
            return
        self._state = state
        self._cancel.set()
        self._event(reason, **data)
        # Contract: cancel is nonblocking, and execute releases keys in finally.
        try:
            self.actuator.cancel()
        except Exception as error:
            self._event('cancel_error', error=str(error))

    def stop(self):
        with self._lock:
            self._finish('stopped', 'user_stop')
        return self.status()

    def join(self, timeout=1):
        if self._thread:
            self._thread.join(timeout)
        return self.status()

    def request_control(self):
        """Request observed world pause before handoff, not just input stop."""
        with self._lock:
            if self._state == 'running' and self._handoff is None:
                self._handoff = dict(reason='supervisor_requested_control', sent_ms=None)
                self._event('handoff_requested')
        return self.status()

    def report(self, after_sequence=0):
        """Incremental supervisor response, with explicit bounded-buffer loss."""
        if type(after_sequence) is not int or after_sequence < 0:
            raise ValueError('Nonnegative event cursor required')
        with self._lock:
            obs = self._latest
            signals = {} if obs is None or obs.combat is None else {
                name: dict(value=signal.value, age_ms=self.clock()-signal.captured_ms,
                           quality=signal.quality, source=signal.source)
                for name, signal in obs.combat.signals}
            return dict(state=self._state, cursor=self._count,
                input_alive=bool(self._input_thread and self._input_thread.is_alive()),
                control_alive=bool(self._thread and self._thread.is_alive()),
                owns_input=self._owns,
                history_lost=bool(self._events and after_sequence < self._events[0]['sequence']-1),
                context=obs.context if obs else None, frame=obs.frame if obs else None,
                signals=signals,
                events=[deepcopy(e) for e in self._events if e['sequence'] > after_sequence],
                world_paused=obs.combat.read('world_paused', self.clock()) if obs and obs.combat else None)

    def status(self):
        with self._lock:
            return dict(state=self._state, model_calls=0,
                        input_alive=bool(self._input_thread and self._input_thread.is_alive()),
                        source_alive=bool(self._source_thread and self._source_thread.is_alive()),
                        control_alive=bool(self._thread and self._thread.is_alive()),
                        owns_input=self._owns, event_count=self._count,
                        events=deepcopy(list(self._events)))

    def _execute(self, action, frame, captured_ms, submitted):
        try:
            if self._cancel.is_set():
                return
            started = self.clock()
            with self._lock:
                self._event('input_started', action=action, frame=frame,
                            observation_age_ms=started-captured_ms,
                            dispatch_ms=started-submitted)
            self.actuator.execute(action, self._cancel)
            with self._lock:
                self._event('input_returned', action=action, duration_ms=self.clock()-started)
        except Exception as error:
            with self._lock:
                self._finish('failed', 'input_error', error=str(error))

    def _step(self):
        now = self.clock()
        if now >= self.plan.expires_ms:
            if self.plan.survival == 'request_verified_pause' and now < self.plan.expires_ms+self.feedback_ms+200:
                if self._handoff is None:
                    self._handoff = dict(reason='plan_expired', sent_ms=None)
            else:
                self._finish('expired', 'plan_expired', world_paused=None)
                return
        obs = self._latest
        if obs is None:
            if now-self._started > self.control.ttl:
                self._finish('failed', 'observation_missing')
            return
        if obs.context != self.plan.context:
            self._finish('failed', 'context_changed')
            return
        if now-obs.captured_ms > self.control.ttl:
            self._finish('failed', 'observation_expired')
            return
        fresh = self.control.latest is None or obs.frame > self.control.latest.frame
        if fresh:
            self.control.observe(obs, now)
            if self.control.stopped:
                self.control.resume(now)
                self.control.set_combat_plan(self.plan)
            self._event('observation', frame=obs.frame, age_ms=now-obs.captured_ms)
        if obs.combat is None:
            self._finish('failed', 'combat_evidence_missing')
            return
        read = lambda name: obs.combat.read(name, now)
        if fresh:
            values = {name: read(name) for name, _ in obs.combat.signals}
            changed = {name: value for name, value in values.items()
                       if name not in self._last_signals or self._last_signals[name] != value}
            if changed:
                self._event('state_changed', frame=obs.frame, changes=changed)
            self._last_signals = values
        # Death is never a successful completion, even if a defeat template matches.
        if read('dead') is True:
            self._finish('failed', 'death_requires_recovery')
            return
        if self._handoff is not None:
            self._pause_for_handoff(obs, now)
            return
        if fresh:
            if read('target_defeated') is True and read('dead') is False:
                if self._defeat_since is None:
                    self._defeat_since = obs.captured_ms
                self._defeat_count += 1
                if self._defeat_count >= 3 and obs.captured_ms-self._defeat_since >= 200:
                    self._finish('completed', 'target_defeat_confirmed', elapsed_ms=now-self._started)
                    return
            else:
                self._defeat_since, self._defeat_count = None, 0
            hp = read('target_hp_ratio')
            if hp is not None:
                if self._target_hp is not None and hp < self._target_hp:
                    self._last_progress = now
                    self._event('damage_observed', target_hp_ratio=hp)
                self._target_hp = hp
            distance = read('target_distance')
            if distance is not None:
                if self._target_distance is not None and distance < self._target_distance:
                    self._last_progress = now
                    self._event('approach_progress', distance=distance)
                self._target_distance = distance
        decision = self.control.recommend(now)
        if decision['route'] in ('astra', 'cancel'):
            self._handoff = dict(reason=decision['reason'], sent_ms=None)
            self._pause_for_handoff(obs, now)
            return
        action = decision.get('action')
        if action in (None, 'wait'):
            return
        if action == 'request_verified_pause':
            self._handoff = dict(reason=decision['reason'], sent_ms=None)
            self._pause_for_handoff(obs, now)
            return
        # Do not let an unimplemented semantic recovery become a guessed key.
        if action not in ('e', 'q', 'attack', 'approach', 'use_verified_heal'):
            self._finish('needs_review', 'unmapped_action', action=action)
            return
        if action == 'use_verified_heal' and self._pending and self._pending[0] != action:
            self._event('action_preempted', action=self._pending[0], reason='low_health')
            self._pending = None
        if self._pending:
            old, at, source_frame = self._pending
            confirmed = obs.frame > source_frame and (
                (old == 'e' and (read('e_ready') is False or (read('e_seconds') or 0) > 0)) or
                (old == 'q' and read('q_ready') is False) or
                (old == 'use_verified_heal' and read('hp_ratio') is not None and read('hp_ratio') > self._pending_hp))
            if confirmed:
                self._event('action_confirmed', action=old, latency_ms=now-at)
                self._pending = None
            elif now-at >= (1500 if old == 'use_verified_heal' else self.feedback_ms):
                self._event('action_unconfirmed', action=old)
                self._handoff = dict(reason='action_unconfirmed', sent_ms=None)
                self._pause_for_handoff(obs, now)
                return
            else:
                return
        if self.plan.require_target and now-self._last_progress >= self.stall_ms:
            self._handoff = dict(reason='no_verified_progress', sent_ms=None)
            self._pause_for_handoff(obs, now)
            return
        if self._input_thread and self._input_thread.is_alive():
            return
        if now < self._next_action:
            return
        self._next_action = now+200
        if action == 'use_verified_heal':
            if now < self._heal_after:
                return
            self._heal_after = now+1500
            self._pending_hp = read('hp_ratio')
        if action in ('e', 'q', 'use_verified_heal'):
            self._pending = (action, now, obs.frame)
        self._event('action_submitted', action=action, frame=obs.frame)
        self._input_thread = threading.Thread(target=self._execute,
            args=(action, obs.frame, obs.captured_ms, now), daemon=True)
        self._input_thread.start()

    def _pause_for_handoff(self, obs, now):
        if obs.combat.read('world_paused', now) is True:
            self._finish('needs_review', 'world_pause_confirmed',
                         trigger=self._handoff['reason'], world_paused=True)
            return
        if self.plan.survival != 'request_verified_pause':
            self._finish('needs_review', self._handoff['reason'], world_paused=None)
            return
        sent = self._handoff['sent_ms']
        if sent is not None:
            if now-sent >= self.feedback_ms:
                self._finish('needs_review', 'world_pause_unconfirmed', world_paused=None)
            return
        if self._input_thread and self._input_thread.is_alive():
            return
        self._handoff['sent_ms'] = now
        self._event('action_submitted', action='request_verified_pause', frame=obs.frame)
        self._input_thread = threading.Thread(target=self._execute,
            args=('request_verified_pause', obs.frame, obs.captured_ms, now), daemon=True)
        self._input_thread.start()

    def _run(self):
        try:
            while not self._cancel.is_set():
                with self._lock:
                    self._step()
                self._cancel.wait(self.period)
        except Exception as error:
            with self._lock:
                self._finish('failed', 'control_error', error=str(error))
        finally:
            # Keep ownership until the actuator has actually released its input.
            if self._input_thread:
                self._input_thread.join()
            with self._lock:
                if self._owns:
                    self._owner.release()
                    self._owns = False
                self._event('input_released')


class SessionActuator:
    """Explicit adapter to existing GameSession; construction sends no input.

    Legacy dialogue/recording tasks must already be stopped. This adapter does
    not claim a cross-process lock or provide camera steering / target finding.
    """
    def __init__(self, session, *, verified_pause_key=None):
        if verified_pause_key not in (None, 'b'):
            raise ValueError('Only an explicitly verified B pause mapping is supported')
        self.session = session
        self.verified_pause_key = verified_pause_key

    def execute(self, action, cancel):
        if cancel.is_set():
            return
        keys = {'attack': ['attack'], 'e': ['e'], 'q': ['q'], 'approach': ['w']}
        if action == 'request_verified_pause':
            if self.verified_pause_key is None:
                raise RuntimeError('Pause mapping is not verified for this encounter')
            keys[action] = [self.verified_pause_key]
        self.session.act(keys[action], seconds=.08)

    def cancel(self):
        self.session.stop()
