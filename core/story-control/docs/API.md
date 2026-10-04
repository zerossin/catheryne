# API

POST to the URL in the private connection file, with `Authorization: Bearer <token>` and JSON
`{"method":"status","params":{}}`. Responses are `{"ok":true,"result":...}` or an error.
Only loopback is bound. Input is disabled unless the host was explicitly configured to enable it.

| Method | Required parameters / purpose |
| --- | --- |
| capture_started / capture_finished | Bracket an official capture; optional error on finish |
| observe | stage, mode, evidence, captured_wall (Unix seconds); optional actor=agent/user, paused, urgent, pause_available |
| run | steps, intent, expected; optional plan_revision after repeated failures, repeat_dialogue=false |
| check_dialogue | active (boolean), evidence, captured_wall; optionally update observed evidence or stop the active dialogue stream |
| begin_direct / end_direct | Reserve/release one externally executed official UI action; intent/expected then optional error |
| think | Begin supervisor waiting; request a configured pause for urgent unpaused scenes |
| result | outcome=success/failure/unknown, evidence, optional actor |
| complete | stage, evidence, optional actor; only the observed stage |
| rollback | stages, evidence; explicitly mark lost progress without erasing history |
| status / report | optional after_sequence cursor; polling does not reset the supervisor timer |
| stop / shutdown | Cancel managed input / gracefully stop the host and remove connection credentials |
| set_control | target=user/agent; optional reason. User cancels input; agent requires released input and a fresh observation before resuming |
| configure | combat=auto/manual/on_failure, failure_limit=1..10, notifications=boolean; persists shared operator settings |

The default `stop` reason `user_stop` now latches user control. An observation does not clear it.
`control` is the operator's persistent authority; `owner` is the current input lease. They are different:
`handoff_pending=true` means the user requested control but input has not returned yet. Agents must
respect user control and must not call `set_control(target='agent')` without an explicit user request.
See [operator UI](OPERATOR-UI.md) for resume and combat-policy behavior.

Modes: idle, navigation, dialogue, interaction, puzzle, stealth, escape, combat, cutscene, paused, recovery.
Native keys: w, a, s, d, shift, space, e, q, f, 1..5, v, m, b, t, attack (left mouse), dodge (right mouse).
Each step uses keys, seconds (0.05..5), dx and dy (integers within +/-1000). Total sequence <=5 seconds.

```javascript
const {connectStory} = await import('/path/to/integrations/sky/bridge.mjs');
const story = await connectStory('/private/runtime/connection.json', sky, returnedWindow);
await story.capture(); // Inspect the returned official screenshot before deciding.
await story.register({stage: 'travel', mode: 'navigation', evidence: 'Observed open path',
  actor: 'agent', paused: false, pause_available: false});
```

Use `run`, `pressKey` or `click` only with an input-enabled host and evidence appropriate to the target.
Wait for owner=null, capture the result and record it before another action. Do not replay the example
as a game walkthrough. Unknown results are explicit; input return is not task completion.

Capture timing is separate from the gap before classification. Supervisor gaps include tool/network
time and are not pure model inference time. Completion counts refer to registered milestones, never
percent of game duration. Journals persist history; restart revokes input leases and pause assumptions.
Plans are fingerprinted. A changed plan requires a new state directory; no implicit progress migration.

### Vertical scrolling in the Sky adapter

Use story.scroll(x, y, scrollY, intent, expected) after capture and register. Positive scrollY scrolls down; negative scrollY scrolls up. It uses the same direct-input lease, screenshot identity, result requirement and post-action capture as click.


### Continuous dialogue and choices

After inspecting and registering a nonurgent `dialogue` scene, place the cursor at the
observed dialogue-choice location using the normal guarded click flow and record that
click's result. Then capture/register again and start:

```javascript
await story.startDialogue(); // Returns immediately; clicks continue at the current cursor.
await story.capture();       // Inspect this image while clicks keep running.
await story.checkDialogue(true, 'Dialogue or choices remain at the chosen click position');
// On a later capture, when dialogue ends or the screen is uncertain:
await story.checkDialogue(false, 'Dialogue ended; world HUD is visible');
// Wait for owner=null, capture, then record result before another action.
```

The same click advances text and selects choices beneath the cursor. Choices are not a
stop condition by themselves. The caller supplies the location from actual UI evidence;
there are no game-specific coordinates, choice semantics or hidden screen classifiers.
Do not blindly renew on a timer: the operator must inspect each new capture. A changed
layout or unexpected menu warrants a stop and a new decision. `story.stop()` cancels
without requiring a capture.

A stream repeats only a short left click (default 0.08s) and released interval (default
0.65s), through the existing sequence executor and exclusive input owner. The default
sequence limit remains 5s. Repetition is restricted to dialogue, never combat or movement.
A single action result is pending for the entire stream; clicking does not prove progress.

The stream stays active without model renewal calls. A positive check optionally records
newer observed evidence; it is not required to keep clicking. Explicit stop, Escape,
F12, focus loss or input failure stops the stream. A negative check requires no new
capture. Checking does not restart stopped input.

This removes click gaps during operator judgment; it does not remove the operator's
latency in detecting dialogue end. Validate the observed click location and stop delay
before relying on a new layout. Ordinary choices at that location remain part of the
same stream, not separate model-directed actions.
The running host must be restarted and the adapter re-imported to load an update; do so
only after the user finishes controlling the game. No runtime is replaced automatically.


### Small routine responses

RPC requests may include top-level `"view":"compact"` (default for raw RPC remains
`"full"`). The compact response is a projection of the same report, retaining stage,
mode, input owner, stop/pause/urgent flags, alert, pending result, capture time, cursor,
input permission, watchdog, completion counts and earlier unverified stages. It omits
repeated event history, step evidence and timing detail. The private journal is unchanged.

The Sky adapter requests compact responses for routine calls. Use `story.status()` for
owner checks; use `story.report(lastCursor)` only when detailed evidence is needed, saving
the returned cursor to avoid printing the same events again. Existing `report()` remains
full and backward compatible. Routine adapter responses no longer contain `steps`,
`events`, `next_steps` or detailed timing metrics; callers that need those use `report()`.

For repeated dialogue: start once, capture/inspect while it runs, then check/stop.
Do not print entire reports for every click, repeat source passages, or capture again
when the previous tool already returned the current image. Keep evidence short but
specific to the visible transition. Do not reduce observation frequency in combat or
hazardous movement merely to save tokens. The adapter does not resize images or claim
an automatic scene detector.

A synthetic 500-event sample measured 93,328 bytes for a full status versus 440 bytes
for compact status (99.5% smaller). This is serialized response size, not model token
usage, total conversation savings, or gameplay throughput. Hidden/unprinted responses
were not model context in the first place. Measure actual workload separately.
