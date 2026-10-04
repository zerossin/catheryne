import threading
from types import SimpleNamespace
import unittest
from PIL import Image
from story_control.game_capture import WindowCapture


class Function:
    def __init__(self, callback): self.callback = callback
    def __call__(self, *args): return self.callback(*args)


class Device:
    def __init__(self, cancel):
        self.hwnd, self.checks = 1, 0
        self.fail_after = None
        def rect(hwnd, pointer):
            pointer._obj.right, pointer._obj.bottom = 100, 50
            return 1
        def origin(hwnd, pointer):
            pointer._obj.x, pointer._obj.y = 20, 30
            return 1
        self.user = SimpleNamespace(GetClientRect=Function(rect), ClientToScreen=Function(origin))
    def check(self):
        self.checks += 1
        if self.fail_after and self.checks >= self.fail_after:
            raise RuntimeError('Focus changed')


class CaptureTests(unittest.TestCase):
    def test_exact_client_bounds_and_checks_before_and_after(self):
        requests = []
        def grab(**kwargs):
            requests.append(kwargs)
            return Image.new('RGB', (100,50))
        capture = WindowCapture('test', grab=grab, device_factory=Device)
        frame, at, context = capture(threading.Event())
        self.assertEqual(requests, [dict(bbox=(20,30,120,80), all_screens=True)])
        self.assertEqual(capture.device.checks, 2)
        self.assertEqual(context, 'test')
        self.assertEqual(frame.size, (100,50))

    def test_focus_changed_during_capture_discards_frame(self):
        device = Device(None)
        device.fail_after = 2
        capture = WindowCapture('test', grab=lambda **kwargs: Image.new('RGB',(100,50)),
                                device_factory=lambda cancel: device)
        with self.assertRaisesRegex(RuntimeError, 'Focus changed'):
            capture(threading.Event())
        self.assertEqual(capture.frames, 0)

    def test_cancelled_capture_never_reads_screen(self):
        def grab(**kwargs): self.fail('Captured after cancellation')
        capture = WindowCapture('test', grab=grab, device_factory=Device)
        cancel = threading.Event(); cancel.set()
        self.assertIsNone(capture(cancel))


if __name__ == '__main__': unittest.main()
