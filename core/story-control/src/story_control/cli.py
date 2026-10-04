"""Portable CLI. No automatic app launch, capture, or input on startup."""
import argparse
from contextlib import ExitStack
import json
import os
from pathlib import Path
import secrets
import threading

from .director import StoryDirector, validate_plan
from .plan import new_plan
from .rpc import make_server
from .service import StoryService, HostLock

def state_directory():
    base = Path(os.environ.get('LOCALAPPDATA', Path.home()/'.local'/'state'))
    return base/'story-control'

def serve(args):
    plan = validate_plan(json.loads(args.plan.read_text(encoding='utf-8')))
    if args.enable_input and (os.name!='nt' or args.target_exe is None or not args.target_exe.is_file()):
        raise ValueError('Native input requires Windows and --target-exe pointing to an existing executable')
    def factory():
        if not args.enable_input:
            raise RuntimeError('Input disabled')
        from .actions import NativeExecutor
        return NativeExecutor(args.target_exe.resolve())
    directory = args.state_dir.resolve()
    directory.mkdir(parents=True, exist_ok=True)
    connection = directory/'connection.json'
    with ExitStack() as leases:
        leases.enter_context(HostLock(directory/'host.lock'))
        if args.enable_input:
            leases.enter_context(HostLock(args.input_lock or (Path(os.environ.get('LOCALAPPDATA', Path.home()/'.local'/'state'))/'Catheryne'/'runtime'/'input.lock')))
        def assistance(executor, **options):
            if not args.enable_input or args.assist_profile is None:
                raise ValueError('Local assistance needs a calibrated HUD profile')
            from .assistance import AssistSession, LocalHudSource
            source = LocalHudSource(args.target_exe.resolve(), args.assist_profile, options['context'])
            return AssistSession(source, executor, **options)
        director = StoryDirector(plan, directory/'journal.jsonl', factory, assistance_factory=assistance)
        server = None
        try:
            focus = None
            if args.enable_input:
                from .windows_input import focus_target
                focus = lambda: focus_target(args.target_exe.resolve())
            service = StoryService(director, args.enable_input, focus)
            token = secrets.token_urlsafe(32)
            server = make_server(service, token)
            service.shutdown = lambda: threading.Thread(target=server.shutdown,daemon=True).start()
            fd = os.open(connection, os.O_WRONLY|os.O_CREAT|os.O_TRUNC, 0o600)
            with os.fdopen(fd, 'w', encoding='utf-8') as stream:
                json.dump(dict(url=f'http://127.0.0.1:{server.server_port}/rpc',token=token,pid=os.getpid()),stream)
            print(json.dumps(dict(state='ready',input_enabled=args.enable_input,connection=str(connection))),flush=True)
            server.serve_forever(poll_interval=.1)
        except KeyboardInterrupt:
            pass
        finally:
            director.close()
            if server:
                server.server_close()
            connection.unlink(missing_ok=True)

def main():
    parser = argparse.ArgumentParser(prog='story-control')
    commands = parser.add_subparsers(dest='command',required=True)
    host = commands.add_parser('serve')
    host.add_argument('--plan',type=Path,required=True)
    host.add_argument('--state-dir',type=Path,default=state_directory())
    host.add_argument('--input-lock',type=Path,help='Shared application input lease')
    host.add_argument('--enable-input',action='store_true')
    host.add_argument('--target-exe',type=Path)
    host.add_argument('--assist-profile',type=Path)
    request = commands.add_parser('call')
    request.add_argument('method')
    request.add_argument('--state-dir',type=Path,default=state_directory())
    request.add_argument('--params',default='{}',help='JSON request parameters')
    ui = commands.add_parser('ui', help='Open the local operator dashboard')
    ui.add_argument('--state-dir',type=Path,default=state_directory())
    check = commands.add_parser('check-plan')
    check.add_argument('path',type=Path)
    create = commands.add_parser('new-plan')
    create.add_argument('--id',required=True)
    create.add_argument('--title',required=True)
    create.add_argument('--output',type=Path,required=True)
    args = parser.parse_args()
    try:
        if args.command=='serve':
            serve(args)
        elif args.command=='call':
            from .client import call
            result = call(args.state_dir/'connection.json',args.method,json.loads(args.params))
            print(json.dumps(result,ensure_ascii=True,indent=2))
            if not result['ok']:
                raise SystemExit(1)
        elif args.command=='ui':
            import webbrowser
            from urllib.parse import urlsplit, quote
            config = json.loads((args.state_dir/'connection.json').read_text(encoding='utf-8'))
            endpoint = urlsplit(config['url'])
            if endpoint.scheme != 'http' or endpoint.hostname != '127.0.0.1':
                raise ValueError('Dashboard requires a loopback host')
            if not webbrowser.open(f'http://127.0.0.1:{endpoint.port}/#token={quote(config["token"], safe="")}'):
                raise RuntimeError('Could not open the default browser')
            print('Operator dashboard opened. Keep this tab open for notifications.')
        elif args.command=='check-plan':
            plan = validate_plan(json.loads(args.path.read_text(encoding='utf-8')))
            print(json.dumps(dict(valid=True,plan_id=plan['id'],steps=len(plan['steps']))))
        else:
            plan = new_plan(args.id,args.title)
            args.output.parent.mkdir(parents=True,exist_ok=True)
            with args.output.open('x',encoding='utf-8') as stream:
                stream.write(json.dumps(plan,ensure_ascii=False,indent=2)+'\n')
            print('Draft created. Fill evidence criteria and set draft=false before use.')
    except (ValueError, OSError, RuntimeError) as error:
        parser.exit(2, f'{error}\n')
