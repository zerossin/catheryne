"""Catheryne MCP: thin adapter over the canonical story runtime and profile store."""
import argparse
from contextlib import closing
import hashlib
import json
import os
from pathlib import Path
import re
import sys
from typing import Literal
from mcp.server.fastmcp import FastMCP
from mcp.types import CallToolResult, TextContent, ImageContent

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'core/story-control/src'))
from story_control.client import call

DATA = Path(os.environ['CATHERYNE_TOOL_DATA']) if os.environ.get('CATHERYNE_TOOL_DATA') else Path(os.environ.get('LOCALAPPDATA', Path.home()/'.local/state'))/'Catheryne'
CONFIG = DATA/'ai-connection.json'

def settings():
    return json.loads(CONFIG.read_text(encoding='utf-8-sig')) if CONFIG.exists() else {}

def request(method, params=None):
    config=settings()
    if not config.get('story_state_dir'):
        return {'ok':False,'error':'STORY_NOT_CONNECTED','next':'Connect a story session in Catheryne settings.'}
    try:
        result=call(Path(config['story_state_dir'])/'connection.json',method,params)
        if not result.get('ok'):return {'ok':False,'error':'STORY_REQUEST_REJECTED','next':'Check the local task status and operation preconditions.'}
        return result
    except (OSError,ValueError,KeyError):
        return {'ok':False,'error':'STORY_UNAVAILABLE','next':'Start or reconnect the story host.'}

mcp=FastMCP('Catheryne',instructions='Read catheryne://guide and catheryne_context first. Use the active profile only. Observe before input; tool success is not game success. Stop on uncertainty. Never start competing input tools.')

def desktop_tool(name, arguments=None):
    """Same domain implementation as the embedded AI; no parallel data store."""
    import subprocess
    import uuid
    candidates=[ROOT/'GenshinLauncher.exe',ROOT/'apps/desktop/GenshinLauncher.exe']
    executable=next((p for p in candidates if p.is_file()),None)
    if executable is None:
        return {'error':'CATHERYNE_DESKTOP_NOT_INSTALLED'}
    if settings().get('profile','default')!='default':
        return {'error':'PROFILE_UNSUPPORTED'}
    request={'name':name,'arguments':arguments or {},'thread':os.environ.get('CATHERYNE_THREAD','mcp'),'request_id':str(uuid.uuid4())}
    response=subprocess.run([str(executable),'--ai-tool'],input=json.dumps(request),encoding='utf-8',capture_output=True,timeout=90 if name in ("catheryne_theater","catheryne_endgame") and (arguments or {}).get("command") in ("prepare","start","recommend") else 45,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0),env={**os.environ,'CATHERYNE_TOOL_DATA':str(DATA)})
    if response.returncode:
        # Only forward the desktop's public error contract, never process stderr.
        failure={'error':'CATHERYNE_TOOL_FAILED'}
        try:
            detail=json.loads(response.stdout.lstrip('\ufeff'))
        except (ValueError,TypeError):
            return failure
        if isinstance(detail,dict) and detail.get('error')=='CATHERYNE_TOOL_FAILED' and isinstance(detail.get('message'),str):
            failure['message']=detail['message']
        return failure
    return json.loads(response.stdout.lstrip('\ufeff'))

# Descriptions come from the same executable as embedded chat, including existing sessions.
_definitions = desktop_tool('__definitions')
canonical_definitions = {item['name']: item for item in _definitions} if isinstance(_definitions, list) else {}
canonical_descriptions = {name: item['description'] for name, item in canonical_definitions.items()}

def canonical_choices(name, field):
    # The desktop validates requests; unavailable metadata must not publish stale enums.
    definition = canonical_definitions.get(name)
    if definition is None:
        return str
    values = definition['inputSchema']['properties'][field]['enum']
    return Literal[tuple(values)]

EndgameCommand = canonical_choices("catheryne_endgame", "command")
EndgameMode = canonical_choices("catheryne_endgame", "mode")
TheaterCommand = canonical_choices('catheryne_theater', 'command')
GameCommand = canonical_choices('catheryne_game', 'command')
QuerySection = canonical_choices('catheryne_query', 'section')
ExecuteAction = canonical_choices('catheryne_execute', 'action')
LauncherOperation = canonical_choices('catheryne_launcher', 'operation')
GoalCategory = canonical_choices('catheryne_goal_add', 'category')
RequestOperation = canonical_choices('catheryne_request', 'operation')
ControlTarget = canonical_choices('catheryne_control', 'target')

