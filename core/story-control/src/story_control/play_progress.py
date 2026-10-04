"""Caller-reported progress, not automatic screen or quest recognition."""
from datetime import datetime, timezone
import json
from pathlib import Path
import time


class PlayProgress:
    def __init__(self, path=None, clock=time.monotonic):
        self.path = Path(path) if path else None
        self.clock = clock
        self.stage = None
        self.started = clock()
        self.failures = {}

    def observe(self, stage, obstacle=None, failed=False, normal_wait=False):
        """Call after a fresh screenshot; normal_wait includes expected cutscenes."""
        if not isinstance(stage, str) or not stage.strip():
            raise ValueError('A visible quest stage is required')
        if failed and not obstacle:
            raise ValueError('Name the obstacle before counting a failure')
        now = self.clock()
        changed = stage != self.stage
        elapsed = now - self.started
        if changed:
            self.stage, self.started, self.failures = stage, now, {}
        if failed:
            self.failures[obstacle] = self.failures.get(obstacle, 0) + 1
        elif obstacle:
            self.failures.pop(obstacle, None)
        result = {
            'at': datetime.now(timezone.utc).isoformat(), 'stage': stage,
            'changed': changed, 'stage_seconds': 0 if changed else round(elapsed, 2),
            'previous_stage_seconds': round(elapsed, 2) if changed else None,
            'obstacle': obstacle, 'failures': self.failures.get(obstacle, 0),
            'change_approach': self.failures.get(obstacle, 0) >= 2,
            'review_stall': not changed and elapsed >= 300 and not normal_wait,
            'normal_wait': normal_wait,
        }
        if self.path:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            with self.path.open('a', encoding='utf-8') as stream:
                stream.write(json.dumps(result, ensure_ascii=False) + '\n')
        return result
