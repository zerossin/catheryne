"""Replay synthetic observation events. Never captures, networks, or inputs."""
import argparse
import json
from pathlib import Path

from story_control.game_control import ControlState, Observation
from story_control.game_policy import MODEL, candidates


def replay(scenarios):
    results = []
    for scenario in scenarios:
        if scenario.get('synthetic') is not True:
            raise ValueError('This runner only accepts explicitly synthetic scenarios')
        control = ControlState()
        rows = []
        for event in scenario['events']:
            operation, now = event['op'], event['at']
            if operation == 'observe':
                result = control.observe(Observation(event['frame'], event.get('captured', now),
                                                     event['context'], event['state']), now)
            elif operation in ('resume', 'stop', 'tick', 'recommend'):
                result = getattr(control, operation)(now)
            elif operation == 'reply':
                # This is a deliberately fabricated response, NOT a model result.
                if control.pending is None:
                    raise ValueError('Synthetic reply requires an outstanding request')
                choice = event['choice']
                offered = candidates(control.pending.observation.state)
                response = {'model': MODEL, 'answers': {'next_action': {
                    'type': 'choice', 'choice': choice, 'confidence': 1.0,
                    'probabilities': {key: float(key == choice) for key in offered}}}}
                result = control.resolve(control.pending.id, response, now)
            else:
                raise ValueError('Unknown replay operation')
            expected = event.get('expect', {})
            passed = all(result.get(key) == value for key, value in expected.items())
            rows.append({'op': operation, 'at': now, 'passed': passed, 'result': result})
        results.append({'id': scenario['id'], 'passed': all(r['passed'] for r in rows), 'rows': rows})
    return {'mode': 'synthetic_replay_no_api_no_game',
            'passed': all(s['passed'] for s in results), 'scenarios': results}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('path', nargs='?', type=Path,
                        default=Path(__file__).resolve().parents[1] / 'experiments/control/scenarios.json')
    args = parser.parse_args()
    result = replay(json.loads(args.path.read_text(encoding='utf-8')))
    print(json.dumps(result, ensure_ascii=True, indent=2))
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
