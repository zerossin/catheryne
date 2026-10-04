import json
import os
from pathlib import Path
import tempfile
import unittest

from PIL import Image, ImageDraw

from combat_fixture import create
from story_control.combat_hud import HudReader
from combat_preview import preview
from story_control.combat_state import CombatPlan


class HudTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.profile, self.frame = create(self.temp.name)
        self.reader = HudReader(self.profile, self.temp.name)

    def test_saved_pixels_to_signal_to_decision(self):
        result = preview(self.reader,self.frame,CombatPlan('synthetic-party',10000,(1,2)),repeats=2)
        self.assertEqual(result['signals']['e_seconds']['value'],8.2)
        self.assertAlmostEqual(result['signals']['hp_ratio']['value'],.6)
        self.assertEqual(result['decision']['action'],'q')
        self.assertEqual(result['model_calls'],0)

    def test_blank_frame_does_not_claim_alive_or_dead(self):
        snapshot, _ = self.reader.read(Image.new('RGB',self.frame.size),0)
        for field in ('hp_ratio','dead','active_slot','q_ready','e_seconds'):
            self.assertIsNone(snapshot.read(field,0),field)

    def test_live_hp_samples_with_unchanged_flight_profile(self):
        if not os.environ.get('CATHERYNE_PRIVATE_FIXTURES'): self.skipTest('Private game frames not distributed')
        root = Path(os.environ['CATHERYNE_PRIVATE_FIXTURES'])
        profile_path = root / 'observed' / 'hp-profile.json'
        reader = HudReader(json.loads(profile_path.read_text(encoding='utf-8')), profile_path.parent)
        for phase, expected in (('field', 1.0), ('bag', None), ('transition', None), ('stable', 1.0)):
            with self.subTest(phase=phase), Image.open(root / 'live-check' / f'{phase}-hp.png') as frame:
                snapshot, _ = reader.read(frame.convert('RGB'), 0)
                self.assertEqual(snapshot.read('hp_ratio', 0), expected)

    def test_resolution_mismatch_rejected(self):
        with self.assertRaises(ValueError):
            self.reader.read(Image.new('RGB',(1920,1080)),0)

    def test_broken_bar_is_unknown(self):
        image = self.frame.copy()
        ImageDraw.Draw(image).rectangle((1640,1350,1650,1357),fill='black')
        snapshot,_ = self.reader.read(image,0)
        self.assertIsNone(snapshot.read('hp_ratio',0))

    def test_similar_templates_abstain(self):
        d = self.profile['detectors'][0]
        d['templates'][1]['path'] = d['templates'][0]['path']
        reader = HudReader(self.profile,self.temp.name)
        snapshot,_ = reader.read(self.frame,0)
        self.assertIsNone(snapshot.read('combat_ui',0))

    def test_invalid_roi_rejected(self):
        self.profile['detectors'][0]['roi'][0]=3440
        with self.assertRaises(ValueError):
            HudReader(self.profile,self.temp.name)

    def test_fixed_text_overlay_only_bridged_when_both_sides_filled(self):
        self.profile['detectors'][6]['occluded_columns'] = [[40,60]]
        reader = HudReader(self.profile,self.temp.name)
        image = self.frame.copy()
        ImageDraw.Draw(image).rectangle((1640,1350,1659,1357),fill='white')
        snapshot,_ = reader.read(image,0)
        self.assertAlmostEqual(snapshot.read('hp_ratio',0),.6)
        ImageDraw.Draw(image).rectangle((1660,1350,1799,1357),fill='black')
        snapshot,_ = reader.read(image,0)
        self.assertIsNone(snapshot.read('hp_ratio',0))


if __name__ == '__main__':
    unittest.main()
