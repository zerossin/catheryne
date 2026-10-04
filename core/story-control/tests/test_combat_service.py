import base64
import io
import json
import os
from pathlib import Path
import threading
import time
import unittest
from unittest.mock import patch
from urllib.request import Request, urlopen
from urllib.error import HTTPError

from PIL import Image
from story_control.combat_hud import HudReader
from story_control.combat_service import Service, make_server
from test_combat_runtime import FakeActuator


class ServiceTests(unittest.TestCase):
    def setUp(self):
        if not os.environ.get('CATHERYNE_PRIVATE_FIXTURES'): self.skipTest('Private game frames not distributed')
        self.root = Path(os.environ['CATHERYNE_PRIVATE_FIXTURES'])
        self.service = Service(HudReader.load(self.root / 'observed/hp-profile.json'),
                               actuator_factory=FakeActuator)
        self.addCleanup(self.service.tools.stop)

    def frame(self, phase='field', frame=1):
        return dict(frame=frame, capture_started_wall_ms=time.time()*1000,
                    context='test', png=base64.b64encode((self.root / f'live-check/{phase}-hp.png').read_bytes()).decode())

    def test_frame_ingestion_is_observation_only(self):
        self.assertTrue(self.service.call('frame', self.frame())['accepted'])
        status = self.service.call('status', {})
        self.assertEqual(status['observation']['signals']['hp_ratio']['value'], 1.)
        self.assertEqual(status['execution']['state'], 'not_started')
        self.assertIn('target_visible', status['missing_detectors'])

    def test_hp_only_profile_cannot_start_combat(self):
        self.service.call('frame', self.frame())
        with self.assertRaisesRegex(ValueError, 'calibrated detectors'):
            self.service.call('start', dict(context='test', goal='test', slots=[1]))

    def test_delayed_frame_keeps_capture_age(self):
        frame = self.frame()
        frame['capture_started_wall_ms'] -= 1200
        self.service.call('frame', frame)
        self.assertGreaterEqual(self.service.call('status', {})['observation']['age_ms'], 1200)

    def test_reordered_frame_rejected(self):
        self.service.call('frame', self.frame(frame=2))
        self.assertFalse(self.service.call('frame', self.frame(frame=1))['accepted'])

    def test_jpeg_transport_supported(self):
        with Image.open(self.root / 'live-check/bag-hp.png') as image:
            encoded = io.BytesIO()
            image.convert('RGB').save(encoded, format='JPEG')
        frame = self.frame('bag')
        frame['png'] = base64.b64encode(encoded.getvalue()).decode()
        self.assertTrue(self.service.call('frame', frame)['accepted'])
        self.assertIsNone(self.service.call('status', {})['observation']['signals']['hp_ratio']['value'])

    def test_loopback_requires_session_token(self):
        server = make_server(self.service, 'test-token')
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        self.addCleanup(lambda: (server.shutdown(), server.server_close(), thread.join()))
        url = f'http://127.0.0.1:{server.server_port}/rpc'
        payload = json.dumps(dict(method='status')).encode()
        with self.assertRaises(HTTPError) as error:
            urlopen(Request(url, data=payload), timeout=1)
        self.assertEqual(error.exception.code, 403)
        with urlopen(Request(url, data=payload, headers={'Authorization': 'Bearer test-token'}), timeout=1) as response:
            self.assertTrue(json.load(response)['ok'])

    def test_local_observer_is_independent_and_releases_on_expiry(self):
        from story_control.combat_runtime import monotonic_ms
        root = self.root
        class Capture:
            frames = 0
            last_latency_ms = 1
            closed = False
            def __init__(self, context, *, target_exe=None):
                self.context = context
            def __call__(self, cancel):
                self.frames += 1
                with Image.open(root / 'live-check/field-hp.png') as image:
                    return image.convert('RGB'), monotonic_ms(), self.context
            def close(self):
                Capture.closed = True
        with patch('story_control.game_capture.WindowCapture', Capture), patch.object(self.service.tools, 'stop') as stop:
            result = self.service.call('observe', dict(context='test', seconds=1, hz=20))
            self.assertFalse(result['input_started'])
            with self.assertRaisesRegex(RuntimeError, 'owns the frame source'):
                self.service.call('frame', self.frame())
            self.service._capture_thread.join(2)
            status = self.service.call('status', {})['local_capture']
            self.assertFalse(status['running'])
            self.assertGreaterEqual(status['frames'], 10)
            self.assertGreaterEqual(status['capture_to_state_max_ms'], status['capture_to_state_p95_ms'])
            self.assertTrue(Capture.closed)
            stop.assert_called_once()

    def test_calibrated_bag_frames_drive_assist_without_input(self):
        actuator = FakeActuator()
        service = Service(HudReader.load(self.root / 'calibration/profile.json'),
                          actuator_factory=lambda: actuator)
        self.addCleanup(service.tools.stop)
        encoded = base64.b64encode((self.root / 'calibration/bag-hud.png').read_bytes()).decode()
        cancel = threading.Event()
        def feed():
            frame = 0
            while not cancel.is_set():
                frame += 1
                service.call('frame', dict(frame=frame, capture_started_wall_ms=time.time()*1000,
                                          context='test', png=encoded))
                cancel.wait(.05)
        thread = threading.Thread(target=feed)
        thread.start()
        try:
            deadline = time.monotonic()+2
            while service.bridge.latest is None and time.monotonic() < deadline:
                time.sleep(.01)
            service.call('start', dict(goal='bag preflight', context='test', slots=[2], seconds=1,
                                      mode='assist', verified_pause=True))
            deadline = time.monotonic()+2
            while service.tools.worker.status()['control_alive'] and time.monotonic() < deadline:
                time.sleep(.01)
            report = service.call('report', {})
            self.assertEqual(report['state'], 'needs_review')
            self.assertFalse(report['owns_input'])
            self.assertFalse(report['target_tracking'])
            self.assertEqual(actuator.actions, [])
            self.assertIn('world_pause_confirmed', [e['kind'] for e in report['events']])
        finally:
            cancel.set()
            thread.join(2)


    def test_paused_preflight_rejects_field_before_actuator_creation(self):
        factory = unittest.mock.Mock()
        service = Service(HudReader.load(self.root / 'calibration/profile.json'), actuator_factory=factory)
        encoded = base64.b64encode((self.root / 'calibration/field-hud.png').read_bytes()).decode()
        service.call('frame', dict(frame=1, capture_started_wall_ms=time.time()*1000,
                                  context='test', png=encoded))
        with self.assertRaisesRegex(ValueError, 'confirmed pause evidence'):
            service.call('start', dict(goal='preflight', context='test', slots=[2], seconds=1,
                                      mode='assist', require_paused=True))
        factory.assert_not_called()


if __name__ == '__main__':
    unittest.main()
