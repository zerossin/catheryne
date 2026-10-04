"""Saved-image HUD -> local decision benchmark. No capture, API, or input."""
import argparse
from dataclasses import asdict
import json
import math
from pathlib import Path
import statistics
import time

from PIL import Image

from story_control.combat_hud import HudReader
from story_control.combat_state import CombatPlan
from story_control.game_control import ControlState, Observation


def preview(reader, image, plan=None, repeats=100):
    if type(repeats) is not int or not 1 <= repeats <= 1000:
        raise ValueError('Repeats must be 1..1000')
    times = []
    last = None
    for _ in range(repeats):
        started = time.perf_counter()
        snapshot, perception_ms = reader.read(image, 0)
        scene = 'combat' if snapshot.read('combat_ui', 0) is True else 'unknown'
        context = plan.context if plan else 'offline-unverified'
        state = dict(scene=scene, dialogue_ui=None, branch_sensitive=None,
                     perception_confidence=1.0, choices=[])
        control = ControlState()
        control.observe(Observation(1, 0, context, state, snapshot), 0)
        control.resume(0)  # Offline recommendation only; no OS interface exists.
        if plan:
            control.set_combat_plan(plan)
        result = control.recommend(0)
        elapsed = (time.perf_counter()-started)*1000
        times.append(elapsed)
        last = {'signals': {k: asdict(v) for k, v in snapshot.signals},
                'decision': result, 'last_perception_ms': perception_ms}
    ordered = sorted(times)
    return {'mode': 'saved_frame_local_preview_no_game', 'repeats': repeats,
            'timestamp_mode': 'offline_zero_not_live_observation',
            'processing_p50_ms': statistics.median(times),
            'processing_p95_ms': ordered[max(0, math.ceil(.95*len(ordered))-1)],
            'processing_max_ms': max(times), 'model_calls': 0,
            'excludes': ['image_loading', 'live_capture', 'physical_input', 'model_inference'], **last}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('profile', type=Path)
    parser.add_argument('image', type=Path)
    parser.add_argument('--plan', type=Path)
    parser.add_argument('--repeats', type=int, default=100)
    args = parser.parse_args()
    reader = HudReader.load(args.profile)
    plan = None
    if args.plan:
        data = json.loads(args.plan.read_text(encoding='utf-8'))
        data['allowed_slots'] = tuple(data['allowed_slots'])
        if 'rotation' in data:
            data['rotation'] = tuple(data['rotation'])
        plan = CombatPlan(**data)
    with Image.open(args.image) as source:
        result = preview(reader, source.convert('RGB'), plan, args.repeats)
    print(json.dumps(result, indent=2, ensure_ascii=False))


if __name__ == '__main__':
    main()
