"""Supervised dialogue clicking; screen interpretation belongs to the caller."""
import math
import random
import threading
import time
from types import SimpleNamespace

from . import windows_input as game_input


def click_current_position(target_exe):
    if not target_exe:
        raise ValueError("Configured game executable is required")
    # Reuse the same foreground validation, F12 handling and button release path.
    game_input.run(SimpleNamespace(keys=['attack'], seconds=0.05, dx=0, dy=0, target_exe=target_exe))


def repeat_dialogue(click, cancel, *, interval=(.25, .4), expired=None,
                    minimum_period=.25):
    """One cancellable repetition loop with a mandatory minimum input period."""
    if not math.isfinite(minimum_period) or not .15 <= minimum_period <= 2:
        raise ValueError('Minimum repeat period must be 0.15..2 seconds')
    while not cancel.is_set():
        if expired is not None and expired():
            return 'observation lease expired'
        started = time.monotonic()
        click()
        delay = max(0., minimum_period - (time.monotonic() - started))
        if interval is not None:
            delay = max(delay, random.uniform(*interval))
        cancel.wait(delay)
    return 'stopped'


class DialogueClicker:
    """Start once, renew after observing dialogue, stop before other input.

    The cursor must already be positioned by the caller. No screen capture,
    account data, game memory access or automatic dialogue detection is used.
    """

    def __init__(self, click=None, interval=(0.25, 0.4), lease=20, *, target_exe=None):
        if click is None:
            click = lambda: click_current_position(target_exe)
        low, high = interval
        if not all(math.isfinite(n) for n in (low, high, lease)):
            raise ValueError('Timing must be finite')
        if not 0.15 <= low <= high <= 2 or not 1 <= lease <= 30:
            raise ValueError('Interval must be 0.15..2s; lease must be 1..30s')
        self.click = click
        self.interval = interval
        self.lease = lease
        self._stop = threading.Event()
        self._thread = None
        self._deadline = 0
        self.count = 0
        self.reason = 'not started'

    def start(self):
        if self._thread and self._thread.is_alive():
            raise RuntimeError('Already running; renew or stop the current session')
        self.count = 0
        self.reason = 'running'
        self._stop.clear()
        self._deadline = time.monotonic() + self.lease
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()
        return self.status()

    def renew(self):
        if not self._thread or not self._thread.is_alive():
            raise RuntimeError('Not running; observe the screen before restarting')
        self._deadline = time.monotonic() + self.lease
        return self.status()

    def stop(self):
        self._stop.set()
        if self._thread:
            self._thread.join()
        return self.status()

    def status(self):
        return {'running': bool(self._thread and self._thread.is_alive()),
                'clicks': self.count, 'reason': self.reason}

    def _run(self):
        def click():
            self.click()
            self.count += 1
        try:
            self.reason = repeat_dialogue(click, self._stop, interval=self.interval,
                minimum_period=self.interval[0],
                expired=lambda: time.monotonic() >= self._deadline)
        except Exception as error:
            self.reason = f'input stopped: {error}'
