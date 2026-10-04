"""Small local combat recorder/player. F8 starts, F12 stops. No game-state AI."""
import argparse
import json
import math
import os
from pathlib import Path
import random
import time

from .windows_input import InputDevice, keyboard

COMBAT_KEYS = ('1', '2', '3', '4', '5', 'e', 'q', 'w', 'a', 's', 'd',
               'shift', 'space', 'attack', 'dodge')


def validate(recording):
    if recording.get('version') != 1:
        raise ValueError('Unsupported recording version')
    duration = recording['duration']
    if not math.isfinite(duration) or not 0 < duration <= 120:
        raise ValueError('Duration must be within 0..120 seconds')
    events = recording['events']
    if not isinstance(events, list) or len(events) > 30000:
        raise ValueError('Invalid event count')
    held, previous = set(), 0
    for event in events:
        at, key, down = event['at'], event['key'], event['down']
        if not math.isfinite(at) or not previous <= at <= duration:
            raise ValueError('Events must be ordered and within duration')
        if key not in COMBAT_KEYS or type(down) is not bool:
            raise ValueError('Invalid combat input')
        if (key in held) == down:
            raise ValueError('Unbalanced key transition')
        held.add(key) if down else held.remove(key)
        previous = at
    if held:
        raise ValueError('Recording ends with held keys')
    return recording


def record(device, seconds=60, clock=time.monotonic, sleep=time.sleep):
    if not math.isfinite(seconds) or not 0 < seconds <= 120:
        raise ValueError('Recording limit must be within 0..120 seconds')
    device.check()
    if any(device.pressed(key) for key in COMBAT_KEYS):
        raise RuntimeError('Release combat keys before recording starts')
    started = clock()
    held, events = set(), []
    reason = 'duration limit'
    elapsed = 0
    try:
        while True:
            device.check()
            elapsed = min(seconds, clock() - started)
            if elapsed >= seconds:
                break
            for key in COMBAT_KEYS:
                down = device.pressed(key)
                if down != (key in held):
                    events.append({'at': elapsed, 'key': key, 'down': down})
                    held.add(key) if down else held.remove(key)
            sleep(.005)
    except RuntimeError as error:
        reason = str(error)
        elapsed = min(seconds, clock() - started)
    # Only close the saved timeline; recording never sends key-up to the user.
    for key in sorted(held):
        events.append({'at': elapsed, 'key': key, 'down': False})
    return validate({'version': 1, 'duration': max(elapsed, .001),
                     'events': events, 'stop_reason': reason})


def play(recording, device, repeats=1, variation=0, clock=time.monotonic,
         sleep=time.sleep, rng=None):
    validate(recording)
    if type(repeats) is not int or not 1 <= repeats <= 10:
        raise ValueError('Repeats must be 1..10')
    if not math.isfinite(variation) or not 0 <= variation <= .03:
        raise ValueError('Cycle timing variation must be 0..0.03')
    if recording['duration'] * repeats * (1 + variation) > 300:
        raise ValueError('Playback must be at most 300 seconds')
    if not recording['events']:
        raise ValueError('Recording has no inputs')
    device.check()
    if any(device.pressed(key) for key in COMBAT_KEYS):
        raise RuntimeError('Release combat keys before playback starts')
    rng = rng or random.Random()
    held = set()

    def wait_until(deadline):
        while True:
            device.check()
            remaining = deadline - clock()
            if remaining <= 0:
                return
            sleep(min(.005, remaining))

    try:
        for _ in range(repeats):
            scale = rng.uniform(1 - variation, 1 + variation)
            started = clock()
            for event in recording['events']:
                wait_until(started + event['at'] * scale)
                key, down = event['key'], event['down']
                # Track before send so a partial/failed send is also released.
                if down:
                    held.add(key)
                device.send([keyboard(key, up=not down)])
                if not down:
                    held.discard(key)
            wait_until(started + recording['duration'] * scale)
    finally:
        release_error = None
        for key in sorted(held):
            try:
                device.send([keyboard(key, up=True)])
            except Exception as error:
                release_error = error
        if release_error is not None:
            raise release_error
    return {'cycles': repeats, 'events': len(recording['events']) * repeats}


