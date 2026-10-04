// Official Computer Use client + the same story ledger. No alternate UI backend.
import fs from 'node:fs/promises';

export async function connectStory(connectionPath, sky, window) {
  const config = JSON.parse(await fs.readFile(connectionPath, 'utf8'));
  const endpoint = new URL(config.url);
  if (endpoint.protocol !== 'http:' || endpoint.hostname !== '127.0.0.1') throw Error('Loopback required');
  let observation = null, capturedWall = null;
  async function call(method, params = {}, view = 'compact') {
    const response = await fetch(config.url, {method: 'POST',
      headers: {'Content-Type': 'application/json', Authorization: `Bearer ${config.token}`},
      body: JSON.stringify({method, params, view}), signal: AbortSignal.timeout(5000)});
    const data = await response.json();
    if (!data.ok) throw Error(data.error);
    return data.result;
  }
  async function capture() {
    // Use capture START, never classification completion, as the image time.
    await call('capture_started');
    capturedWall = Date.now()/1000;
    observation = null;
    try {
      observation = await sky.get_window_state({window, include_screenshot: true, include_text: false});
    } catch (error) {
      await call('capture_finished', {error: String(error)});
      throw error;
    }
    await call('capture_finished');
    window = observation.window;
    return {captured_wall: capturedWall, screenshot_id: observation.screenshots?.[0]?.id};
  }
  async function direct(method, params, intent, expected) {
    if (!observation) throw Error('Observe before direct input');
    await call('begin_direct', {intent, expected});
    const prior = observation;
    observation = null;
    try {
      if (method === 'click' || method === 'scroll') params = {...params, screenshotId: prior.screenshots[0].id};
      await sky[method]({...params, window: prior.window});
    } catch (error) {
      await call('end_direct', {error: String(error)});
      throw error;
    }
    await call('end_direct');
    return capture();
  }
  return {
    capture,
    register: params => {
      if (!observation) throw Error('Capture the current scene first');
      return call('observe', {...params, captured_wall: capturedWall});
    },
    pressKey: (key, intent, expected) => direct('press_key', {key}, intent, expected),
    click: (x, y, intent, expected) => direct('click', {x, y}, intent, expected),
    scroll: (x, y, scrollY, intent, expected) => direct('scroll', {x, y, scrollX: 0, scrollY}, intent, expected),
    run: params => {
      observation = null;
      return call('run', params);
    },
    startDialogue: (intent = 'Advance dialogue', expected = 'Dialogue ends or needs attention') => {
      if (!observation) throw Error('Capture and register dialogue first');
      observation = null;
      return call('run', {steps: [{keys:['attack'], seconds:.08}, {keys:[], seconds:.65}],
        intent, expected, repeat_dialogue:true});
    },
    checkDialogue: (active, evidence) => {
      if (typeof active !== 'boolean') throw Error('active must be boolean');
      if (active && !observation) throw Error('Capture and inspect the current dialogue first');
      return call('check_dialogue', {active, evidence, captured_wall:active ? capturedWall : 0});
    },
    think: () => call('think'),
    status: () => call('status'),
    report: (after_sequence = 0) => call('report', {after_sequence}, 'full'),
    result: params => call('result', params),
    complete: params => call('complete', params),
    rollback: params => call('rollback', params),
    stop: () => call('stop'),
  };
}
