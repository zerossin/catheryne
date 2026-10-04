"""Offline control-state core. Recommendations only: no OS, input, or network.

The caller serializes events and supplies timestamps from one monotonic clock.
tick() must be called independently of model responses to detect observation loss.
This module does not start a timer, acquire input ownership, or execute actions.
"""
from copy import deepcopy
from dataclasses import dataclass
import math

from .game_policy import candidates, request_for, review, validate_state
from .combat_state import CombatSnapshot, CombatPlan, decide_combat


def timestamp(value):
    if type(value) not in (int, float) or not math.isfinite(value) or value < 0:
        raise ValueError('Expected a finite nonnegative timestamp')


@dataclass(frozen=True)
class Observation:
    frame: int
    captured_ms: float
    context: str
    state: dict
    combat: CombatSnapshot | None = None

    def __post_init__(self):
        if type(self.frame) is not int or self.frame < 0:
            raise ValueError('Invalid frame ID')
        timestamp(self.captured_ms)
        if not isinstance(self.context, str) or not self.context.strip():
            raise ValueError('A scene/target context ID is required')
        validate_state(self.state)
        if self.combat is not None and not isinstance(self.combat, CombatSnapshot):
            raise ValueError('Expected CombatSnapshot')
        object.__setattr__(self, 'state', deepcopy(self.state))


@dataclass(frozen=True)
class DecisionTicket:
    id: int
    generation: int
    observation: Observation


