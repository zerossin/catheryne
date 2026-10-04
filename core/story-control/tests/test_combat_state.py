import unittest

from story_control.combat_state import Signal, CombatSnapshot, CombatPlan, decide_combat
from story_control.game_control import ControlState, Observation


def snapshot(at=0, **changes):
    values = dict(combat_ui=True, dead=False, active_slot=2, hp_ratio=.8,
                  e_ready=False, q_ready=True, world_paused=False)
    values.update(changes)
    return CombatSnapshot(tuple((k, Signal(v, at, 1, 'synthetic')) for k, v in values.items()))


def plan(**changes):
    values = dict(context='trial-party', expires_ms=10000, allowed_slots=(1, 2))
    values.update(changes)
    return CombatPlan(**values)


class CombatTests(unittest.TestCase):
    def test_ready_action_selected_without_model(self):
        r = decide_combat(snapshot(), plan(), 'trial-party', 0)
        self.assertEqual((r['route'], r['action']), ('rule', 'q'))

    def test_unknown_skills_skip_to_allowed_attack(self):
        r = decide_combat(snapshot(e_ready=None, q_ready=None), plan(), 'trial-party', 0)
        self.assertEqual(r['action'], 'attack')

    def test_low_health_overrides_ready_skill(self):
        r = decide_combat(snapshot(hp_ratio=.1, e_ready=True), plan(), 'trial-party', 0)
        self.assertEqual(r['reason'], 'low_health_no_verified_response')
        r = decide_combat(snapshot(hp_ratio=.1, heal_ready=True), plan(survival='use_verified_heal'), 'trial-party', 0)
        self.assertEqual(r['action'], 'use_verified_heal')

    def test_stale_and_unknown_health_are_not_safe(self):
        for s in (snapshot(hp_ratio=None), snapshot()):
            self.assertIsNone(decide_combat(s, plan(), 'trial-party', 401)['action'])

    def test_signal_age_checked_individually(self):
        signals = dict(snapshot(at=500).signals)
        signals['hp_ratio'] = Signal(.8, 0, 1, 'old')
        self.assertEqual(decide_combat(CombatSnapshot(tuple(signals.items())), plan(), 'trial-party', 500)['reason'], 'health_unknown')

    def test_slot_and_context_mismatch(self):
        self.assertEqual(decide_combat(snapshot(active_slot=5), plan(), 'trial-party', 0)['reason'], 'active_slot_unconfirmed')
        self.assertEqual(decide_combat(snapshot(), plan(), 'other-party', 0)['reason'], 'combat_plan_missing_or_expired')

    def test_deadline_decreases_with_age_and_expired_never_clicks(self):
        s = snapshot(dead=True, retry_visible=True, retry_seconds=2)
        p = plan(retry=True, retry_budget_ms=1800)
        self.assertEqual(decide_combat(s, p, 'trial-party', 100)['action'], 'retry')
        self.assertIsNone(decide_combat(s, p, 'trial-party', 300)['action'])
        self.assertIsNone(decide_combat(s, plan(), 'trial-party', 100)['action'])

    def test_unknown_is_not_paused(self):
        r = decide_combat(snapshot(world_paused=None), None, 'trial-party', 0)
        self.assertNotEqual(r['reason'], 'world_pause_observed')

    def test_main_control_stop_overrides_recovery(self):
        c = ControlState()
        state = dict(scene='combat', dialogue_ui=False, branch_sensitive=None, perception_confidence=1., choices=[])
        c.observe(Observation(1, 0, 'trial-party', state, snapshot(dead=True,retry_visible=True,retry_seconds=10)), 0)
        c.set_combat_plan(plan(retry=True))
        self.assertEqual(c.recommend(1)['reason'], 'stopped')

    def test_new_urgent_reason_bypasses_duplicate_notice(self):
        c = ControlState()
        state = dict(scene='combat', dialogue_ui=False, branch_sensitive=None, perception_confidence=1., choices=[])
        c.observe(Observation(1, 0, 'trial-party', state, snapshot(hp_ratio=.1)), 0)
        c.resume(0)
        c.set_combat_plan(plan())
        self.assertEqual(c.recommend(1)['route'], 'astra')
        self.assertEqual(c.recommend(2)['route'], 'wait')
        c.observe(Observation(2, 10, 'trial-party', state, snapshot(at=10,dead=True)), 10)
        self.assertEqual(c.recommend(11)['reason'], 'death_requires_recovery')

    def test_invalid_values_rejected(self):
        for key, value in [('hp_ratio', True), ('active_slot', 7), ('dead', 0), ('e_seconds', -1)]:
            with self.assertRaises(ValueError):
                snapshot(**{key:value})

    def test_positive_cooldown_overrides_conflicting_ready_icon(self):
        r = decide_combat(snapshot(e_ready=True, e_seconds=3), plan(), 'trial-party', 0)
        self.assertEqual(r['action'], 'q')

    def test_combat_evidence_invalidates_pending_dialogue_reply(self):
        from story_control.game_policy import candidates, MODEL
        c = ControlState()
        s = dict(scene='dialogue',dialogue_ui=True,branch_sensitive=False,perception_confidence=1.,choices=['continue'])
        c.observe(Observation(1,0,'trial-party',s),0)
        c.resume(0)
        ticket = c.recommend(1)['ticket']
        c.observe(Observation(2,10,'trial-party',s,snapshot(at=10,dead=True)),10)
        response = {'model':MODEL,'answers':{'next_action':{'type':'choice','choice':'option_0','confidence':1.,
            'probabilities':{k:float(k=='option_0') for k in candidates(s)}}}}
        self.assertEqual(c.resolve(ticket,response,11)['route'],'discard')


if __name__ == '__main__':
    unittest.main()
