import random
import unittest

from story_control.game_session import GameSession


class Dialogue:
    def stop(self):
        return {'running': False}


class SequenceTests(unittest.TestCase):
    def test_invalid_tail_never_sends_prefix(self):
        game = GameSession(Dialogue(), lambda a: self.fail('unexpected input'))
        with self.assertRaises(ValueError):
            game.sequence([{'keys': ['e']}, {'keys': ['unknown']}])

    def test_budget_accounts_for_jitter(self):
        game = GameSession(Dialogue(), lambda a: self.fail('unexpected input'))
        with self.assertRaises(ValueError):
            game.sequence([{'seconds': 2.5}, {'seconds': 2.5}], jitter=0.02)

    def test_random_duration_preserves_actions(self):
        sent = []
        game = GameSession(Dialogue(), sent.append)
        game.sequence([{'keys': ['1'], 'seconds': .3},
                       {'keys': [], 'seconds': .5},
                       {'keys': ['e'], 'seconds': .2}], jitter=.03, rng=random.Random(4))
        self.assertEqual([a.keys for a in sent], [['1'], [], ['e']])
        for actual, base in zip(sent, [.3, .5, .2]):
            self.assertLessEqual(abs(actual.seconds - base), .03)
        self.assertEqual(len(game.history), 3)

    def test_failure_latches_stop_and_cancels_tail(self):
        sent = []
        def fail(args):
            sent.append(args)
            raise RuntimeError('Focus changed')
        game = GameSession(Dialogue(), fail)
        with self.assertRaises(RuntimeError):
            game.sequence([{'keys': ['e']}, {'keys': ['q']}])
        with self.assertRaises(RuntimeError):
            game.interact()
        self.assertEqual(len(sent), 1)

    def test_stop_is_passed_to_active_input(self):
        game = GameSession(Dialogue(), lambda a: a.stop_event)
        event = game.act()
        game.stop()
        self.assertTrue(event.is_set())
        with self.assertRaises(RuntimeError):
            game.act()
        game.resume()
        self.assertFalse(game.act().is_set())


if __name__ == '__main__':
    unittest.main()
