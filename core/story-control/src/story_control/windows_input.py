"""Optional Windows foreground-only keyboard and relative mouse backend."""
from concurrent.futures import CancelledError
import ctypes as c
from ctypes import wintypes as w
import math
import os
import time


class Mouse(c.Structure):
    _fields_ = [('dx', w.LONG), ('dy', w.LONG), ('data', w.DWORD),
                ('flags', w.DWORD), ('time', w.DWORD), ('extra', c.c_size_t)]


class Keyboard(c.Structure):
    _fields_ = [('vk', w.WORD), ('scan', w.WORD), ('flags', w.DWORD),
                ('time', w.DWORD), ('extra', c.c_size_t)]


class Payload(c.Union):
    _fields_ = [('mouse', Mouse), ('keyboard', Keyboard)]


class Input(c.Structure):
    _fields_ = [('type', w.DWORD), ('payload', Payload)]


from .keys import SCANCODES as KEYS


def keyboard(key, up=False):
    if key in ('attack', 'dodge'):
        event = Input(type=0)
        down_flag, up_flag = (0x0002, 0x0004) if key == 'attack' else (0x0008, 0x0010)
        event.payload.mouse = Mouse(0, 0, 0, up_flag if up else down_flag, 0, 0)
        return event
    event = Input(type=1)
    event.payload.keyboard = Keyboard(0, KEYS[key], 0x0008 | (0x0002 if up else 0), 0, 0)
    return event


def mouse(dx, dy):
    event = Input(type=0)
    event.payload.mouse = Mouse(dx, dy, 0, 0x0001, 0, 0)
    return event


def virtual_key(key):
    special = {"shift": 0x10, "space": 0x20, "attack": 0x01, "dodge": 0x02,
               "escape": 0x1b, "enter": 0x0d, "f8": 0x77, "f12": 0x7b}
    return special[key] if key in special else ord(key.upper())


def isolated_session(kernel):
    """Fail closed when a child worker is accidentally launched on its parent."""
    child = os.environ.get('CATHERYNE_GAME_SESSION')
    if child is None:
        return False
    try:
        child, parent = int(child), int(os.environ['CATHERYNE_GAME_PARENT'])
    except (ValueError, KeyError) as error:
        raise RuntimeError('Invalid isolated session context') from error
    actual = w.DWORD()
    kernel.GetCurrentProcessId.restype = w.DWORD
    kernel.ProcessIdToSessionId.argtypes = [w.DWORD, c.POINTER(w.DWORD)]
    if not kernel.ProcessIdToSessionId(kernel.GetCurrentProcessId(), c.byref(actual)):
        raise c.WinError(c.get_last_error())
    if child < 0 or parent < 0 or child == parent or child != actual.value:
        raise RuntimeError('Isolated input cannot target the user desktop')
    return True


def activate_isolated(user, kernel, hwnd):
    """Temporarily share focus queues inside the verified child desktop only."""
    kernel.GetCurrentThreadId.restype = w.DWORD
    user.AttachThreadInput.argtypes = [w.DWORD, w.DWORD, w.BOOL]
    user.BringWindowToTop.argtypes = [w.HWND]
    current = kernel.GetCurrentThreadId()
    foreground = user.GetForegroundWindow()
    threads = [user.GetWindowThreadProcessId(foreground, None),
               user.GetWindowThreadProcessId(hwnd, None)]
    attached = []
    try:
        for thread in dict.fromkeys(threads):
            if thread and thread != current:
                if not user.AttachThreadInput(current, thread, True):
                    raise c.WinError(c.get_last_error())
                attached.append(thread)
        user.BringWindowToTop(hwnd)
        user.SetForegroundWindow(hwnd)
    finally:
        released = True
        for thread in reversed(attached):
            released = bool(user.AttachThreadInput(current, thread, False)) and released
        if not released:
            raise RuntimeError('Failed to release isolated focus queue')

