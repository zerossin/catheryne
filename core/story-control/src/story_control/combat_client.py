"""Call the local game-assist host while Computer Use capture is in flight."""
import argparse
import json
from pathlib import Path
from .client import call


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--connection', required=True)
    parser.add_argument('method', choices=['status', 'observe', 'stop_observer', 'start', 'report', 'take_control', 'stop',
                                         'think', 'run', 'result', 'complete', 'rollback', 'shutdown'])
    parser.add_argument('--params', type=Path, help='JSON parameters file; contains no credentials')
    parser.add_argument('--output', type=Path, help='Save this report without connection credentials')
    args = parser.parse_args()
    result = call(args.connection, args.method, json.loads(args.params.read_text(encoding='utf-8')) if args.params else {})
    print(json.dumps(result, ensure_ascii=True, indent=2))
    if args.output:
        args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    raise SystemExit(0 if result['ok'] else 1)
