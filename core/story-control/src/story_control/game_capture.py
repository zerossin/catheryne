"""Foreground-only local frame supplier for the user's game-assist program.

The main agent still uses Computer Use. This helper does not emit screenshot
payloads to the chat, activate windows, or access game memory. It uses Pillow's
Windows screen capture and the canonical windows_input foreground/F12 checks.
"""
import ctypes
from ctypes import wintypes
import threading

from PIL import ImageGrab
from .windows_input import InputDevice
from .combat_runtime import monotonic_ms


class WindowCapture:
    def __init__(self, context, *, grab=ImageGrab.grab, device_factory=None, target_exe=None):
        if device_factory is None:
            if not target_exe:
                raise ValueError("Configured game executable is required")
            device_factory = lambda cancel: InputDevice(cancel, target_exe=target_exe)
        self.context, self.grab, self.device_factory = context, grab, device_factory
        self.device = None
        self.bound_size = None
        self.last_latency_ms = None
        self.frames = 0
        self._lock = threading.Lock()

    def __call__(self, cancel):
        with self._lock:
            if cancel.is_set():
                return None
            if self.device is None:
                self.device = self.device_factory(cancel)
            device = self.device
            device.stop_event = cancel
            device.check()
            device.user.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
            device.user.ClientToScreen.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.POINT)]
            rect, origin = wintypes.RECT(), wintypes.POINT()
            if not device.user.GetClientRect(device.hwnd, ctypes.byref(rect)):
                raise RuntimeError('Cannot read game client rectangle')
            if not device.user.ClientToScreen(device.hwnd, ctypes.byref(origin)):
                raise RuntimeError('Cannot locate game client pixels')
            size = (rect.right-rect.left, rect.bottom-rect.top)
            if min(size) <= 0 or size[0]*size[1] > 16000000:
                raise RuntimeError('Unexpected game resolution')
            if self.bound_size is not None and size != self.bound_size:
                raise RuntimeError('Game resolution changed; recalibration required')
            self.bound_size = size
            started = monotonic_ms()
            image = self.grab(bbox=(origin.x, origin.y, origin.x+size[0], origin.y+size[1]),
                              all_screens=True).convert('RGB')
            # Discard a frame if focus/F12 changed during readback.
            device.check()
            if cancel.is_set():
                return None
            self.frames += 1
            self.last_latency_ms = monotonic_ms()-started
            return image, started, self.context

    def close(self):
        self.device = None
