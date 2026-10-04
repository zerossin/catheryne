import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {connectStory} from './bridge.mjs';

async function fixture(t) {
  const calls = [], ui = [];
  const server = http.createServer(async (req, res) => {
    assert.equal(req.headers.authorization, 'Bearer test-only');
    let body = '';
    for await (const chunk of req) body += chunk;
    calls.push(JSON.parse(body));
    res.end(JSON.stringify({ok: true, result: {}}));
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'story-bridge-test-'));
  const config = path.join(dir, 'connection.json');
  await fs.writeFile(config, JSON.stringify({url: `http://127.0.0.1:${server.address().port}/rpc`, token: 'test-only'}));
  t.after(async () => { await new Promise(resolve => server.close(resolve)); await fs.rm(dir, {recursive:true}); });
  const window = {app:'test-only', id:1};
  const sky = {
    async get_window_state() { ui.push('capture'); return {window, screenshots:[{id:'test-screen'}]}; },
    async press_key(params) { ui.push(params); },
    async click(params) { ui.push(params); },
    async scroll(params) { ui.push(params); },
  };
  return {calls, ui, sky, story: await connectStory(config, sky, window)};
}

test('capture timestamps and input reservations surround official calls', async t => {
  const f = await fixture(t);
  await f.story.capture();
  await f.story.register({stage:'a', mode:'navigation', evidence:'fixture'});
  await f.story.click(10, 20, 'open', 'menu visible');
  assert.deepEqual(f.calls.map(c=>c.method), ['capture_started','capture_finished','observe',
    'begin_direct','end_direct','capture_started','capture_finished']);
  assert.ok(f.calls[2].params.captured_wall <= Date.now()/1000);
  assert.equal(f.ui[1].screenshotId, 'test-screen');
});

test('failed input records an error and cannot reuse the previous screenshot', async t => {
  const f = await fixture(t);
  await f.story.capture();
  f.sky.press_key = async () => { throw Error('test failure'); };
  await assert.rejects(f.story.pressKey('b','open','inventory'), /test failure/);
  assert.match(f.calls.at(-1).params.error, /test failure/);
  assert.throws(()=>f.story.register({}), /Capture/);
});

test('background sequence invalidates direct-input screenshot', async t => {
  const f = await fixture(t);
  await f.story.capture();
  await f.story.run({steps:[{keys:['w'],seconds:1}],intent:'test',expected:'test'});
  await assert.rejects(f.story.pressKey('f','test','test'), /Observe/);
  assert.equal(f.ui.length, 1);
});

test('scroll uses the shared input lease and observed screenshot', async t => {
  const f = await fixture(t);
  await assert.rejects(f.story.scroll(10,20,120,'select','next option'), /Observe/);
  assert.equal(f.ui.length, 0);
  await f.story.capture();
  await f.story.register({stage:'a', mode:'interaction', evidence:'fixture'});
  await f.story.scroll(10,20,120,'select','next option');
  assert.deepEqual(f.calls.map(c=>c.method), ['capture_started','capture_finished','observe',
    'begin_direct','end_direct','capture_started','capture_finished']);
  assert.deepEqual(f.ui[1], {x:10,y:20,scrollX:0,scrollY:120,
    screenshotId:'test-screen',window:{app:'test-only',id:1}});
});

test('dialogue starts asynchronously; stopping never requires another capture', async t => {
  const f = await fixture(t);
  assert.throws(()=>f.story.startDialogue(), /Capture/);
  await f.story.capture();
  await f.story.register({stage:'a',mode:'dialogue',evidence:'text and choices'});
  await f.story.startDialogue();
  assert.equal(f.calls.at(-1).params.repeat_dialogue, true);
  assert.deepEqual(f.calls.at(-1).params.steps,
    [{keys:['attack'],seconds:.08},{keys:[],seconds:.65}]);
  assert.throws(()=>f.story.checkDialogue(true,'still talking'), /Capture/);
  await f.story.checkDialogue(false,'operator stopped');
  assert.equal(f.calls.at(-1).params.captured_wall, 0);
  assert.equal(f.calls.at(-1).params.active, false);
  await f.story.capture();
  await f.story.checkDialogue(true,'choice remains inside the click area');
  assert.equal(f.calls.at(-1).method, 'check_dialogue');
  assert.equal(f.calls.at(-1).params.active, true);
  assert.ok(f.calls.at(-1).params.captured_wall <= Date.now()/1000);
  await f.story.checkDialogue(false,'world HUD appeared');
  assert.equal(f.calls.at(-1).params.active, false);
  assert.equal(f.ui.filter(x=>x==='capture').length, 2);
});


test('routine calls request compact replies while explicit reports preserve cursor history', async t => {
  const f = await fixture(t);
  await f.story.capture();
  await f.story.register({stage:'a',mode:'dialogue',evidence:'fixture'});
  await f.story.status();
  assert.ok(f.calls.every(c=>c.view==='compact'));
  assert.equal(f.calls.at(-1).method, 'status');
  await f.story.report(42);
  assert.equal(f.calls.at(-1).view, 'full');
  assert.equal(f.calls.at(-1).params.after_sequence, 42);
});
