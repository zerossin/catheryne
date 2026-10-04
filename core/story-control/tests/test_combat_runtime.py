import threading
import time
import unittest

from story_control.combat_runtime import CombatRuntime, monotonic_ms
from story_control.combat_state import CombatPlan, CombatSnapshot, Signal
from story_control.game_control import Observation


class FakeActuator:
    def __init__(self, hold=.005):
        self.actions = []
        self.hold = hold
        self.cancelled = False

    def execute(self, action, cancel):
        self.actions.append(action)
        cancel.wait(self.hold)

    def cancel(self):
        self.cancelled = True


def observation(frame, **changes):
    now = monotonic_ms()
    values = dict(combat_ui=True, dead=False, active_slot=1, hp_ratio=.9,
                  e_ready=False, q_ready=False, world_paused=False,
                  target_visible=True, target_in_range=True,
                  target_defeated=False, target_hp_ratio=.8)
    values.update(changes)
    return Observation(frame, now, 'test',
        dict(scene='combat', dialogue_ui=False, branch_sensitive=None,
             perception_confidence=1., choices=[]),
        CombatSnapshot(tuple((k, Signal(v, now, 1, 'simulator')) for k, v in values.items())))


class RuntimeTests(unittest.TestCase):
    def start_worker(self, source=None, **timings):
        actuator = FakeActuator()
        worker = CombatRuntime(actuator, period_ms=5, **timings)
        plan = CombatPlan('test', monotonic_ms()+3000, (1,), require_target=True, allow_approach=True)
        worker.start(plan, source=source)
        self.addCleanup(lambda: (worker.stop(), worker.join()))
        return worker, actuator

    def wait_state(self, worker, expected, timeout=1):
        end = time.monotonic()+timeout
        while time.monotonic() < end:
            if worker.status()['state'] == expected:
                return
            time.sleep(.005)
        self.fail(worker.status())

    def test_capture_continues_without_supervisor_calls_and_confirms_completion(self):
        frame = 0
        def source(cancel):
            nonlocal frame
            frame += 1
            return observation(frame, target_defeated=frame > 3,
                               target_hp_ratio=max(0, .8-frame*.1))
        worker, actuator = self.start_worker(source)
        self.wait_state(worker, 'completed')
        self.assertTrue(actuator.actions)
        self.assertEqual(worker.status()['model_calls'], 0)
        self.assertGreater(frame, 10)

    def test_blocked_capture_cannot_block_watchdog(self):
        blocked = threading.Event()
        self.addCleanup(blocked.set)
        def source(cancel):
            blocked.wait(.8)
            return None
        worker, _ = self.start_worker(source, observation_ttl_ms=40)
        self.wait_state(worker, 'failed')
        self.assertIn('observation_missing', [x['kind'] for x in worker.status()['events']])

    def test_unknown_target_is_not_a_completion(self):
        worker, actuator = self.start_worker()
        worker.publish(observation(1, target_visible=False))
        self.wait_state(worker, 'needs_review')
        self.assertFalse(actuator.actions)

    def test_low_health_interrupts_pending_skill_confirmation(self):
        worker, actuator = self.start_worker()
        worker.publish(observation(1, e_ready=True))
        end = time.monotonic()+.5
        while not actuator.actions and time.monotonic() < end:
            time.sleep(.005)
        worker.publish(observation(2, hp_ratio=.1, e_ready=True))
        self.wait_state(worker, 'needs_review')
        self.assertEqual(actuator.actions, ['e'])
        self.assertTrue(actuator.cancelled)

    def test_low_health_requests_verified_pause_without_model(self):
        from dataclasses import replace
        worker, actuator = self.start_worker()
        worker.plan = replace(worker.plan, survival='request_verified_pause')
        worker.publish(observation(1, hp_ratio=.1))
        end = time.monotonic()+.5
        while not actuator.actions and time.monotonic() < end:
            time.sleep(.005)
        self.assertEqual(actuator.actions, ['request_verified_pause'])
        worker.publish(observation(2, hp_ratio=.1, world_paused=True))
        self.wait_state(worker, 'needs_review')
        self.assertTrue(worker.report()['world_paused'])
        self.assertEqual(worker.status()['model_calls'], 0)

    def test_skill_is_not_repeated_without_feedback(self):
        frame = 0
        def source(cancel):
            nonlocal frame
            frame += 1
            return observation(frame, e_ready=True)
        worker, actuator = self.start_worker(source, feedback_ms=60)
        self.wait_state(worker, 'needs_review')
        self.assertEqual(actuator.actions, ['e'])

    def test_death_overrides_defeat_evidence(self):
        worker, _ = self.start_worker()
        worker.publish(observation(1, dead=True, target_defeated=True))
        self.wait_state(worker, 'failed')

    def test_out_of_order_observation_dropped(self):
        worker, _ = self.start_worker()
        self.assertTrue(worker.publish(observation(2)))
        self.assertFalse(worker.publish(observation(1)))

    def test_stop_releases_input_and_rejects_restart(self):
        worker, actuator = self.start_worker()
        worker.publish(observation(1))
        worker.stop()
        status = worker.join()
        self.assertFalse(status['input_alive'])
        self.assertFalse(status['owns_input'])
        self.assertTrue(actuator.cancelled)
        self.assertFalse(worker.publish(observation(2)))

    def test_duplicate_input_owner_rejected(self):
        worker, _ = self.start_worker()
        other = CombatRuntime(FakeActuator())
        with self.assertRaises(RuntimeError):
            other.start(worker.plan)

    def test_no_damage_or_approach_progress_stops(self):
        frame = 0
        def source(cancel):
            nonlocal frame
            frame += 1
            return observation(frame)
        worker, _ = self.start_worker(source, stall_ms=60)
        self.wait_state(worker, 'needs_review')
        self.assertIn('no_verified_progress', [x['kind'] for x in worker.status()['events']])

    def test_approach_requires_visible_target_clear_route_and_out_of_range(self):
        worker, actuator = self.start_worker()
        worker.publish(observation(1, target_in_range=False, approach_clear=True, target_distance=10))
        end = time.monotonic()+.5
        while not actuator.actions and time.monotonic() < end:
            time.sleep(.005)
        self.assertEqual(actuator.actions, ['approach'])
        worker.publish(observation(2, target_in_range=False, approach_clear=False))
        self.wait_state(worker, 'needs_review')

    def test_report_cursor_preserves_actions_and_current_evidence(self):
        worker, _ = self.start_worker()
        worker.publish(observation(1))
        report = worker.report()
        self.assertEqual(report['signals']['hp_ratio']['value'], .9)
        self.assertFalse(report['history_lost'])
        worker.stop()
        newer = worker.report(report['cursor'])
        self.assertTrue(all(e['sequence'] > report['cursor'] for e in newer['events']))

    def test_takeover_waits_for_pause_evidence(self):
        from dataclasses import replace
        worker, actuator = self.start_worker()
        worker.plan = replace(worker.plan, survival='request_verified_pause')
        worker.publish(observation(1))
        worker.request_control()
        end = time.monotonic()+.5
        while 'request_verified_pause' not in actuator.actions and time.monotonic() < end:
            time.sleep(.005)
        self.assertIn('request_verified_pause', actuator.actions)
        self.assertEqual(worker.status()['state'], 'running')
        worker.publish(observation(2, world_paused=True))
        self.wait_state(worker, 'needs_review')
        self.assertIn('world_pause_confirmed', [e['kind'] for e in worker.report()['events']])

    def test_pause_command_success_is_not_pause_success(self):
        from dataclasses import replace
        worker, actuator = self.start_worker(feedback_ms=50)
        worker.plan = replace(worker.plan, survival='request_verified_pause')
        worker.publish(observation(1))
        worker.request_control()
        self.wait_state(worker, 'needs_review')
        self.assertEqual(actuator.actions, ['request_verified_pause'])
        self.assertIn('world_pause_unconfirmed', [e['kind'] for e in worker.report()['events']])

    def test_unconfirmed_skill_requests_pause_when_authorized(self):
        from dataclasses import replace
        frame = 0
        def source(cancel):
            nonlocal frame
            frame += 1
            return observation(frame, e_ready=True)
        worker, actuator = self.start_worker(source, feedback_ms=50)
        worker.plan = replace(worker.plan, survival='request_verified_pause')
        self.wait_state(worker, 'needs_review')
        self.assertEqual(actuator.actions, ['e', 'request_verified_pause'])
        self.assertIn('world_pause_unconfirmed', [e['kind'] for e in worker.report()['events']])


if __name__ == '__main__':
    unittest.main()
