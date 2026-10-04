"""High-level local tool facade; requires explicit source/actuator wiring.

This module neither registers an MCP tool nor starts native capture on import.
"""
from collections import Counter
from .combat_runtime import CombatRuntime, monotonic_ms
from .combat_state import CombatPlan


def hud_observation(frame, captured_ms, context, snapshot):
    from .game_control import Observation
    now = monotonic_ms()
    scene = ('combat' if snapshot.read('combat_ui', now) is True else
             'field' if snapshot.read('controls_visible', now) is True else 'unknown')
    return Observation(frame, captured_ms, context,
        dict(scene=scene, dialogue_ui=None, branch_sensitive=None,
             perception_confidence=0. if scene == 'unknown' else 1., choices=[]), snapshot)


class CombatTools:
    def __init__(self, source, actuator_factory):
        if not callable(source) or not callable(actuator_factory):
            raise ValueError('A continuous observation source and actuator factory are required')
        self.source, self.actuator_factory = source, actuator_factory
        self.worker = None
        self.goal = None

    def start_encounter(self, *, goal, context, slots, seconds=45,
                        allow_approach=False, verified_pause=False, mode='tracked',
                        use_food=False, preflight=False):
        """Bounded autonomous encounter. The goal text is a label, not a parser.

        Target, route and defeat evidence come from the configured source.
        No guessed geometry, party selection, or unbounded automatic renewal.
        """
        if not isinstance(goal, str) or not goal.strip():
            raise ValueError('An encounter goal is required')
        if type(seconds) not in (int, float) or not 1 <= seconds <= 120:
            raise ValueError('Encounter lease must be 1..120 seconds')
        if mode not in ('tracked', 'assist'):
            raise ValueError('Expected tracked or assist mode')
        if mode == 'assist' and (allow_approach or seconds > 10):
            raise ValueError('Untracked assistance is stationary and limited to 10 seconds')
        if self.worker and (self.worker.status()['control_alive'] or self.worker.status()['owns_input']):
            raise RuntimeError('Existing encounter must release control first')
        if type(use_food) is not bool:
            raise ValueError('use_food must be boolean')
        plan = CombatPlan(context, monotonic_ms()+seconds*1000, tuple(slots),
            require_target=mode == 'tracked', allow_approach=allow_approach,
            survival='use_verified_heal' if use_food else 'request_verified_pause' if verified_pause else None)
        source = self.source
        if preflight:
            import threading
            from .combat_state import decide_combat
            first = source(threading.Event())
            if first is None or not 0 <= monotonic_ms()-first.captured_ms <= 400:
                raise ValueError('Fresh local HUD observation is required')
            decision = decide_combat(first.combat, plan, first.context, monotonic_ms())
            if decision['route'] != 'rule':
                raise ValueError('Local assistance not ready: '+decision['reason'])
            def checked_source(cancel):
                nonlocal first
                if first is not None:
                    result, first = first, None
                    return result
                return source(cancel)
        else:
            checked_source = source
        self.worker = CombatRuntime(self.actuator_factory())
        self.goal = goal
        self.worker.start(plan, source=checked_source)
        return self.report()

    def report(self, after_sequence=0):
        if self.worker is None:
            return dict(state='not_started', goal=None, cursor=0, events=[])
        result = self.worker.report(after_sequence)
        result['goal'] = self.goal
        result['mode'] = 'tracked' if self.worker.plan.require_target else 'assist'
        result['target_tracking'] = self.worker.plan.require_target
        result['summary'] = dict(Counter(event['kind'] for event in result['events']))
        # Avoid sending repetitive frame logs to the main model. The worker
        # retains them for measurement, exposed explicitly via status().
        result['events'] = [e for e in result['events'] if e['kind'] != 'observation']
        return result

    def take_control(self):
        if self.worker:
            self.worker.request_control()
        return self.report()

    def stop(self):
        if self.worker:
            self.worker.stop()
            self.worker.join(timeout=.2)
        return self.report()


class HudObservationSource:
    """Bridge continuous RGB capture to the existing canonical HUD reader.

    capture(cancel_event) returns (PIL image, captured_ms, context), or None.
    Use capture-start time conservatively when exact exposure time is unknown.
    Never stamp a delayed image with its arrival time to make it appear fresh.
    This adapter supplies no capture implementation or pretrained detectors.
    """
    def __init__(self, capture, reader):
        self.capture, self.reader = capture, reader
        self.frame = 0

    def __call__(self, cancel):
        result = self.capture(cancel)
        if result is None:
            return None
        image, captured_ms, context = result
        snapshot, _ = self.reader.read(image, captured_ms)
        self.frame += 1
        return hud_observation(self.frame, captured_ms, context, snapshot)
