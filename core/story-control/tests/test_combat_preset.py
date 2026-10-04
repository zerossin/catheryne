import unittest
from story_control.combat_preset import extract


class PresetTests(unittest.TestCase):
    def test_remove_movement_keep_gaps_and_close_boundary_holds(self):
        source = {'version': 1, 'duration': 4, 'events': [
            {'at': 0, 'key': 'w', 'down': True},
            {'at': .5, 'key': 'attack', 'down': True},
            {'at': 1.5, 'key': 'q', 'down': True},
            {'at': 1.6, 'key': 'q', 'down': False},
            {'at': 3, 'key': 'attack', 'down': False},
            {'at': 4, 'key': 'w', 'down': False}]}
        result = extract(source, 1, 2)
        self.assertEqual([e['key'] for e in result['events']], ['attack', 'q', 'q', 'attack'])
        self.assertEqual(result['events'][1]['at'], .5)
        self.assertEqual(result['events'][-1], {'at': 1, 'key': 'attack', 'down': False})
        self.assertEqual(source['events'][0]['key'], 'w')

    def test_empty_or_outside_range_rejected(self):
        source = {'version': 1, 'duration': 4, 'events': []}
        for start, end in ((2, 2), (-1, 2), (1, 5)):
            with self.assertRaises(ValueError):
                extract(source, start, end)


if __name__ == '__main__':
    unittest.main()
