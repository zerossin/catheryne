import asyncio
import json
from pathlib import Path
import sys
import os
import tempfile
import sqlite3
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

async def main():
    with tempfile.TemporaryDirectory(prefix="catheryne-mcp-") as root:
        await check(root)

async def check(root):
    data=Path(root)/"GenshinCompanion";data.mkdir()
    with sqlite3.connect(data/"catheryne.db") as db:
        db.execute("CREATE TABLE observations(id INTEGER PRIMARY KEY,profile TEXT,kind TEXT,observed_at TEXT,payload TEXT)")
        db.execute("INSERT INTO observations VALUES(1,?,?,?,?)",("default","attendance","2000-01-01",json.dumps({"is_sign":True})))
    db.close()
    # A synthetic pre-existing snapshot exercises explicit saved-data start.
    import hashlib
    account={'format':'Catheryne.Account','version':1,'characters':[{'key':'Fischl','level':90}]}
    payload=json.dumps(account).encode('utf-8');digest=hashlib.sha256(payload).hexdigest()+'.json'
    folder=data/'profiles/default/account';(folder/'snapshots').mkdir(parents=True)
    (folder/'snapshots'/digest).write_bytes(payload);(folder/'current.json').write_text(json.dumps({'snapshot':digest}))
    async with stdio_client(StdioServerParameters(command=sys.executable,args=[str(Path(__file__).with_name('server.py'))],env={**os.environ,'CATHERYNE_TOOL_DATA':str(data)})) as (reader,writer):
        async with ClientSession(reader,writer) as session:
            await session.initialize()
            tools=await session.list_tools()
            assert len(tools.tools)==21
            endgame=json.loads((await session.call_tool("catheryne_endgame",{"command":"status","mode":"abyss","parameters":{}})).content[0].text)
            assert endgame["mode"]=="abyss" and "builds" in endgame
            request_schema=next(t.inputSchema for t in tools.tools if t.name=="catheryne_request")
            assert "plan" in request_schema["properties"]
            context=json.loads((await session.call_tool('catheryne_context',{})).content[0].text)
            theater=json.loads((await session.call_tool('catheryne_theater',{'command':'start','parameters':{'goal':'Synthetic theater goal','use_saved_account':True}})).content[0].text)
            theater_schema=next(t.inputSchema for t in tools.tools if t.name=='catheryne_theater')
            assert 'event_id' in theater_schema['properties']['parameters']['properties']
            assert 'assessment' in theater_schema['properties']['parameters']['properties'] and 'allOf' in theater_schema
            assert {'view','battleChanges','reviewedBattles','battle_ids'} <= set(theater_schema['properties']['parameters']['properties'])
            assert 'plan' in theater_schema['properties']['command']['enum']
            run=theater['session']['id']
            observed=json.loads((await session.call_tool('catheryne_theater',{'command':'observe','parameters':{'session_id':run,'expected_revision':0,'event_id':'initial','source':'user','evidence':'Visible selection screen','delta':{'phase':'choice','flowers':0}}})).content[0].text)
            assert observed['session']['state']['revision']==1 and observed['session']['state']['flowers']==0
            assert observed['view']=='decision' and 'lastPlan' not in observed['session']['state']
            details=json.loads((await session.call_tool('catheryne_theater',{'command':'plan','parameters':{'session_id':run}})).content[0].text)
            assert details['revision']==1 and details['battles']==[]
            failed=await session.call_tool('catheryne_theater',{'command':'observe','parameters':{'session_id':run,'expected_revision':0,'event_id':'stale','source':'user','evidence':'stale','delta':{'act':2}}})
            assert failed.isError
            assert any(a['action']=='daily.configure' for a in context['actions'])
            changed=json.loads((await session.call_tool('catheryne_execute',{'action':'daily.configure','parameters':{'resinThreshold':150}})).content[0].text)
            assert changed['State']=='completed'
            settings=json.loads((data/'settings.json').read_text(encoding='utf-8-sig'))
            assert settings['resinThreshold']==150
            for action in ('calendar.add','calendar.refresh','daily.disconnect','unlocker.update','achievements.refresh'):
                assert any(a['action']==action for a in context['actions']), action
            added=json.loads((await session.call_tool('catheryne_execute',{'action':'calendar.add','parameters':{'title':'transport test','due':'2030-01-02T18:30:00+09:00','repeat':'weekly'}})).content[0].text)
            assert added['State']=='completed'
            calendar=json.loads((await session.call_tool('catheryne_query',{'section':'calendar','offset':0})).content[0].text)
            assert any(e['Title']=='transport test' and e['Repeat']=='weekly' for e in calendar['items'])
            observed=await session.call_tool('daily_status',{})
            assert not observed.isError
            payload=json.loads(observed.content[0].text)
            assert payload['attendance']['value']['is_sign'] is True
            assert payload['attendance']['observed_at']=='2000-01-01'
            assert payload['resin'] is None
            goals=await session.call_tool('goal_query',{})
            assert json.loads(goals.content[0].text)['state']['Goals']==[]
            with sqlite3.connect(data/'catheryne.db') as db:
                db.execute('INSERT INTO observations(profile,kind,observed_at,payload) VALUES(?,?,?,?)',('default','goal-state','2000-01-02',json.dumps({'Schema':1,'Revision':1,'ResinLimit':60,'Goals':[{'Id':'synthetic','Title':'Synthetic goal','Paused':False}]})))
            db.close()
            goals=json.loads((await session.call_tool('goal_query',{})).content[0].text)
            assert goals['state']['ResinLimit']==60 and goals['state']['Goals'][0]['Id']=='synthetic'
            assert goals['execution_available'] is False
            result=await session.call_tool('capabilities',{})
            assert not result.isError
            assert not (await session.read_resource('catheryne://guide')).contents==[]
            invalid=await session.call_tool('account_query',{'section':'characters','limit':101})
            assert invalid.isError
            stopped=await session.call_tool('task_stop',{})
            assert not stopped.isError
            print('PASS: MCP initialize, twenty-one tools, local daily observation, guide, bounds validation, disconnected stop')
asyncio.run(main())