class InputDevice:
    """One foreground-bound input backend shared by holds and recordings."""

    def __init__(self, stop_event=None, *, target_exe):
        if not hasattr(c, 'WinDLL'):
            raise RuntimeError('Native input requires Windows')
        user = c.WinDLL('user32', use_last_error=True)
        kernel = c.WinDLL('kernel32', use_last_error=True)
        isolated_session(kernel)
        user.GetForegroundWindow.restype = w.HWND
        user.GetWindowThreadProcessId.argtypes = [w.HWND, c.POINTER(w.DWORD)]
        user.GetAsyncKeyState.argtypes = [c.c_int]
        user.GetAsyncKeyState.restype = c.c_short
        user.SendInput.argtypes = [w.UINT, c.POINTER(Input), c.c_int]
        user.SendInput.restype = w.UINT
        kernel.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
        kernel.OpenProcess.restype = w.HANDLE
        kernel.QueryFullProcessImageNameW.argtypes = [w.HANDLE, w.DWORD, w.LPWSTR, c.POINTER(w.DWORD)]
        kernel.CloseHandle.argtypes = [w.HANDLE]
        hwnd = user.GetForegroundWindow()
        pid = w.DWORD()
        user.GetWindowThreadProcessId(hwnd, c.byref(pid))
        process = kernel.OpenProcess(0x1000, False, pid.value)
        if not process:
            raise c.WinError(c.get_last_error())
        try:
            size = w.DWORD(32768)
            path = c.create_unicode_buffer(size.value)
            if not kernel.QueryFullProcessImageNameW(process, 0, path, c.byref(size)):
                raise c.WinError(c.get_last_error())
            if path.value.casefold() != str(target_exe).casefold():
                raise RuntimeError('Foreground window is not the configured executable')
        finally:
            kernel.CloseHandle(process)
        self.user, self.hwnd, self.pid = user, hwnd, pid.value
        self.stop_event = stop_event

    def send(self, events):
        user = self.user
        if not events:
            return
        batch = (Input * len(events))(*events)
        if user.SendInput(len(events), batch, c.sizeof(Input)) != len(events):
            raise RuntimeError(f'SendInput failed or was partial: {c.get_last_error()}')

    def check(self):
        user, hwnd = self.user, self.hwnd
        stop = self.stop_event
        if stop is not None and stop.is_set():
            raise CancelledError('Session stop requested')
        if user.GetForegroundWindow() != hwnd:
            raise RuntimeError('Focus changed; input stopped')
        if not getattr(self, 'sending_escape', False) and user.GetAsyncKeyState(0x1b) & 0x8000:
            raise RuntimeError('Escape stop requested')
        if user.GetAsyncKeyState(0x7b) & 0x8000:
            raise RuntimeError('F12 stop requested')

    def pressed(self, key):
        return bool(self.user.GetAsyncKeyState(virtual_key(key)) & 0x8000)


def run(args):
    user = c.WinDLL("user32", use_last_error=True)
    user.SetThreadDpiAwarenessContext.argtypes = [c.c_void_p]
    user.SetThreadDpiAwarenessContext.restype = c.c_void_p
    prior = user.SetThreadDpiAwarenessContext(c.c_void_p(-4))
    try:
        return _run(args)
    finally:
        user.SetThreadDpiAwarenessContext(prior)


