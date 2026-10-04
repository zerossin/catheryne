"""Exercise the built desktop client against the real, input-disabled service."""
import json
from pathlib import Path
import secrets
import subprocess
import sys
import tempfile
import threading
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'core/story-control/src'))
from story_control.director import StoryDirector
from story_control.service import StoryService
from story_control.rpc import make_server

def forbidden():raise AssertionError('Native input must never be constructed')
with tempfile.TemporaryDirectory() as directory:
    folder=Path(directory)
    plan=json.loads((ROOT/'core/story-control/examples/demo.json').read_text())
    director=StoryDirector(plan,folder/'journal.jsonl',forbidden)
    service=StoryService(director,False)
    token=secrets.token_urlsafe(32)
    server=make_server(service,token)
    thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
    try:
        (folder/'connection.json').write_text(json.dumps({'url':f'http://127.0.0.1:{server.server_port}/rpc','token':token}))
        (folder/'ai-connection.json').write_text(json.dumps({'story_state_dir':str(folder)}))
        result=subprocess.run([str(Path(__file__).with_name('GenshinLauncher.exe')),'--story-integration-test',directory],timeout=20)
        print((folder/'result.txt').read_text())
        assert result.returncode==0
    finally:server.shutdown();server.server_close();director.close();thread.join()
