import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from story_control.client import call

SOURCE = str(Path(__file__).resolve().parents[1]/'src')
BOOTSTRAP = 'import sys,runpy;sys.path.insert(0,sys.argv.pop(1));'


class LifecycleTests(unittest.TestCase):
    def test_real_process_default_is_no_input_and_shutdown_cleans_credentials(self):
        plan = Path(__file__).resolve().parents[1]/'examples/demo.json'
        with tempfile.TemporaryDirectory() as temporary:
            state = Path(temporary)
            connection = state/'connection.json'
            process = subprocess.Popen([sys.executable, '-B', '-c', BOOTSTRAP+"runpy.run_module('story_control',run_name='__main__')", SOURCE, 'serve',
                '--plan', str(plan), '--state-dir', str(state)], stdout=subprocess.PIPE,
                stderr=subprocess.PIPE, text=True)
            try:
                end = time.monotonic()+5
                while not connection.exists() and process.poll() is None and time.monotonic()<end:
                    time.sleep(.02)
                self.assertTrue(connection.exists(), 'Host failed to create a connection')
                result = call(connection, 'status')
                self.assertTrue(result['ok'])
                self.assertFalse(result['result']['input_enabled'])
                self.assertFalse(call(connection, 'run', {})['ok'])
                config = json.loads(connection.read_text())
                request = Request(config['url'], data=json.dumps(dict(method='status',view='compact')).encode(),
                    headers={'Authorization':'Bearer '+config['token'], 'Content-Type':'application/json'})
                with urlopen(request, timeout=1) as response:
                    compact = json.load(response)['result']
                self.assertNotIn('events', compact)
                self.assertFalse(compact['input_enabled'])
                self.assertEqual(compact['progress'], result['result']['progress'])
                with self.assertRaises(HTTPError) as rejected:
                    urlopen(Request(config['url'], data=b'{"method":"status"}'), timeout=1)
                self.assertEqual(rejected.exception.code, 403)
                self.assertTrue(call(connection, 'shutdown')['ok'])
                process.communicate(timeout=5)
                self.assertEqual(process.returncode, 0)
                self.assertFalse(connection.exists())
                events = [json.loads(line) for line in (state/'journal.jsonl').read_text().splitlines()]
                self.assertEqual(events[-1]['data']['reason'], 'host_shutdown')
            finally:
                if process.poll() is None:
                    process.terminate()
                process.communicate(timeout=5)

    def test_no_native_backend_loaded_by_core(self):
        command = [sys.executable, '-B', '-c',
            BOOTSTRAP+"import story_control.cli; assert 'story_control.windows_input' not in sys.modules", SOURCE]
        subprocess.run(command, check=True, timeout=5)
