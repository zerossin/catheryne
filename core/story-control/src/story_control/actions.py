"""Canonical bounded actions. Native input is imported only when explicitly used."""
import math
import threading
from types import SimpleNamespace

from .keys import SCANCODES
KEYS = frozenset(SCANCODES)

def validate_sequence(steps, jitter=0.0, *, max_seconds=5):
    if type(jitter) not in (int, float) or not math.isfinite(jitter) or not 0 <= jitter <= .1:
        raise ValueError('Jitter must be 0..0.1 seconds')
    if not isinstance(steps, list) or not 1 <= len(steps) <= 32:
        raise ValueError('Expected 1..32 steps')
    if type(max_seconds) not in (int, float) or not math.isfinite(max_seconds) or not 0 < max_seconds <= 30:
        raise ValueError('Sequence budget must be within 0..30 seconds')
    prepared = []
    for step in steps:
        if not isinstance(step, dict) or set(step)-{'keys','seconds','dx','dy','cursor'}:
            raise ValueError('Invalid step fields')
        keys = step.get('keys', [])
        seconds = step.get('seconds', .2)
        dx, dy = step.get('dx', 0), step.get('dy', 0)
        if not isinstance(keys, list) or any(k not in KEYS for k in keys):
            raise ValueError('Unknown keys')
        if type(seconds) not in (int,float) or not math.isfinite(seconds) or not .05+jitter <= seconds <= 5-jitter:
            raise ValueError('Duration must be 0.05..5 seconds')
        if any(type(v) is not int or abs(v)>1000 for v in (dx,dy)):
            raise ValueError('Mouse delta must be an integer within +/-1000')
        cursor = step.get('cursor')
        if cursor is not None:
            if (not isinstance(cursor, dict) or set(cursor) != {'x','y','width','height'}
                    or any(type(v) is not int for v in cursor.values())
                    or not 64 <= cursor['width'] <= 16384 or not 64 <= cursor['height'] <= 16384
                    or not 0 <= cursor['x'] < cursor['width'] or not 0 <= cursor['y'] < cursor['height']):
                raise ValueError('Invalid client cursor coordinates')
        prepared.append(dict(keys=list(dict.fromkeys(keys)), seconds=seconds, dx=dx, dy=dy))
        if cursor is not None:
            prepared[-1]["cursor"] = dict(cursor)
    if sum(s['seconds']+jitter for s in prepared)>max_seconds:
        raise ValueError(f'Sequence must last at most {max_seconds} seconds')
    return prepared

class NativeExecutor:
    def __init__(self, target_exe):
        self.target_exe = str(target_exe)
        self.cancel = threading.Event()
    def act(self, **step):
        from .windows_input import run
        step = validate_sequence([step])[0]
        return run(SimpleNamespace(**step, target_exe=self.target_exe, stop_event=self.cancel))
    def stop(self):
        self.cancel.set()
