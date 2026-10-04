import unittest

from story_control.game_session import GameSession


class SessionTests(unittest.TestCase):
    def test_stop_finishes_before_input(self):
        events = []

        class Dialogue:
            def stop(self):
                events.append('stopped')

        game = GameSession(Dialogue(), lambda args: events.append(args))
        game.move(2, turn=-120, sprint=True)
        self.assertEqual(events[0], 'stopped')
        self.assertEqual(events[1].keys, ['w', 'shift'])
        self.assertEqual(events[1].dx, -120)

    def test_failed_stop_prevents_input(self):
        class Dialogue:
            def stop(self):
                raise RuntimeError('failed')

        game = GameSession(Dialogue(), lambda args: self.fail('input after failed stop'))
        with self.assertRaises(RuntimeError):
            game.interact()


if __name__ == '__main__':
    unittest.main()
