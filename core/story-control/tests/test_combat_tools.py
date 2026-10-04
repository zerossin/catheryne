import time
import unittest
from story_control.combat_tools import CombatTools
from test_combat_runtime import FakeActuator, observation


class ToolTests(unittest.TestCase):
    def test_tool_start_report_take_control_stop(self):
        frame = 0
        def source(cancel):
            nonlocal frame
            frame += 1
            return observation(frame, target_hp_ratio=max(0., .9-frame*.001))
        tools = CombatTools(source, FakeActuator)
        self.addCleanup(tools.stop)
        self.assertEqual(tools.report()['state'], 'not_started')
        result = tools.start_encounter(goal='defeat observed target', context='test', slots=[1])
        self.assertEqual(result['goal'], 'defeat observed target')
        deadline = time.monotonic()+1
        report = tools.report(result['cursor'])
        while 'observation' not in report['summary'] and time.monotonic()<deadline:
            time.sleep(.01)
            report = tools.report(result['cursor'])
        self.assertIn('observation', report['summary'])
        self.assertNotIn('observation', [e['kind'] for e in report['events']])
        tools.take_control()
        deadline = time.monotonic()+1
        while tools.report()['state']=='running' and time.monotonic()<deadline:
            time.sleep(.01)
        self.assertEqual(tools.report()['state'], 'needs_review')
        self.assertIsNot(tools.report()['world_paused'], True)

    def test_invalid_lease_does_not_start_worker(self):
        tools = CombatTools(lambda cancel: None, FakeActuator)
        for seconds in (float('nan'), 0, 121, True):
            with self.assertRaises(ValueError):
                tools.start_encounter(goal='test', context='test', slots=[1], seconds=seconds)
        self.assertIsNone(tools.worker)


if __name__ == '__main__':
    unittest.main()
