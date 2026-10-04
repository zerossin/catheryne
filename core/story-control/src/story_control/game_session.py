"""Reusable supervised actions sharing the existing input implementation."""
from types import SimpleNamespace
import random
import threading
import time

from . import windows_input as game_input
from .dialogue_clicker import DialogueClicker


from .actions import validate_sequence


class GameSession:
    def __init__(self, dialogue=None, run=game_input.run, *, target_exe=None):
        self.target_exe = target_exe
        self.dialogue = dialogue if dialogue is not None else DialogueClicker(target_exe=target_exe)
        self._run = run
        self._stop = threading.Event()
        self.history = []

    def stop(self):
        self._stop.set()
        return self.dialogue.stop()

    def resume(self):
        """Only call after explicit user resume and a fresh screen observation."""
        self._stop.clear()

    def act(self, keys=(), seconds=0.2, dx=0, dy=0):
        # Join the click worker before any movement, camera or interaction input.
        self.dialogue.stop()
        if self._stop.is_set():
            raise RuntimeError('Session stopped; explicit resume required')
        started = time.monotonic()
        try:
            result = self._run(SimpleNamespace(keys=list(keys), seconds=seconds,
                                              dx=dx, dy=dy, stop_event=self._stop, target_exe=self.target_exe))
        except Exception:
            self._stop.set()
            raise
        self.history.append({'keys': list(keys), 'seconds': seconds,
                             'elapsed': time.monotonic() - started, 'dx': dx, 'dy': dy})
        self.history[:] = self.history[-200:]
        return result

    def sequence(self, steps, jitter=0.0, rng=None):
        """Execute one <=5s observed chunk. No automatic repetition or targeting.

        Each step uses act's keys/seconds/dx/dy. Empty keys are checked waits.
        Jitter changes hold duration only, never position, order or target.
        Validate the entire worst-case duration before sending any input.
        """
        prepared = validate_sequence(steps, jitter)
        rng = rng or random.Random()
        results = []
        for step in prepared:
            step['seconds'] += rng.uniform(-jitter, jitter)
            results.append(self.act(**step))
        return results

    def move(self, seconds, turn=0, sprint=False, direction='w'):
        if direction not in ('w', 'a', 's', 'd'):
            raise ValueError('Direction must be w/a/s/d')
        return self.act([direction] + (['shift'] if sprint else []), seconds, dx=turn)

    def play_recording(self, recording, repeats=1, variation=0):
        """Replay a saved combat cycle after observing combat; F12/stop aborts."""
        from story_control.combat_recording import play, validate
        validate(recording)
        self.dialogue.stop()
        if self._stop.is_set():
            raise RuntimeError('Session stopped; explicit resume required')
        try:
            return play(recording, game_input.InputDevice(self._stop, target_exe=self.target_exe), repeats, variation)
        except Exception:
            self._stop.set()
            raise

    def look(self, dx=0, dy=0, seconds=0.3):
        return self.act(seconds=seconds, dx=dx, dy=dy)

    def interact(self):
        return self.act(['f'])
