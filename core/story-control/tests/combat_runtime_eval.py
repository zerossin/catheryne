"""Wall-clock unattended worker evaluation using a simulated encounter only."""
import argparse
import json
from pathlib import Path
import threading
import time

from story_control.combat_runtime import CombatRuntime, monotonic_ms
from story_control.combat_state import CombatPlan, CombatSnapshot, Signal
from story_control.game_control import Observation


class SimulatedEncounter:
    def __init__(self, seconds):
        self.seconds = seconds
        self.started = monotonic_ms()
        self.frame = self.actions = 0
        self.attacks = 0
        self.distance = 3.
        self._lock = threading.Lock()

    def execute(self, action, cancel):
        with self._lock:
            self.actions += 1
            if action == 'approach':
                self.distance = max(0., self.distance-.5)
            if action == 'attack' and self.distance == 0:
                self.attacks += 1
        cancel.wait(.08)

    def cancel(self):
        pass

    def observe(self, cancel):
        now = monotonic_ms()
        with self._lock:
            self.frame += 1
            # Health reduction requires input; wall time alone cannot win.
            hp = max(0., 1-self.attacks / max(1, int(self.seconds*3)))
            values = dict(combat_ui=True, dead=False, world_paused=False,
                active_slot=1, hp_ratio=.9, e_ready=False, q_ready=False,
                target_visible=True, target_in_range=self.distance == 0,
                approach_clear=True, target_distance=self.distance,
                target_hp_ratio=hp, target_defeated=hp == 0)
            return Observation(self.frame, now, 'simulated-encounter',
                dict(scene='combat', dialogue_ui=False, branch_sensitive=None,
                     perception_confidence=1., choices=[]),
                CombatSnapshot(tuple((k, Signal(v, now, 1, 'simulation')) for k, v in values.items())))


def percentile(values, p):
    if not values:
        return None
    values = sorted(values)
    return values[min(len(values)-1, int((len(values)-1)*p))]


def evaluate(seconds=30):
    game = SimulatedEncounter(seconds)
    runtime = CombatRuntime(game)
    runtime.start(CombatPlan('simulated-encounter', monotonic_ms()+(seconds+5)*1000,
        (1,), require_target=True, allow_approach=True), source=game.observe)
    # Deliberately no status, model callback, input command, or plan renewal.
    # Capture and control continue on their own threads during this interval.
    threading.Event().wait(seconds)
    before_stop = runtime.status()
    stop_started = monotonic_ms()
    runtime.stop()
    status = runtime.join()
    stop_ms = monotonic_ms()-stop_started
    events = status['events']
    inputs = [e for e in events if e['kind'] == 'input_started']
    gaps = [b['at_ms']-a['at_ms'] for a, b in zip(inputs, inputs[1:])]
    ages = [e['observation_age_ms'] for e in inputs]
    result = dict(mode='simulated_wall_clock_no_game_input', unattended_seconds=seconds,
        state_before_stop=before_stop['state'], state_after_stop=status['state'],
        model_calls=status['model_calls'], actions=len(inputs),
        observations=sum(e['kind'] == 'observation' for e in events),
        observation_age_p95_ms=percentile(ages, .95),
        input_gap_max_ms=max(gaps, default=None), input_gap_p95_ms=percentile(gaps, .95),
        stop_to_join_ms=stop_ms, input_released=not status['input_alive'] and not status['owns_input'],
        completed=any(e['kind'] == 'target_defeat_confirmed' for e in events),
        live_eligible=False,
        unmeasured=['real capture latency', 'real HP/skill recognition accuracy',
                    'camera steering', 'real damage and death', 'human completion baseline',
                    'cross-process input ownership', 'real risk-to-input latency'])
    result['passed'] = (len(inputs) > seconds*2 and bool(ages) and percentile(ages, .95) <= 200
        and bool(gaps) and max(gaps) <= 1000 and stop_ms <= 200 and result['input_released']
        and before_stop['state'] == 'completed' and result['completed'])
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--seconds', type=float, default=30)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    if not 1 <= args.seconds <= 45:
        parser.error('seconds must be 1..45')
    result = evaluate(args.seconds)
    output = json.dumps(result, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(output+'\n', encoding='utf-8')
    print(output)
    raise SystemExit(0 if result['passed'] else 1)
