import ctypes as c
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from story_control import windows_input as game
TARGET = r"C:\Example\Game.exe"


class InputTests(unittest.TestCase):
    def setup_api(self, foreground=None, path=TARGET, fail_down=False):
        events = []
        def image_name(handle, flags, buffer, size):
            buffer.value = path
            return 1
        def send(count, batch, size):
            events.extend((event.type, event.payload.keyboard.flags)
                          for event in batch if event.type == 1)
            if fail_down and batch[0].payload.keyboard.flags == 8:
                return 0
            return count
        user = SimpleNamespace(
            SetThreadDpiAwarenessContext=Mock(return_value=1),
            GetForegroundWindow=Mock(side_effect=foreground, return_value=123),
            GetWindowThreadProcessId=Mock(), GetAsyncKeyState=Mock(return_value=0),
            SendInput=Mock(side_effect=send))
        kernel = SimpleNamespace(OpenProcess=Mock(return_value=1),
                                 QueryFullProcessImageNameW=Mock(side_effect=image_name),
                                 CloseHandle=Mock())
        return user, kernel, events

    def execute(self, user, kernel, **kwargs):
        args = SimpleNamespace(seconds=0.05, keys=['w'], dx=0, dy=0, target_exe=TARGET)
        args.__dict__.update(kwargs)
        with patch.object(game.c, 'WinDLL', side_effect=[user, user, kernel], create=True), patch.object(game.c, 'get_last_error', return_value=5, create=True):
            return game.run(args)

    def test_success_releases(self):
        user, kernel, events = self.setup_api()
        result = self.execute(user, kernel, dx=12)
        self.assertEqual(events, [(1, 8), (1, 10)])
        self.assertEqual(result['mouse_delta'], [12, 0])

    def test_focus_loss_releases(self):
        user, kernel, events = self.setup_api(foreground=[123, 123, 999])
        with self.assertRaisesRegex(RuntimeError, 'Focus changed'):
            self.execute(user, kernel)
        self.assertEqual(events, [(1, 8), (1, 10)])

    def test_wrong_app_no_input(self):
        user, kernel, events = self.setup_api(path=r'C:\Other.exe')
        with self.assertRaisesRegex(RuntimeError, 'not the configured'):
            self.execute(user, kernel)
        user.SendInput.assert_not_called()

    def test_failed_down_still_releases(self):
        user, kernel, events = self.setup_api(fail_down=True)
        with self.assertRaisesRegex(RuntimeError, 'SendInput failed'):
            self.execute(user, kernel)
        self.assertEqual(events[-1], (1, 10))

    def test_escape_cancels_with_release(self):
        user, kernel, events = self.setup_api()
        # Key-held precheck, first Escape/F12 checks, then Escape during hold.
        user.GetAsyncKeyState.side_effect = [0, 0, 0, 0x8000]
        with self.assertRaisesRegex(RuntimeError, 'Escape stop'):
            self.execute(user, kernel)
        self.assertEqual(events, [(1, 8), (1, 10)])

    def test_duration_bounds(self):
        for seconds in [0, 6, float('nan'), float('inf')]:
            with self.assertRaises(ValueError):
                game.run(SimpleNamespace(seconds=seconds))


class IsolatedSessionTests(unittest.TestCase):
    def kernel(self, session):
        def get_session(pid, output):
            output._obj.value = session
            return 1
        return SimpleNamespace(GetCurrentProcessId=Mock(return_value=7),
                               ProcessIdToSessionId=Mock(side_effect=get_session))

    def test_rejects_parent_before_loading_input(self):
        user = SimpleNamespace(SendInput=Mock())
        with patch.dict(game.os.environ, {'CATHERYNE_GAME_SESSION': '3',
                                         'CATHERYNE_GAME_PARENT': '1'}), \
             patch.object(game.c, 'WinDLL', side_effect=[user, self.kernel(1)], create=True):
            with self.assertRaisesRegex(RuntimeError, 'user desktop'):
                game.InputDevice(target_exe=TARGET)
        user.SendInput.assert_not_called()

    def test_accepts_only_recorded_child(self):
        with patch.dict(game.os.environ, {'CATHERYNE_GAME_SESSION': '3',
                                         'CATHERYNE_GAME_PARENT': '1'}):
            self.assertTrue(game.isolated_session(self.kernel(3)))
            with self.assertRaisesRegex(RuntimeError, 'user desktop'):
                game.isolated_session(self.kernel(4))

    def test_focus_releases_all_attached_queues_on_failure(self):
        user = SimpleNamespace(GetForegroundWindow=Mock(return_value=123),
            GetWindowThreadProcessId=Mock(side_effect=[2, 3]),
            AttachThreadInput=Mock(side_effect=[True, True, False, True]),
            BringWindowToTop=Mock(), SetForegroundWindow=Mock())
        kernel = SimpleNamespace(GetCurrentThreadId=Mock(return_value=1))
        with self.assertRaisesRegex(RuntimeError, 'release isolated focus'):
            game.activate_isolated(user, kernel, 456)
        self.assertEqual(user.AttachThreadInput.call_args_list[-2:],
                         [unittest.mock.call(1, 3, False), unittest.mock.call(1, 2, False)])


if __name__ == '__main__':
    unittest.main()