def _run(args):
    if not math.isfinite(args.seconds) or not 0.05 <= args.seconds <= 5:
        raise ValueError('Duration must be between 0.05 and 5 seconds')
    if abs(args.dx) > 1000 or abs(args.dy) > 1000:
        raise ValueError('Mouse delta must be within +/-1000')
    keys = list(dict.fromkeys(args.keys))
    if any(key not in KEYS for key in keys):
        raise ValueError('Unknown key')
    device = InputDevice(getattr(args, "stop_event", None), target_exe=args.target_exe)
    device.sending_escape = "escape" in keys
    send, check_focus = device.send, device.check
    # Avoid interfering with keys the user is already holding.
    for key in keys:
        if device.pressed(key):
            raise RuntimeError(f'Key already held: {key}')
    cursor = getattr(args, 'cursor', None)
    if cursor is not None:
        rect, point = w.RECT(), w.POINT()
        user = device.user
        user.GetClientRect.argtypes = [w.HWND, c.POINTER(w.RECT)]
        user.ClientToScreen.argtypes = [w.HWND, c.POINTER(w.POINT)]
        if not user.GetClientRect(device.hwnd, c.byref(rect)):
            raise RuntimeError('Cannot read client bounds')
        if (rect.right, rect.bottom) != (cursor['width'], cursor['height']):
            raise RuntimeError('Game size changed; observe again')
        point.x, point.y = cursor['x'], cursor['y']
        if not user.ClientToScreen(device.hwnd, c.byref(point)):
            raise RuntimeError('Cannot map game coordinates')
        check_focus()
        if not user.SetCursorPos(point.x, point.y):
            raise RuntimeError('Cannot position cursor')
    started = time.monotonic()
    moved_x = moved_y = 0
    try:
        check_focus()
        send([keyboard(key) for key in keys])
        while True:
            check_focus()
            fraction = min(1, (time.monotonic() - started) / args.seconds)
            x, y = round(args.dx * fraction), round(args.dy * fraction)
            if (x, y) != (moved_x, moved_y):
                send([mouse(x - moved_x, y - moved_y)])
                moved_x, moved_y = x, y
            if fraction == 1:
                break
            time.sleep(0.01)
    finally:
        send([keyboard(key, up=True) for key in reversed(keys)])
    return {'pid': device.pid, 'keys': keys, 'elapsed': round(time.monotonic() - started, 3),
            'mouse_delta': [moved_x, moved_y], 'released': True}


def focus_target(target_exe):
    """Explicit activation only, limited to the host's configured executable."""
    user = c.WinDLL('user32', use_last_error=True)
    kernel = c.WinDLL('kernel32', use_last_error=True)
    isolated = isolated_session(kernel)
    user.GetForegroundWindow.restype = w.HWND
    user.GetWindowThreadProcessId.argtypes = [w.HWND, c.POINTER(w.DWORD)]
    user.IsWindowVisible.argtypes = [w.HWND]
    user.IsIconic.argtypes = [w.HWND]
    user.ShowWindow.argtypes = [w.HWND, c.c_int]
    user.SetForegroundWindow.argtypes = [w.HWND]
    kernel.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
    kernel.OpenProcess.restype = w.HANDLE
    kernel.QueryFullProcessImageNameW.argtypes = [w.HANDLE, w.DWORD, w.LPWSTR, c.POINTER(w.DWORD)]
    kernel.CloseHandle.argtypes = [w.HANDLE]
    matches = []
    callback_type = c.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
    @callback_type
    def visit(hwnd, _):
        if not user.IsWindowVisible(hwnd):
            return True
        pid = w.DWORD()
        user.GetWindowThreadProcessId(hwnd, c.byref(pid))
        handle = kernel.OpenProcess(0x1000, False, pid.value)
        if handle:
            try:
                size = w.DWORD(32768)
                path = c.create_unicode_buffer(size.value)
                if kernel.QueryFullProcessImageNameW(handle, 0, path, c.byref(size)) and path.value.casefold() == str(target_exe).casefold():
                    matches.append(hwnd)
            finally:
                kernel.CloseHandle(handle)
        return True
    user.EnumWindows.argtypes = [callback_type, w.LPARAM]
    user.EnumWindows(visit, 0)
    if len(matches) != 1:
        raise RuntimeError('Expected one visible configured game window')
    hwnd = matches[0]
    if user.IsIconic(hwnd):
        user.ShowWindow(hwnd, 9)
    user.SetForegroundWindow(hwnd)
    if isolated and user.GetForegroundWindow() != hwnd:
        activate_isolated(user, kernel, hwnd)
    for _ in range(10):
        if user.GetForegroundWindow() == hwnd:
            return
        time.sleep(0.05)
    raise RuntimeError('Windows did not activate the configured game window')