def wait_start(device, seconds=60):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        device.check()
        if device.pressed('f8'):
            while device.pressed('f8'):
                device.check()
                time.sleep(.005)
            return
        time.sleep(.01)
    raise RuntimeError('F8 start timed out')


def wait_game_start(seconds=120, *, target_exe=None):
    target_exe = target_exe or os.environ.get("CATHERYNE_GAME_EXE")
    if not target_exe:
        raise ValueError("Configured game executable is required")
    """Allow the user to switch to the game without a five-second deadline."""
    deadline = time.monotonic() + seconds if seconds is not None else float('inf')
    while time.monotonic() < deadline:
        try:
            device = InputDevice(target_exe=target_exe)
        except (RuntimeError, OSError):
            time.sleep(.1)
            continue
        try:
            wait_start(device, max(.1, deadline - time.monotonic()))
            return device
        except RuntimeError as error:
            if 'Focus changed' not in str(error):
                raise
    raise RuntimeError('Game/F8 start timed out')


def signal(kind):
    """Status is always printed; audible cues are supplementary."""
    print(kind, flush=True)
    try:
        import winsound
        winsound.Beep(1000 if kind == 'RECORDING' else 600, 120)
    except (ImportError, RuntimeError):
        pass


def save_take(data, base, take):
    validate(data)
    path = base if take == 1 else base.with_name(f'{base.stem}-{take:03d}{base.suffix}')
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('x', encoding='utf-8') as stream:
        json.dump(data, stream, ensure_ascii=False, indent=2)
    return path


def record_takes(base, seconds, continuous=False, wait=wait_game_start,
                 capture=record, notify=signal, sleep=time.sleep):
    take = 1
    while True:
        print('Ready: switch to Genshin and press F8. F12 saves. Close this window to exit.', flush=True)
        device = wait(None if continuous else 120)
        # Do not announce success before verifying that input can be sampled.
        device.check()
        if any(device.pressed(key) for key in COMBAT_KEYS):
            print('Release combat keys, then press F8 again.', flush=True)
            continue
        notify('RECORDING')
        data = capture(device, seconds)
        path = save_take(data, base, take)
        print(f'SAVED: {path} | {len(data["events"])} events | {data["duration"]:.1f}s | {data["stop_reason"]}', flush=True)
        notify('SAVED' if data['events'] else 'EMPTY: no combat inputs detected')
        if not continuous:
            return
        take += 1
        while device.pressed('f12'):
            sleep(.01)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['record', 'play', 'inspect'])
    parser.add_argument('file', type=Path)
    parser.add_argument('--seconds', type=float, default=60)
    parser.add_argument('--repeats', type=int, default=1)
    parser.add_argument('--variation', type=float, default=0)
    parser.add_argument('--continuous', action='store_true', help='Return to F8 wait after saving each take')
    args = parser.parse_args()
    if args.mode == 'record':
        if args.file.exists():
            parser.error('Recording exists; choose a new filename')
        if not math.isfinite(args.seconds) or not 0 < args.seconds <= 120:
            parser.error('--seconds must be within 0..120')
        data = None
    else:
        data = validate(json.loads(args.file.read_text(encoding='utf-8')))
    if args.mode == 'inspect':
        print(json.dumps({'duration': data['duration'], 'events': len(data['events']),
                          'keys': sorted({e['key'] for e in data['events']})}))
        return
    if args.mode == 'record':
        record_takes(args.file, args.seconds, args.continuous)
    else:
        print('Switch to Genshin, then press F8 to start. F12: stop.', flush=True)
        device = wait_game_start()
        print(play(data, device, args.repeats, args.variation))


if __name__ == '__main__':
    try:
        main()
    except (RuntimeError, OSError, ValueError) as error:
        print(f'STOPPED: {error}', flush=True)
        raise SystemExit(1)
    except KeyboardInterrupt:
        print('Recorder closed.', flush=True)