@mcp.tool(description=canonical_descriptions.get("catheryne_endgame"))
def catheryne_endgame(command: EndgameCommand, mode: EndgameMode, parameters: dict) -> CallToolResult:
    result=desktop_tool("catheryne_endgame", {"command":command,"mode":mode,"parameters":parameters})
    image=result.pop("image_url",None)
    content=[TextContent(type="text",text=json.dumps(result,ensure_ascii=False))]
    if image:
        header,encoded=image.split(",",1)
        content.append(ImageContent(type="image",mimeType="image/png",data=encoded))
    return CallToolResult(content=content,isError="error" in result)

@mcp.tool(description=canonical_descriptions.get('catheryne_theater'))
def catheryne_theater(command: TheaterCommand, parameters: dict) -> CallToolResult:
    result=desktop_tool('catheryne_theater',{'command':command,'parameters':parameters})
    image=result.pop('image_url',None)
    content=[TextContent(type='text',text=json.dumps(result,ensure_ascii=False))]
    if image:
        header, encoded=image.split(',',1)
        content.append(ImageContent(type='image',mimeType='image/png',data=encoded))
    return CallToolResult(content=content,isError='error' in result)

# Publish the desktop's conditional parameter contract verbatim. Runtime calls
# still use the same validated desktop implementation.
if 'catheryne_theater' in canonical_definitions:
    mcp._tool_manager.get_tool('catheryne_theater').parameters = canonical_definitions['catheryne_theater']['inputSchema']

@mcp.tool(description=canonical_descriptions.get('catheryne_game', 'Use catheryne_context and the runtime guide before game input.'))
def catheryne_game(command: GameCommand, parameters: dict) -> CallToolResult:
    result=desktop_tool('catheryne_game',{'command':command,'parameters':parameters})
    image=result.pop('image_url',None)
    content=[TextContent(type='text',text=json.dumps(result,ensure_ascii=False))]
    if image:
        header, encoded = image.split(',',1)
        mime = header.removeprefix('data:').split(';',1)[0]
        if mime not in ('image/png','image/jpeg'):
            raise ValueError('Unsupported game image format')
        content.append(ImageContent(type='image',mimeType=mime,data=encoded))
    return CallToolResult(content=content,isError='error' in result)

@mcp.tool(description=canonical_descriptions.get('catheryne_execute'))
def catheryne_execute(action: ExecuteAction, parameters: dict) -> dict:
    """Execute an existing application action listed by catheryne_context.actions. Same domain code as the desktop. Explicit requests only; inspect task state/result."""
    return desktop_tool('catheryne_execute', {'action':action,'parameters':parameters})

@mcp.tool(description=canonical_descriptions.get('catheryne_launcher'))
def catheryne_launcher(operation: LauncherOperation, fps: int, enabled: bool):
    """Explicit launcher action through the same validated desktop domain implementation."""
    return desktop_tool('catheryne_launcher', {'operation':operation,'fps':fps,'enabled':enabled})

@mcp.tool(description=canonical_descriptions.get('catheryne_context'))
def catheryne_context() -> dict:
    """Read actual capabilities, known/unknown data, goals, preferences and last task reasons first."""
    return desktop_tool('catheryne_context')

@mcp.tool(description=canonical_descriptions.get('catheryne_query'))
def catheryne_query(section: QuerySection,offset:int=0,query:str='') -> dict:
    """Read bounded canonical local data. Missing is unknown, not zero."""
    return desktop_tool('catheryne_query',{'section':section,'offset':offset,'query':query})

@mcp.tool(description=canonical_descriptions.get('catheryne_goal_add'))
def catheryne_goal_add(title:str,category: GoalCategory, characterKey: str | None = None, targetLevel: int | None = None, targetTalent: int | None = None) -> dict:
    """Explicit user request only. Register a goal; never marks game execution complete."""
    args={'title':title,'category':category}
    args.update({key:value for key,value in {'characterKey':characterKey,'targetLevel':targetLevel,'targetTalent':targetTalent}.items() if value is not None})
    return desktop_tool('catheryne_goal_add',args)

@mcp.tool(description=canonical_descriptions.get('catheryne_request'))
def catheryne_request(title:str,operation: RequestOperation,task_id:str='',plan:list[str]|None=None) -> dict:
    """Explicit action request only. Persist a task, execute available operation and verify the result; unsupported execution is blocked. Register an ordered plan of observable milestones for game work. Retry a blocked prerequisite using its task_id and unchanged title/operation/plan."""
    args={'title':title,'operation':operation,'task_id':task_id}
    if plan is not None:args['plan']=plan
    return desktop_tool('catheryne_request',args)

