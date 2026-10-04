import copy
import unittest
import tempfile
import json
from pathlib import Path

from story_control.combat_recording import record, play, validate, record_takes, save_take
from story_control.windows_input import KEYS


class Clock:
    def __init__(self):
        self.now = 0

    def __call__(self):
        return self.now

    def sleep(self, seconds):
        self.now += seconds


class Device:
    def __init__(self, clock):
        self.clock = clock
        self.sent = []
        self.fail_at = None

    def check(self):
        if self.fail_at is not None and self.clock() >= self.fail_at:
            raise RuntimeError('Focus changed')

    def pressed(self, key):
        return False

    def send(self, events):
        for event in events:
            self.sent.append((self.clock(), event.payload.keyboard.scan,
                              event.payload.keyboard.flags))


def sample():
    return {'version': 1, 'duration': .1, 'events': [
        {'at': .01, 'key': 'q', 'down': True},
        {'at': .02, 'key': 'q', 'down': False},
        {'at': .03, 'key': 'q', 'down': True},
        {'at': .04, 'key': 'q', 'down': False}]}


class RecordingTests(unittest.TestCase):
    def test_f12_save_then_f8_records_a_new_file(self):
        clock = Clock()
        device = Device(clock)
        waits = []
        def wait(timeout):
            waits.append(timeout)
            if len(waits) > 2:
                raise KeyboardInterrupt()
            return device
        notifications = []
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder) / 'take.json'
            with self.assertRaises(KeyboardInterrupt):
                record_takes(base, 60, True, wait=wait,
                             capture=lambda d, s: dict(sample(), stop_reason='F12 stop requested'),
                             notify=notifications.append, sleep=clock.sleep)
            files = sorted(Path(folder).glob('*.json'))
            self.assertEqual(len(files), 2)
            for path in files:
                self.assertEqual(len(json.loads(path.read_text())['events']), 4)
            self.assertEqual(notifications, ['RECORDING', 'SAVED'] * 2)

    def test_existing_take_is_not_overwritten(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder) / 'take.json'
            save_take(sample(), base, 1)
            with self.assertRaises(FileExistsError):
                save_take(sample(), base, 1)

    def test_repeated_q_preserved_over_repeated_cycles(self):
        clock = Clock()
        device = Device(clock)
        result = play(sample(), device, repeats=2, clock=clock, sleep=clock.sleep)
        self.assertEqual(result['events'], 8)
        self.assertEqual([s[1] for s in device.sent], [KEYS['q']] * 8)
        self.assertEqual([s[2] for s in device.sent], [8, 10] * 4)
        self.assertAlmostEqual(device.sent[4][0], .11)

    def test_focus_loss_releases_held_key_and_skips_tail(self):
        clock = Clock()
        device = Device(clock)
        device.fail_at = .015
        with self.assertRaises(RuntimeError):
            play(sample(), device, clock=clock, sleep=clock.sleep)
        self.assertEqual([s[2] for s in device.sent], [8, 10])

    def test_invalid_tail_sends_nothing(self):
        data = sample()
        data['events'][-1]['key'] = 'f8'
        clock = Clock()
        device = Device(clock)
        with self.assertRaises(ValueError):
            play(data, device, clock=clock, sleep=clock.sleep)
        self.assertEqual(device.sent, [])

    def test_record_detects_repeated_taps_without_sending_input(self):
        clock = Clock()
        device = Device(clock)
        device.pressed = lambda key: key == 'q' and (
            .01 <= clock() < .02 or .03 <= clock() < .04)
        data = record(device, .06, clock, clock.sleep)
        self.assertEqual([e['down'] for e in data['events']], [True, False] * 2)
        self.assertEqual(device.sent, [])

    def test_record_focus_loss_closes_saved_hold_only(self):
        clock = Clock()
        device = Device(clock)
        device.pressed = lambda key: key == 'q' and clock() >= .01
        device.fail_at = .025
        data = record(device, .1, clock, clock.sleep)
        self.assertEqual(data['stop_reason'], 'Focus changed')
        self.assertFalse(data['events'][-1]['down'])
        self.assertEqual(device.sent, [])

    def test_rejects_unbalanced_and_nonfinite_records(self):
        for change in ('held', 'time', 'duration'):
            data = copy.deepcopy(sample())
            if change == 'held':
                data['events'].pop()
            elif change == 'time':
                data['events'][0]['at'] = float('nan')
            else:
                data['duration'] = float('inf')
            with self.assertRaises(ValueError):
                validate(data)


if __name__ == '__main__':
    unittest.main()
