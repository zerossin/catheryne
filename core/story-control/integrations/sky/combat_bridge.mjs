// Import in the persistent Computer Use node_repl after selecting a real window.
// No input, capture or server starts on import. No alternate screenshot backend.
import fs from 'node:fs/promises';

export async function connect(connectionPath, sky, window) {
  const config = JSON.parse(await fs.readFile(connectionPath, 'utf8'));
  const url = new URL(config.url);
  if (url.hostname !== '127.0.0.1' || url.protocol !== 'http:') throw Error('Loopback endpoint required');
  let running = false, sequence = 0;
  let lastError = null, lastCapture = null, deadline = 0;
  async function call(method, params = {}) {
    const response = await fetch(config.url, {
      method: 'POST', headers: {'Content-Type': 'application/json', Authorization: `Bearer ${config.token}`},
      body: JSON.stringify({method, params}), signal: AbortSignal.timeout(5000),
    });
    const data = await response.json();
    if (!data.ok) throw Error(data.error);
    return data.result;
  }
  async function capture(context) {
    const started = Date.now();
    const state = await sky.get_window_state({window, include_screenshot: true, include_text: false});
    window = state.window;
    const shot = state.screenshots?.[0];
    if (!/^data:image\/(png|jpeg);base64,/.test(shot?.url ?? '')) throw Error('Expected official PNG/JPEG frame');
    lastCapture = await call('frame', {frame: ++sequence, context,
      capture_started_wall_ms: started, png: shot.url.split(',')[1]});
    return lastCapture;
  }
  sequence = (await call('status')).observation.frame ?? 0;
  return {
    call,
    captureOnce: capture,
    async runCapture({context, seconds = 2, intervalMs = 200}) {
      // MUST remain awaited within a live node_repl call. The official sky
      // client rejects timers after that call's execution context has ended.
      // The caller can yield the outer functions.exec while this tool runs.
      if (running) throw Error('Capture already started');
      if (!context || !Number.isFinite(seconds) || seconds < 1 || seconds > 5 ||
          !Number.isFinite(intervalMs) || intervalMs < 0) throw Error('Invalid capture lease');
      running = true; lastError = null; deadline = Date.now()+seconds*1000;
      try {
        // Each official capture is also attached to the tool response. Bound
        // diagnostic batches to avoid overflowing its IPC output frame.
        let frames = 0;
        while (running && Date.now() < deadline && frames++ < 5) {
          await capture(context);
          await new Promise(resolve => setTimeout(resolve, intervalMs));
        }
      } catch (error) { lastError = String(error); }
      finally { running = false; }
      return {running, sequence, lastCapture, lastError};
    },
    async stopCapture() {
      running = false;
      return {running, lastError};
    },
    captureStatus() { return {running, sequence, lastCapture, lastError}; },
  };
}
