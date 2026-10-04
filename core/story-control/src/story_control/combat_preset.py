"""Derive stationary combat presets from immutable recordings."""
import argparse
import hashlib
import json
import math
from pathlib import Path

from .combat_recording import validate

PRESET_KEYS = frozenset(('1', '2', '3', '4', '5', 'e', 'q', 'attack'))


def extract(source, start, end):
    validate(source)
    if not all(math.isfinite(t) for t in (start, end)) or not 0 <= start < end <= source['duration']:
        raise ValueError('Invalid crop range')
    held, events = set(), []
    for event in source['events']:
        if event['key'] in PRESET_KEYS and event['at'] < start:
            held.add(event['key']) if event['down'] else held.remove(event['key'])
    events.extend({'at': 0, 'key': key, 'down': True} for key in sorted(held))
    for event in source['events']:
        if event['key'] in PRESET_KEYS and start <= event['at'] < end:
            events.append(dict(event, at=event['at'] - start))
            held.add(event['key']) if event['down'] else held.remove(event['key'])
    events.extend({'at': end - start, 'key': key, 'down': False} for key in sorted(held))
    return validate({'version': 1, 'duration': end - start, 'events': events,
                     'preset': {'stationary': True, 'source_range': [start, end],
                                'verified_in_game': False}})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--start', required=True, type=float)
    parser.add_argument('--end', required=True, type=float)
    parser.add_argument('--slots', nargs=4, required=True, help='Expected characters in slots 1..4')
    args = parser.parse_args()
    raw = args.source.read_bytes()
    result = extract(json.loads(raw), args.start, args.end)
    result['preset'].update(source_file=args.source.name,
                            source_sha256=hashlib.sha256(raw).hexdigest(),
                            expected_slots=args.slots)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open('x', encoding='utf-8') as stream:
        json.dump(result, stream, ensure_ascii=False, indent=2)
    print(json.dumps({'duration': result['duration'], 'events': len(result['events'])}))


if __name__ == '__main__':
    main()
