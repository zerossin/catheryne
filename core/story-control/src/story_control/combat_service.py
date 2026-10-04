"""Loopback bridge for Computer Use frames and persistent combat tools.

Only explicit `start` enables input. Starting the host is observation-only.
Connection credentials belong in an OS temporary directory, never the vault.
"""
import argparse
from collections import deque
import base64
import io
import json
from pathlib import Path
import secrets
import threading
import time
from .rpc import make_server

from PIL import Image
from story_control.combat_hud import HudReader
from story_control.combat_runtime import monotonic_ms, SessionActuator
from story_control.combat_tools import CombatTools, hud_observation


class FrameBridge:
    def __init__(self, reader, *, crop=None):
        self.reader, self.crop = reader, crop
        self.lock = threading.Lock()
        self.latest = None
        self.last_frame = -1
        self.metrics = None

    def receive(self, data):
        frame = data['frame']
        started_wall = data['capture_started_wall_ms']
        if type(frame) is not int or frame < 0:
            raise ValueError('Invalid frame sequence')
        if type(started_wall) not in (int, float):
            raise ValueError('Capture-start wall time required')
        age = time.time()*1000-started_wall
        if not 0 <= age <= 10000:
            raise ValueError('Frame clock is invalid or image is too old')
        captured_ms = monotonic_ms()-age
        payload = base64.b64decode(data['png'], validate=True)
        if len(payload) > 24*1024*1024:
            raise ValueError('Image too large')
        with Image.open(io.BytesIO(payload)) as image:
            if image.width*image.height > 16000000:
                raise ValueError('Image dimensions too large')
            image = image.convert('RGB')
            if self.crop:
                image = image.crop(self.crop)
            snapshot, processing = self.reader.read(image, captured_ms)
        now = monotonic_ms()
        obs = hud_observation(frame, captured_ms, data['context'], snapshot)
        with self.lock:
            if self.latest and (frame <= self.latest.frame or captured_ms <= self.latest.captured_ms):
                return dict(accepted=False, reason='out_of_order')
            self.latest = obs
            self.metrics = dict(frame=frame, processing_ms=processing, capture_to_state_ms=now-captured_ms)
        return dict(accepted=True, **self.metrics)

    def source(self, cancel):
        with self.lock:
            if self.latest is None or self.latest.frame <= self.last_frame:
                return None
            self.last_frame = self.latest.frame
            return self.latest

    def status(self):
        with self.lock:
            if self.latest is None:
                return dict(frame=None, signals={}, metrics=None)
            return dict(frame=self.latest.frame, context=self.latest.context,
                age_ms=monotonic_ms()-self.latest.captured_ms, metrics=self.metrics,
                signals={name: dict(value=s.value, quality=s.quality, source=s.source)
                         for name, s in self.latest.combat.signals})


