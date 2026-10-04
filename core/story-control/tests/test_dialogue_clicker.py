import threading
import unittest
from unittest.mock import patch

from story_control.dialogue_clicker import DialogueClicker


class DialogueClickerTests(unittest.TestCase):
    def test_stop_prevents_further_clicks(self):
        clicked = threading.Event()
        worker = DialogueClicker(click=clicked.set)
        worker.start()
        self.assertTrue(clicked.wait(1))
        result = worker.stop()
        self.assertFalse(result['running'])
        self.assertEqual(result['reason'], 'stopped')
        self.assertEqual(result['clicks'], 1)

    def test_input_failure_stops_worker(self):
        def fail():
            raise RuntimeError('Focus changed')
        worker = DialogueClicker(click=fail)
        worker.start()
        worker._thread.join(1)
        self.assertFalse(worker.status()['running'])
        self.assertIn('Focus changed', worker.reason)

    def test_expired_lease_never_clicks(self):
        with patch('story_control.dialogue_clicker.time.monotonic', return_value=10):
            worker = DialogueClicker(click=lambda: self.fail('Unexpected click'))
            worker._deadline = 9
            worker._run()
        self.assertEqual(worker.reason, 'observation lease expired')

    def test_requires_explicit_restart(self):
        with self.assertRaises(RuntimeError):
            DialogueClicker().renew()

    def test_invalid_timings(self):
        for lease in [0, 31, float('nan')]:
            with self.assertRaises(ValueError):
                DialogueClicker(lease=lease)


if __name__ == '__main__':
    unittest.main()
