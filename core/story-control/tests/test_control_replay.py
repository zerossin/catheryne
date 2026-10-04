import json
from pathlib import Path
import unittest

from control_replay import replay


class ReplayTests(unittest.TestCase):
    def test_all_regression_scenarios(self):
        path = Path(__file__).resolve().parent / 'fixtures/control-scenarios.json'
        result = replay(json.loads(path.read_text(encoding='utf-8')))
        self.assertEqual(len(result['scenarios']), 6)
        self.assertTrue(result['passed'], result)

    def test_wrong_expectation_is_reported_as_failure(self):
        result = replay([{'id': 'negative', 'synthetic': True, 'events': [
            {'op': 'tick', 'at': 0, 'expect': {'route': 'rule'}}]}])
        self.assertFalse(result['passed'])

    def test_unmarked_data_rejected(self):
        with self.assertRaises(ValueError):
            replay([{'id': 'unmarked', 'events': []}])


if __name__ == '__main__':
    unittest.main()
