import unittest

from story_control.game_control import ControlState, Observation
from story_control.game_policy import MODEL, candidates


def state(choices=(), scene='dialogue', sensitive=False, confidence=1.0):
    return dict(scene=scene, dialogue_ui=scene == 'dialogue', branch_sensitive=sensitive,
                perception_confidence=confidence, choices=list(choices))


def response(s, choice='option_0'):
    return {'model': MODEL, 'answers': {'next_action': {
        'type': 'choice', 'choice': choice, 'confidence': 1.0,
        'probabilities': {k: float(k == choice) for k in candidates(s)}}}}


class ControlTests(unittest.TestCase):
    def start(self, s=None):
        c = ControlState()
        c.observe(Observation(1, 0, 'room', s or state()), 0)
        c.resume(0)
        return c

    def test_dialogue_and_cinematic_do_not_request_models(self):
        for scene, action in [('dialogue', 'advance'), ('cinematic', 'wait')]:
            c = self.start(state(scene=scene))
            for now in (1, 100, 200):
                result = c.recommend(now)
                self.assertEqual((result['route'], result['action']), ('rule', action))
                self.assertIsNone(c.pending)

    def test_new_frame_same_meaning_does_not_starve_reply(self):
        s = state(['continue', 'leave'])
        c = self.start(s)
        ticket = c.recommend(1)['ticket']
        generation = c.generation
        for frame in range(2, 5):
            c.observe(Observation(frame, frame * 100, 'room', s), frame * 100)
        self.assertEqual(c.generation, generation)
        self.assertEqual(c.resolve(ticket, response(s), 450)['action'], 'option_0')
        self.assertEqual(c.recommend(451)['reason'], 'awaiting_choice_change')

    def test_choice_order_text_and_context_changes_reject_reply(self):
        original = state(['continue', 'leave'])
        variants = [(state(['leave', 'continue']), 'room'),
                    (state(['new', 'leave']), 'room'), (original, 'reset-room')]
        for s, context in variants:
            c = self.start(original)
            ticket = c.recommend(1)['ticket']
            result = c.observe(Observation(2, 100, context, s), 100)
            self.assertTrue(result['cancel_current'])
            self.assertEqual(c.resolve(ticket, response(original), 200)['route'], 'discard')

    def test_exit_dialogue_cancels_and_notifies_once(self):
        c = self.start()
        self.assertEqual(c.recommend(1)['action'], 'advance')
        result = c.observe(Observation(2, 100, 'room', state(scene='special')), 100)
        self.assertTrue(result['cancel_current'])
        self.assertEqual(c.recommend(101)['route'], 'astra')
        self.assertEqual(c.recommend(102)['route'], 'wait')
        c.observe(Observation(3, 200, 'room', state(scene='special')), 200)
        self.assertEqual(c.recommend(201)['route'], 'wait')

    def test_observation_loss_cancels_without_a_reply(self):
        c = self.start(state(['continue']))
        ticket = c.recommend(1)['ticket']
        self.assertEqual(c.tick(751)['reason'], 'observation_expired')
        generation = c.generation
        c.tick(752)
        self.assertEqual(c.generation, generation)
        c.observe(Observation(2, 800, 'room', state(['continue'])), 800)
        self.assertEqual(c.resolve(ticket, response(state(['continue'])), 801)['route'], 'discard')

    def test_capture_gap_invalidates_even_without_tick(self):
        c = self.start(state(['continue']))
        ticket = c.recommend(1)['ticket']
        c.observe(Observation(2, 900, 'room', state(['continue'])), 900)
        self.assertEqual(c.resolve(ticket, response(state(['continue'])), 901)['route'], 'discard')

    def test_late_reply_rejected_even_when_observation_kept_fresh(self):
        s = state(['continue'])
        c = self.start(s)
        ticket = c.recommend(1)['ticket']
        for frame in range(2, 5):
            c.observe(Observation(frame, (frame - 1) * 500, 'room', s), (frame - 1) * 500)
        result = c.resolve(ticket, response(s), 1501)
        self.assertEqual(result['route'], 'astra')
        self.assertEqual(result['reason'], 'stale_observation')

    def test_one_request_in_flight_survives_invalidation(self):
        s = state(['continue'])
        c = self.start(s)
        ticket = c.recommend(1)['ticket']
        c.observe(Observation(2, 100, 'new-room', s), 100)
        self.assertEqual(c.recommend(101)['reason'], 'decision_in_flight')
        self.assertEqual(c.resolve(999, None, 102)['reason'], 'unknown_ticket')
        self.assertIsNotNone(c.pending)
        c.resolve(ticket, None, 103)
        self.assertEqual(c.recommend(104)['route'], 'jev')

    def test_error_does_not_retry_every_frame(self):
        c = self.start(state(['continue']))
        ticket = c.recommend(1)['ticket']
        self.assertEqual(c.resolve(ticket, None, 2)['route'], 'astra')
        self.assertEqual(c.recommend(3)['route'], 'wait')

    def test_explicit_stop_needs_new_observation_and_explicit_resume(self):
        c = self.start()
        c.stop(10)
        with self.assertRaises(ValueError):
            c.resume(11)
        c.observe(Observation(2, 20, 'room', state()), 20)
        self.assertEqual(c.recommend(21)['reason'], 'stopped')
        c.resume(22)
        self.assertEqual(c.recommend(23)['action'], 'advance')

    def test_confidence_jitter_vs_crossing_policy_threshold(self):
        c = self.start()
        generation = c.generation
        c.observe(Observation(2, 100, 'room', state(confidence=.98)), 100)
        self.assertEqual(c.generation, generation)
        result = c.observe(Observation(3, 200, 'room', state(confidence=.5)), 200)
        self.assertTrue(result['cancel_current'])
        self.assertEqual(c.recommend(201)['route'], 'astra')

    def test_unknown_sensitive_never_offers_choice(self):
        c = self.start(state(['continue'], sensitive=None))
        self.assertEqual(c.recommend(1)['route'], 'astra')

    def test_bad_order_future_and_invalid_clock_rejected(self):
        c = self.start()
        for obs in (Observation(1, 1, 'room', state()), Observation(2, 0, 'room', state()),
                    Observation(2, 100, 'room', state())):
            with self.assertRaises(ValueError):
                c.observe(obs, 10)
        for now in (9, True, float('nan'), float('inf')):
            with self.assertRaises(ValueError):
                c.tick(now)

    def test_caller_mutation_cannot_change_request_state(self):
        s = state(['continue'])
        c = self.start(s)
        request = c.recommend(1)
        s['choices'][0] = 'purchase'
        request['request']['state']['choices'][0] = 'purchase'
        # The serialized payload must not mutate the ticket used for validation.
        self.assertEqual(c.pending.observation.state['choices'], ['continue'])


if __name__ == '__main__':
    unittest.main()
