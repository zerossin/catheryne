import json
from pathlib import Path
import tempfile
import threading
import unittest
from urllib.error import HTTPError
from urllib.request import urlopen

from story_control.director import StoryDirector
from story_control.rpc import make_server
from story_control.service import StoryService
from test_director import PLAN, FakeExecutor


class OperatorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name)/'journal.jsonl'
        self.director = StoryDirector(PLAN, self.path, FakeExecutor, threaded=False)
        self.addCleanup(self.director.close)

    def observe(self, mode='navigation'):
        return self.director.observe(stage='a', mode=mode, evidence='synthetic observation',
                                     captured_wall=self.director.wall())

    def settings(self, **changes):
        settings = dict(combat='auto', failure_limit=2, notifications=True)
        settings.update(changes)
        return self.director.configure(**settings)

    def test_stop_is_latched_across_agent_observations_and_restart(self):
        self.observe()
        self.director.stop()
        self.observe()
        with self.assertRaises(RuntimeError):
            self.director.begin_direct(intent='test', expected='test')
        self.director.close()
        restored = StoryDirector(PLAN, self.path, FakeExecutor, threaded=False)
        try:
            self.assertEqual(restored.report()['control'], 'user')
            restored.set_control(target='agent')
            with self.assertRaises(RuntimeError):
                restored.begin_direct(intent='test', expected='test')
            restored.observe(stage='a', mode='navigation', evidence='fresh', captured_wall=restored.wall())
            restored.begin_direct(intent='test', expected='test')
            restored.end_direct()
        finally:
            restored.close()

    def test_takeover_keeps_external_lease_until_return(self):
        self.observe()
        self.director.begin_direct(intent='test', expected='test')
        state = self.director.set_control(target='user')
        self.assertTrue(state['handoff_pending'])
        with self.assertRaises(RuntimeError):
            self.director.set_control(target='agent')
        self.director.end_direct()
        self.assertFalse(self.director.report()['handoff_pending'])
        self.assertIsNotNone(self.director.report()['pending_result'])

    def test_manual_combat_stops_without_native_input(self):
        self.settings(combat='manual')
        state = self.observe('combat')
        self.assertEqual(state['control'], 'user')
        self.assertEqual(state['alert'], 'combat_manual')
        self.assertTrue(state['stopped'])
        self.assertIsNone(self.director.worker)
        cursor = state['cursor']
        repeated = self.observe('combat')
        self.assertEqual(repeated['alert'], 'combat_manual')
        self.assertFalse(any(e['kind']=='control_changed' for e in self.director.report(cursor)['events']))
        self.director.set_control(target='agent')
        self.assertEqual(self.observe('combat')['control'], 'user')

    def test_takeover_cancels_managed_worker_before_resuming(self):
        self.observe()
        self.director.run(steps=[dict(keys=['w'], seconds=5)], intent='fake travel', expected='fake destination')
        self.director.set_control(target='user')
        self.director.worker.join(1)
        self.assertFalse(self.director.worker.is_alive())
        self.assertTrue(self.director.cancel.is_set())
        self.assertFalse(self.director.report()['handoff_pending'])
        self.director.set_control(target='agent')
        self.assertIsNotNone(self.director.report()['pending_result'])

    def test_failure_policy_uses_recorded_failures_and_persists(self):
        self.settings(combat='on_failure', failure_limit=1, notifications=False)
        self.observe('combat')
        self.director.begin_direct(intent='test', expected='test')
        self.director.end_direct(error='synthetic failure')
        state = self.director.result(outcome='failure', evidence='synthetic defeat')
        self.assertEqual(state['control'], 'user')
        self.assertEqual(state['alert'], 'combat_failures')
        self.director.close()
        restored = StoryDirector(PLAN, self.path, FakeExecutor, threaded=False)
        try:
            self.assertEqual(restored.report()['settings'], state['settings'])
        finally:
            restored.close()

    def test_invalid_settings_are_atomic(self):
        before = self.director.report()['settings']
        for change in [dict(combat='invalid'),dict(failure_limit=True),dict(failure_limit=0),dict(notifications='yes')]:
            with self.assertRaises(ValueError):
                self.settings(**change)
            self.assertEqual(self.director.report()['settings'], before)

    def test_user_control_blocks_protective_input(self):
        self.director.set_control(target='user')
        self.director.observe(stage='a', mode='combat', evidence='fake pause available',
                              captured_wall=self.director.wall(), pause_available=True)
        self.director._protect('synthetic late watchdog')
        self.assertIsNone(self.director.worker)

    def test_dashboard_assets_have_no_token_and_rpc_stays_authenticated(self):
        server = make_server(StoryService(self.director), 'secret-test-token')
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            base = f'http://127.0.0.1:{server.server_port}'
            with urlopen(base) as response:
                html = response.read().decode()
                self.assertIn('frame-ancestors', response.headers['Content-Security-Policy'])
                self.assertNotIn('secret-test-token', html)
                self.assertIn('dashboard.js', html)
            with urlopen(base+'/dashboard.js') as response:
                self.assertIn('set_control', response.read().decode())
            from urllib.request import Request
            with self.assertRaises(HTTPError) as denied:
                urlopen(Request(base+'/rpc', data=b'{"method":"stop"}'))
            self.assertEqual(denied.exception.code, 403)
        finally:
            server.shutdown()
            server.server_close()
            thread.join()
