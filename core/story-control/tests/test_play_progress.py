import unittest
from story_control.play_progress import PlayProgress


class ProgressTests(unittest.TestCase):
    def test_retry_and_stall_are_independent(self):
        now = [0]
        progress = PlayProgress(clock=lambda: now[0])
        progress.observe('contact')
        self.assertFalse(progress.observe('contact', 'pillar', True)['change_approach'])
        self.assertTrue(progress.observe('contact', 'pillar', True)['change_approach'])
        now[0] = 301
        self.assertTrue(progress.observe('contact')['review_stall'])
        self.assertFalse(progress.observe('contact', normal_wait=True)['review_stall'])
        result = progress.observe('bank')
        self.assertEqual(result['previous_stage_seconds'], 301)
        self.assertFalse(result['review_stall'])
        self.assertFalse(progress.observe('bank', 'pillar', True)['change_approach'])

    def test_success_clears_obstacle(self):
        progress = PlayProgress()
        progress.observe('contact', 'pillar', True)
        progress.observe('contact', 'pillar', False)
        self.assertEqual(progress.observe('contact', 'pillar', True)['failures'], 1)


if __name__ == '__main__':
    unittest.main()
