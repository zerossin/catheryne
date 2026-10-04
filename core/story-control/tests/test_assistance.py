import json
from pathlib import Path
import tempfile
import time
import unittest

from story_control.assistance import AssistSession
from story_control.combat_runtime import monotonic_ms
from story_control.combat_state import CombatSnapshot, Signal
from story_control.director import StoryDirector
from story_control.game_control import Observation


class Executor:
    def __init__(self):
        self.actions = []
        self.stopped = False
    def act(self, **step):
        self.actions.append(step)
    def stop(self):
        self.stopped = True


class AssistanceTests(unittest.TestCase):
    def make(self, defeated=False):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        executor = Executor()
        self.frames = 0
        self.health = .9
        def source(cancel):
            if cancel.wait(.02):
                return None
            self.frames += 1
            now = monotonic_ms()
            values = dict(combat_ui=True,dead=False,active_slot=1,hp_ratio=self.health,heal_ready=True,
                e_ready=False,q_ready=False,world_paused=False,target_visible=True,
                target_in_range=True,target_defeated=defeated and self.frames>8,
                target_hp_ratio=max(.01,1-self.frames*.01))
            return Observation(self.frames,now,'test',
                dict(scene='combat',dialogue_ui=False,branch_sensitive=None,
                    perception_confidence=1.,choices=[]),
                CombatSnapshot(tuple((k,Signal(v,now,1.,'fixture')) for k,v in values.items())))
        def factory(native, **options):
            return AssistSession(source,native,**options)
        plan=dict(id='test',steps=[dict(id='a',title='test',modes=['combat'],
            tools=['sequence'],completion='observed',checkpoint='test',failure_cost='test')])
        director=StoryDirector(plan,Path(directory.name)/'journal.jsonl',lambda:executor,
            assistance_factory=factory)
        self.addCleanup(director.close)
        director.observe(stage='a',mode='combat',evidence='fixture',captured_wall=time.time())
        return director,executor

    def test_worker_continues_without_model_and_shares_stop_and_ownership(self):
        director,executor=self.make()
        director.start_assist(slots=[1],seconds=3)
        deadline=time.monotonic()+1.5
        while self.frames<=10 and time.monotonic()<deadline:
            time.sleep(.02)
        self.assertGreater(self.frames,10)
        self.assertGreater(len(executor.actions),1)
        self.assertEqual(director.report()['owner'],'combat')
        with self.assertRaises(RuntimeError):
            director.run(steps=[dict(keys=['attack'],seconds=.05)],intent='conflict',expected='none')
        director.set_control(target='user')
        director.worker.join(1)
        count=len(executor.actions)
        time.sleep(.1)
        self.assertEqual(count,len(executor.actions))
        self.assertTrue(executor.stopped)
        self.assertIsNone(director.report()['owner'])
        self.assertEqual(director.report()['control'],'user')
        events=[json.loads(x) for x in director.journal.read_text().splitlines()]
        self.assertTrue(any(x['kind']=='assistance_event' for x in events))

    def test_low_health_uses_authorized_food_without_waiting_for_model(self):
        director,executor=self.make()
        def act(**step):
            executor.actions.append(step)
            if step['keys']==['z']:
                self.health=.8
        executor.act=act
        director.start_assist(slots=[1],seconds=3,use_food=True)
        self.health=.1
        deadline=time.monotonic()+1
        while not any(s['keys']==['z'] for s in executor.actions) and time.monotonic()<deadline:
            time.sleep(.01)
        self.assertEqual(sum(s['keys']==['z'] for s in executor.actions),1)
        time.sleep(.25)
        self.assertEqual(sum(s['keys']==['z'] for s in executor.actions),1)
        director.stop()
        director.worker.join(1)
        events=[json.loads(x) for x in director.journal.read_text().splitlines()]
        self.assertTrue(any(x['kind']=='assistance_event' and
            x['data']['event']['kind']=='action_confirmed' and
            x['data']['event'].get('action')=='use_verified_heal' for x in events))

    def test_local_encounter_completion_does_not_claim_quest_completion(self):
        director,executor=self.make(True)
        director.start_assist(slots=[1],seconds=3)
        director.worker.join(2)
        report=director.report()
        self.assertIsNone(report['owner'])
        self.assertEqual(report['assistance']['state'],'completed')
        self.assertEqual(report['progress']['completed'],0)
        self.assertIsNotNone(report['pending_result'])