@mcp.tool(description=canonical_descriptions.get('catheryne_control'))
def catheryne_control(target: ControlTarget,task_id:str='story') -> dict:
    """Stop a blocked request or control the existing story host. Resume still needs fresh observation."""
    return desktop_tool('catheryne_control',{'target':target,'task_id':task_id})

@mcp.tool(description=canonical_descriptions.get('catheryne_progress'))
def catheryne_progress(task_id: str, percent: float, evidence: str) -> dict:
    return desktop_tool('catheryne_progress', {'task_id':task_id,'percent':percent,'evidence':evidence})

@mcp.tool(description=canonical_descriptions.get('catheryne_preferences'))
def catheryne_preferences(text:str) -> dict:
    """Remember an explicitly stated preference. Does not grant permissions."""
    return desktop_tool('catheryne_preferences',{'text':text})

@mcp.tool()
def capabilities() -> dict:
    """Discover available modules without starting games, scans or input."""
    context=catheryne_context()
    return {**context.get('capabilities',{}),'api_version':2,'story_connected':bool(settings().get('story_state_dir')),'goal_execution':False,'native_input':'catheryne_game: shared foreground-bound story input and actual game images'}

@mcp.tool()
def goal_query() -> dict:
    """Read canonical goals. Registration is not execution or completion."""
    return {'state':catheryne_query('goals'),'execution_available':False}

@mcp.tool()
def account_query(section:Literal['characters','weapons','artifacts'],offset:int=0,limit:int=20) -> dict:
    """Read imported GOOD data. Missing sections mean uncollected, never zero."""
    if offset<0 or not 1<=limit<=100:raise ValueError('Invalid page bounds')
    result=catheryne_query(section,offset)
    if 'error' in result:return result
    items=list(result['items'])
    while len(items)<limit and result.get('next_offset') is not None:
        page=catheryne_query(section,result['next_offset']);items.extend(page['items']);result['next_offset']=page.get('next_offset')
    result['items']=items[:limit]
    result['next_offset']=offset+limit if result.get('total') is not None and result['total']>offset+limit else None
    return result

@mcp.tool()
def task_status(after_sequence:int=0) -> dict:
    """Read observed progress and recent events; does not renew input authority."""
    if after_sequence<0:raise ValueError('Invalid cursor')
    result=request('status',{'after_sequence':after_sequence})
    if result.get('ok'):
        data=result['result'];allowed={'plan_id','mode','stage','owner','stopped','paused','alert','pending_result','steps','session_seconds','input_enabled','next_sequence','history_lost'}
        result['result']={k:v for k,v in data.items() if k in allowed}
    return result

@mcp.tool()
def task_stop() -> dict:
    """Stop managed input. Does not assert that the game world is paused."""
    return request('stop')

@mcp.tool()
def action_execute(steps:list[dict], intent:str, expected:str) -> dict:
    """Run bounded steps only after fresh observation registration. Host enforces ownership and input policy. Inspect result in game afterwards."""
    return request('run',{'steps':steps,'intent':intent,'expected':expected})

@mcp.tool()
def observation_register(stage:str, mode:str, evidence:str, captured_wall:float, paused:bool=False) -> dict:
    """Register fresh evidence obtained through the user's Computer Use tool. Never invent observations."""
    return request('observe',dict(stage=stage,mode=mode,evidence=evidence,captured_wall=captured_wall,paused=paused,actor='agent'))

@mcp.tool()
def action_result(outcome:Literal['success','failure','unknown'], evidence:str) -> dict:
    """Record the observed result after execution; sending input is insufficient evidence."""
    return request('result',dict(outcome=outcome,evidence=evidence))

@mcp.resource('catheryne://guide')
def guide() -> str:
    return (Path(__file__).with_name('GUIDE.md')).read_text(encoding='utf-8')


@mcp.tool()
def daily_status() -> dict:
    """Read timestamped canonical attendance/resin. No network request or credentials returned."""
    return catheryne_query('daily')

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--config',type=Path);parser.add_argument('--embedded',action='store_true');args=parser.parse_args()
    if args.config:CONFIG=args.config
    if args.embedded:
        for name in ('capabilities','goal_query','account_query','task_status','task_stop','action_execute','observation_register','action_result','daily_status'):
            mcp.remove_tool(name)
    mcp.run(transport='stdio')