class Service:
    REQUIRED = {'combat_ui', 'dead', 'active_slot', 'hp_ratio', 'world_paused',
                'target_visible', 'target_in_range', 'target_hp_ratio', 'target_defeated'}

    def __init__(self, reader, *, crop=None, actuator_factory=None):
        self.bridge = FrameBridge(reader, crop=crop)
        self.fields = {d['signal'] for d in reader.detectors}
        if reader.profile.get('derive_alive_from_hp'):
            self.fields.add('dead')
        self.tools = CombatTools(self.bridge.source, actuator_factory or self.native_actuator)
        self._capture_stop = threading.Event()
        self._capture_thread = None
        self._capture_error = None
        self._capture_stats = {}
        self._commands = threading.RLock()

    @staticmethod
    def native_actuator():
        from story_control.game_session import GameSession
        # No input is sent by constructing this adapter. Pause is separately
        # authorized by the encounter plan; actual pause still requires evidence.
        import os
        return SessionActuator(GameSession(target_exe=os.environ.get('CATHERYNE_GAME_EXE')), verified_pause_key='b')

    def call(self, method, params, *, view="full"):
        if view != "full":
            raise ValueError("Combat diagnostics support full view only")
        # Serialize lifecycle changes across concurrent HTTP clients.
        with self._commands:
            return self._call(method, params)

    def _call(self, method, params):
        if method == 'frame':
            if self._capture_thread and self._capture_thread.is_alive():
                raise RuntimeError('Local observer owns the frame source')
            return self.bridge.receive(params)
        if method == 'status':
            return dict(observation=self.bridge.status(), execution=self.tools.report(params.get('after_sequence', 0)),
                        missing_detectors=sorted(self.REQUIRED-self.fields),
                        local_capture=dict(running=bool(self._capture_thread and self._capture_thread.is_alive()),
                                           error=self._capture_error, **self._capture_stats))
        if method == 'observe':
            return self.start_observer(**params)
        if method == 'stop_observer':
            self._capture_stop.set()
            self.tools.stop()
            if self._capture_thread:
                self._capture_thread.join(.5)
            return dict(stopped=not self._capture_thread or not self._capture_thread.is_alive())
        if method == 'start':
            params = dict(params)
            require_paused = params.pop('require_paused', False)
            if type(require_paused) is not bool:
                raise ValueError('require_paused must be boolean')
            required = self.REQUIRED if params.get('mode', 'tracked') == 'tracked' else {'controls_visible', 'dead', 'active_slot', 'hp_ratio', 'world_paused'}
            missing = required-self.fields
            if missing:
                raise ValueError('Live encounter needs calibrated detectors: '+', '.join(sorted(missing)))
            latest = self.bridge.status()
            if latest['frame'] is None or latest['age_ms'] > 400:
                raise ValueError('Fresh continuous capture required before input')
            if params['context'] != latest['context']:
                raise ValueError('Capture/plan context mismatch')
            pause = latest['signals'].get('world_paused', {})
            if require_paused and (pause.get('value') is not True or pause.get('quality', 0) < .9):
                raise ValueError('Paused preflight requires fresh confirmed pause evidence')
            return self.tools.start_encounter(**params)
        if method == 'report':
            return self.tools.report(**params)
        if method == 'take_control':
            return self.tools.take_control()
        if method == 'stop':
            return self.tools.stop()
        raise ValueError('Unknown method')

    def start_observer(self, context, seconds=60, hz=10):
        from story_control.game_capture import WindowCapture
        if not context or type(seconds) not in (int, float) or not 1 <= seconds <= 120:
            raise ValueError('Observer lease must be 1..120 seconds')
        if type(hz) not in (int, float) or not 1 <= hz <= 20:
            raise ValueError('Observer rate must be 1..20Hz')
        if self._capture_thread and self._capture_thread.is_alive():
            raise RuntimeError('Observer already running')
        self._capture_stop = threading.Event()
        self._capture_error = None
        self._capture_stats = dict(frames=0)
        def observe():
            import os
            capture = WindowCapture(context, target_exe=os.environ.get("CATHERYNE_GAME_EXE"))
            started = monotonic_ms()
            deadline = started+seconds*1000
            latencies = deque(maxlen=2400)
            try:
                while not self._capture_stop.is_set() and monotonic_ms() < deadline:
                    loop_start = monotonic_ms()
                    result = capture(self._capture_stop)
                    if result is None:
                        break
                    image, captured_ms, frame_context = result
                    if self.bridge.crop:
                        image = image.crop(self.bridge.crop)
                    snapshot, processing = self.bridge.reader.read(image, captured_ms)
                    with self.bridge.lock:
                        frame = 1 if self.bridge.latest is None else self.bridge.latest.frame+1
                        self.bridge.latest = hud_observation(frame, captured_ms, frame_context, snapshot)
                        self.bridge.metrics = dict(frame=frame, processing_ms=processing,
                                                  capture_to_state_ms=monotonic_ms()-captured_ms)
                    latency = monotonic_ms()-captured_ms
                    latencies.append(latency)
                    ordered = sorted(latencies)
                    self._capture_stats = dict(frames=capture.frames, capture_ms=capture.last_latency_ms,
                        processing_ms=processing, capture_to_state_ms=latency,
                        capture_to_state_p50_ms=ordered[(len(ordered)-1)//2],
                        capture_to_state_p95_ms=ordered[int((len(ordered)-1)*.95)],
                        capture_to_state_max_ms=max(ordered),
                        effective_hz=capture.frames*1000/max(1, monotonic_ms()-started))
                    self._capture_stop.wait(max(0., 1/hz-(monotonic_ms()-loop_start)/1000))
            except Exception as error:
                self._capture_error = str(error)
                self.tools.stop()
            finally:
                capture.close()
                self._capture_stop.set()
                # An expired capture lease cannot leave a worker using its last frame.
                self.tools.stop()
        self._capture_thread = threading.Thread(target=observe, daemon=True)
        self._capture_thread.start()
        return dict(started=True, input_started=False, seconds=seconds, hz=hz)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--profile', required=True, type=Path)
    parser.add_argument('--connection', required=True, type=Path)
    parser.add_argument('--crop', type=int, nargs=4)
    args = parser.parse_args()
    service = Service(HudReader.load(args.profile), crop=args.crop)
    token = secrets.token_urlsafe(32)
    server = make_server(service, token, max_request_bytes=34*1024*1024)
    args.connection.write_text(json.dumps(dict(url=f'http://127.0.0.1:{server.server_port}/rpc', token=token)), encoding='utf-8')
    print(json.dumps(dict(state='observation_only', port=server.server_port)), flush=True)
    try:
        server.serve_forever(poll_interval=.1)
    finally:
        service._capture_stop.set()
        service.tools.stop()
        server.server_close()
        args.connection.unlink(missing_ok=True)