class ControlState:
    """Single-consumer shadow state machine; instantiate once per session.

    Context must change on reset, target relocation, camera/window changes, or
    other invalidating events even when text and mode remain the same. Producing
    this signal is the observer's job, NOT an image-recognition feature here.
    """

    def __init__(self, observation_ttl_ms=750):
        timestamp(observation_ttl_ms)
        if observation_ttl_ms == 0:
            raise ValueError('Observation TTL must be positive')
        self.ttl = observation_ttl_ms
        self.latest = None
        self.generation = 0
        self.pending = None
        self.stopped = True
        self._now = 0
        self._next_ticket = 0
        self._notified = None
        self._expired = False
        self._resume_after = -1
        self._consumed_generation = None
        self.combat_plan = None
        self._combat_notice = None

    def set_combat_plan(self, plan):
        if not isinstance(plan, CombatPlan):
            raise ValueError('Expected CombatPlan')
        self.combat_plan = plan
        self._combat_notice = None

    def _time(self, now):
        timestamp(now)
        if now < self._now:
            raise ValueError('Clock moved backwards')
        self._now = now

    def _result(self, route, reason, **extra):
        return dict(mode='shadow_only', generation=self.generation,
                    route=route, reason=reason, **extra)

    @staticmethod
    def _meaning(obs):
        s = obs.state
        # Confidence jitter does not invalidate an otherwise identical scene;
        # crossing the policy threshold does, via the offered candidate set.
        return (obs.context, s['scene'], s['dialogue_ui'], s['branch_sensitive'],
                tuple(s['choices']), tuple(candidates(s)), obs.combat is not None)

    def observe(self, observation, now):
        self._time(now)
        if not isinstance(observation, Observation):
            raise ValueError('Expected Observation')
        if observation.captured_ms > now:
            raise ValueError('Observation is from the future')
        if self.latest and (observation.frame <= self.latest.frame or
                            observation.captured_ms <= self.latest.captured_ms):
            raise ValueError('Observation must advance frame and capture time')
        changed = (self.latest is None or self._expired or
                   observation.captured_ms - self.latest.captured_ms > self.ttl or
                   self._meaning(observation) != self._meaning(self.latest))
        self.latest = deepcopy(observation)
        self._expired = False
        if changed:
            self.generation += 1
            self._notified = None
        result = self.tick(now)
        result['cancel_current'] = changed or result['route'] == 'cancel'
        return result

    def tick(self, now):
        self._time(now)
        if self.latest is None:
            return self._result('cancel', 'no_observation')
        if now - self.latest.captured_ms > self.ttl:
            if not self._expired:
                self.generation += 1
                self._expired = True
                self._notified = None
            return self._result('cancel', 'observation_expired')
        if self.stopped:
            return self._result('cancel', 'stopped')
        return self._result('ready', 'fresh_observation')

    def stop(self, now):
        self._time(now)
        self.stopped = True
        self.generation += 1
        self._resume_after = self.latest.frame if self.latest else -1
        self._notified = None
        self.combat_plan = None
        self._combat_notice = None
        return self._result('cancel', 'explicit_stop')

    def resume(self, now):
        """Caller invokes only on user-authorized resume and a new observation."""
        self.tick(now)
        if (not self.latest or self._expired or
                self.latest.frame <= self._resume_after):
            raise ValueError('Resume requires a fresh post-stop observation')
        self.stopped = False
        return self._result('ready', 'resumed')

    def recommend(self, now):
        result = self.tick(now)
        if result['route'] != 'ready':
            return result
        if self.latest.combat is not None:
            combat = decide_combat(self.latest.combat, self.combat_plan,
                                   self.latest.context, now)
            # Typed combat evidence may override generic mode on death, but
            # ordinary attacks require BOTH generic and HUD combat evidence.
            supervised_field = (self.combat_plan is not None and not self.combat_plan.require_target
                                and self.latest.state['scene'] == 'field'
                                and self.latest.combat.read('controls_visible', now) is True)
            if self.latest.state['scene'] != 'combat' and not supervised_field and combat['reason'] not in (
                    'death_requires_recovery', 'retry_within_deadline',
                    'retry_deadline_unusable', 'world_pause_observed'):
                combat = dict(route='astra', reason='combat_scene_mismatch', action=None)
            if combat['route'] == 'astra':
                key = (self.generation, combat['reason'])
                if self._combat_notice == key:
                    return self._result('wait', 'combat_review_already_requested')
                self._combat_notice = key
            else:
                self._combat_notice = None
            return self._result(**combat)
        offered = candidates(self.latest.state)
        if self._consumed_generation == self.generation:
            return self._result('wait', 'awaiting_choice_change')
        if 'advance' in offered:
            return self._result('rule', 'ordinary_dialogue', action='advance')
        if 'wait' in offered:
            return self._result('rule', 'cinematic', action='wait')
        if len(offered) > 1:
            if self.pending is not None:
                return self._result('wait', 'decision_in_flight')
            if self._notified == self.generation:
                return self._result('wait', 'review_already_requested')
            self._next_ticket += 1
            ticket = DecisionTicket(self._next_ticket, self.generation, deepcopy(self.latest))
            self.pending = ticket
            return self._result('jev', 'ordinary_choices', ticket=ticket.id,
                                request=request_for(deepcopy(ticket.observation.state)))
        return self._supervisor('unsupported_or_uncertain')

    def _supervisor(self, reason):
        if self._notified == self.generation:
            return self._result('wait', 'review_already_requested')
        self._notified = self.generation
        return self._result('astra', reason)

    def resolve(self, ticket_id, response, now):
        """Consume a completion, including response=None on transport failure.

        The pending slot survives invalidation until transport completion: this
        avoids launching overlapping requests while an old request still runs.
        """
        status = self.tick(now)
        if self.pending is None or type(ticket_id) is not int or ticket_id != self.pending.id:
            return self._result('discard', 'unknown_ticket')
        ticket, self.pending = self.pending, None
        if (ticket.generation != self.generation or status['route'] != 'ready'
                or self.latest.combat is not None):
            return self._result('discard', 'invalidated_observation')
        checked = review(ticket.observation.state, response,
                         age_ms=now - ticket.observation.captured_ms)
        choice = checked['recommendation']
        if choice == 'escalate':
            return self._supervisor(checked['reason'])
        if choice not in candidates(self.latest.state):
            return self._result('discard', 'candidate_no_longer_valid')
        # Shadow recommendations are emitted once; do not spam the same option
        # on every fresh frame while awaiting its visible result.
        self._consumed_generation = self.generation
        return self._result('recommendation', 'validated_shadow_only', action=choice,
                            frame=self.latest.frame, source_frame=ticket.observation.frame)
