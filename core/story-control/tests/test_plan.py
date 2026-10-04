from pathlib import Path
import tempfile
import unittest

from story_control.director import StoryDirector, validate_plan
from story_control.plan import new_plan


class GenericPlanTests(unittest.TestCase):
    def test_unfilled_draft_cannot_run(self):
        with self.assertRaisesRegex(ValueError, 'Draft plan'):
            validate_plan(new_plan('any-quest', 'Any quest'))

    def test_same_director_handles_different_task_shapes(self):
        with tempfile.TemporaryDirectory() as directory:
            for ident, modes in [('talking', ['navigation', 'dialogue']),
                                 ('mechanism', ['puzzle', 'interaction']),
                                 ('chase', ['escape', 'recovery'])]:
                with self.subTest(plan=ident):
                    plan = new_plan(ident, ident)
                    plan['draft'] = False
                    plan['steps'][0].update(id=ident+'-step', modes=modes,
                        completion='fixture visible completion', checkpoint='fixture checkpoint',
                        failure_cost='fixture reset')
                    director = StoryDirector(plan, Path(directory)/(ident+'.jsonl'),
                        lambda: self.fail('No game input allowed'), threaded=False)
                    try:
                        import time
                        director.observe(stage=ident+'-step', mode=modes[0],
                            evidence='synthetic task fixture', captured_wall=time.time())
                        director.complete(stage=ident+'-step', evidence='fixture completion')
                        self.assertEqual(director.report()['progress']['completed'], 1)
                    finally:
                        director.close()
