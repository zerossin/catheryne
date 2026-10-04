"""Authenticated local requests. Connection credentials are never returned."""
import json
from pathlib import Path
from urllib.parse import urlparse
from urllib.request import Request, urlopen
from urllib.error import HTTPError, URLError

def call(connection, method, params=None):
    config = json.loads(Path(connection).read_text(encoding='utf-8'))
    url = urlparse(config['url'])
    if url.hostname != '127.0.0.1' or url.scheme != 'http':
        raise ValueError('Loopback endpoint required')
    req = Request(config['url'], data=json.dumps(dict(method=method,params=params or {})).encode(),
        headers={'Content-Type':'application/json','Authorization':'Bearer '+config['token']})
    try:
        with urlopen(req, timeout=5) as response:
            return json.load(response)
    except HTTPError as error:
        return json.load(error)
    except URLError:
        return dict(ok=False,error='Local host unavailable; start the host and reconnect')
