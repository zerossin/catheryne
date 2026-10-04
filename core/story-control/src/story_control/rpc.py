"""Shared authenticated loopback transport; no application or input policy."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import secrets
from importlib.resources import files


def make_server(service, token, port=0, *, max_request_bytes=64*1024):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def do_GET(self):
            assets = {'/': ('dashboard.html', 'text/html; charset=utf-8'),
                      '/dashboard.js': ('dashboard.js', 'text/javascript; charset=utf-8')}
            if self.path not in assets:
                self.send_error(404)
                return
            name, mime = assets[self.path]
            payload = files('story_control').joinpath(name).read_bytes()
            self.send_response(200)
            self.send_header('Content-Type', mime)
            self.send_header('Content-Length', str(len(payload)))
            self.send_header('Cache-Control', 'no-store')
            self.send_header('Referrer-Policy', 'no-referrer')
            self.send_header('X-Content-Type-Options', 'nosniff')
            self.send_header('Content-Security-Policy', "default-src 'self'; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'")
            self.end_headers()
            self.wfile.write(payload)

        def do_POST(self):
            if self.path != '/rpc' or not secrets.compare_digest(self.headers.get('Authorization', ''), 'Bearer '+token):
                self.send_error(403)
                return
            self.connection.settimeout(5)
            try:
                size = int(self.headers.get('Content-Length', '0'))
                if not 0 < size <= max_request_bytes:
                    raise ValueError('Invalid request length')
                data = json.loads(self.rfile.read(size))
                result = dict(ok=True, result=service.call(data['method'], data.get('params', {}), view=data.get('view', 'full')))
                status = 200
            except Exception as error:
                result, status = dict(ok=False, error=str(error)), 400
            payload = json.dumps(result, allow_nan=False).encode()
            self.send_response(status)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
    return ThreadingHTTPServer(('127.0.0.1', port), Handler)
