"""Typed combat evidence and fast local policy. No game or model calls."""
from dataclasses import dataclass
import math


FIELDS = {
    'combat_ui', 'dead', 'retry_visible', 'retry_seconds', 'active_slot',
    'hp_ratio', 'e_ready', 'e_seconds', 'q_ready', 'world_paused',
    'target_visible', 'target_in_range', 'target_defeated', 'approach_clear',
    'target_hp_ratio',
    'target_distance',
    'controls_visible', 'heal_ready',
}


def finite(value):
    return type(value) in (int, float) and math.isfinite(value)


@dataclass(frozen=True)
class Signal:
    value: object
    captured_ms: float
    quality: float
    source: str

    def __post_init__(self):
        if (not finite(self.captured_ms) or self.captured_ms < 0 or
                not finite(self.quality) or not 0 <= self.quality <= 1 or
                not isinstance(self.source, str) or not self.source):
            raise ValueError('Invalid signal metadata')


@dataclass(frozen=True)
class CombatSnapshot:
    # Immutable pairs, not a second mutable state store.
    signals: tuple

    def __post_init__(self):
        object.__setattr__(self, 'signals', tuple(self.signals))
        names = []
        for name, signal in self.signals:
            if name not in FIELDS or not isinstance(signal, Signal):
                raise ValueError('Unknown combat signal')
            names.append(name)
            value = signal.value
            if value is None:
                continue
            if name in ('retry_seconds', 'e_seconds', 'target_distance'):
                valid = finite(value) and 0 <= value <= 3600
            elif name in ('hp_ratio', 'target_hp_ratio'):
                valid = finite(value) and 0 <= value <= 1
            elif name == 'active_slot':
                valid = type(value) is int and 1 <= value <= 5
            else:
                valid = type(value) is bool
            if not valid:
                raise ValueError(f'Invalid value for {name}')
        if len(names) != len(set(names)):
            raise ValueError('Duplicate combat signal')

    def read(self, name, now, ttl=400, minimum_quality=.9):
        signal = dict(self.signals).get(name)
        if (signal is None or not 0 <= now - signal.captured_ms <= ttl or
                signal.quality < minimum_quality):
            return None
        return signal.value


@dataclass(frozen=True)
class CombatPlan:
    """Supervisor-provided scope. No default attack or recovery authorization.

    Slots identify the observed party context, NOT character recognition proof.
    The context must be changed whenever the party, target or encounter changes.
    """
    context: str
    expires_ms: float
    allowed_slots: tuple
    rotation: tuple = ('e', 'q', 'attack')
    low_hp: float = .3
    survival: str | None = None
    retry: bool = False
    retry_budget_ms: float = 1500
    require_target: bool = False
    allow_approach: bool = False

    def __post_init__(self):
        object.__setattr__(self, 'allowed_slots', tuple(self.allowed_slots))
        object.__setattr__(self, 'rotation', tuple(self.rotation))
        if (not isinstance(self.context, str) or not self.context or
                not finite(self.expires_ms) or self.expires_ms < 0 or
                not self.allowed_slots or any(type(s) is not int or not 1 <= s <= 5 for s in self.allowed_slots) or
                len(set(self.allowed_slots)) != len(self.allowed_slots) or
                not self.rotation or any(s not in ('e', 'q', 'attack') for s in self.rotation) or
                not finite(self.low_hp) or not 0 < self.low_hp < 1 or
                self.survival not in (None, 'request_verified_pause', 'use_verified_heal') or
                type(self.retry) is not bool or not finite(self.retry_budget_ms) or self.retry_budget_ms <= 0 or
                type(self.require_target) is not bool or type(self.allow_approach) is not bool):
            raise ValueError('Invalid combat plan')


def decide_combat(snapshot, plan, context, now):
    """Return a semantic recommendation only, never an executable key sequence."""
    if not finite(now) or now < 0:
        raise ValueError('Invalid time')
    read = lambda name: snapshot.read(name, now)
    def result(route, reason, action=None):
        return dict(route=route, reason=reason, action=action)
    valid_plan = plan is not None and plan.context == context and now < plan.expires_ms
    # Death overrides stale strategy/HP, but never grants recovery permission.
    if read('dead') is True:
        if valid_plan and plan.retry and read('retry_visible') is True:
            remaining = read('retry_seconds')
            signal = dict(snapshot.signals).get('retry_seconds')
            if remaining is not None:
                remaining_ms = remaining * 1000 - (now - signal.captured_ms)
                if remaining_ms > plan.retry_budget_ms:
                    return result('rule', 'retry_within_deadline', 'retry')
            return result('astra', 'retry_deadline_unusable')
        return result('astra', 'death_requires_recovery')
    if read('world_paused') is True:
        return result('rule', 'world_pause_observed', 'wait')
    if not valid_plan:
        return result('astra', 'combat_plan_missing_or_expired')
    controls_confirmed = read('combat_ui') is True or (not plan.require_target and read('controls_visible') is True)
    if not controls_confirmed or read('dead') is not False:
        return result('astra', 'combat_mode_unconfirmed')
    if read('active_slot') not in plan.allowed_slots:
        return result('astra', 'active_slot_unconfirmed')
    hp = read('hp_ratio')
    if hp is None:
        return result('astra', 'health_unknown')
    if hp <= plan.low_hp:
        if plan.survival == 'use_verified_heal' and read('heal_ready') is not True:
            return result('astra', 'healing_unavailable_or_unverified')
        if plan.survival:
            return result('rule', 'low_health', plan.survival)
        return result('astra', 'low_health_no_verified_response')
    if plan.require_target:
        if read('target_defeated') is True:
            return result('rule', 'target_defeat_evidence', 'wait')
        if read('target_visible') is not True:
            return result('astra', 'target_unconfirmed')
        if read('target_in_range') is not True:
            if (read('target_in_range') is False and plan.allow_approach and
                    read('approach_clear') is True):
                return result('rule', 'verified_approach', 'approach')
            return result('astra', 'approach_unconfirmed')
    for action in plan.rotation:
        if action == 'e' and read('e_seconds') is not None and read('e_seconds') > 0:
            continue
        if action == 'attack' or read(action + '_ready') is True:
            return result('rule', 'combat_local_policy', action)
    return result('astra', 'no_verified_action_available')
