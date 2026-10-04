import unittest
from story_control.actions import validate_sequence


class ActionTests(unittest.TestCase):
    def test_whole_sequence_validated_before_execution(self):
        for steps in ([{'keys':['w'], 'seconds':.2}, {'keys':['unknown']}],
                      [{'keys':['w'], 'seconds':3}, {'keys':['w'], 'seconds':3}],
                      [{'seconds':True}], [{'seconds':float('nan')}], [{'dx':True}]):
            with self.subTest(steps=steps), self.assertRaises(ValueError):
                validate_sequence(steps)

    def test_duplicate_keys_normalized(self):
        self.assertEqual(validate_sequence([{'keys':['w','w']}])[0]['keys'], ['w'])

    def test_cursor_bounds_and_types(self):
        valid={'x':1279,'y':719,'width':1280,'height':720}
        self.assertEqual(validate_sequence([{'keys':['attack'],'cursor':valid}])[0]['cursor'],valid)
        for edits in ({'x':1280},{'y':-1},{'x':True},{'width':0}):
            with self.subTest(edits=edits), self.assertRaises(ValueError):
                validate_sequence([{'cursor':{**valid,**edits}}])

    def test_menu_keys_are_canonical(self):
        self.assertEqual(validate_sequence([{'keys':['escape','enter']}])[0]['keys'],['escape','enter'])
