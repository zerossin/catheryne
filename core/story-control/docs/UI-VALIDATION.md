# Operator UI validation — 2026-09-26

Isolated branch based on c3edf08. The active checkout and game input were not modified.

## Automated and browser checks

- 45 Python tests and 6 Node adapter tests passed with synthetic data/fake input.
- Added tests cover persisted user control, fresh observation on resume, external input still in flight,
  managed worker cancellation, manual combat policy, failure-triggered handoff, settings validation, no protective input after handoff,
  public static assets and authenticated RPC.
- A separate no-input host loaded the UI in a real browser. User takeover, return to agent with fresh
  observation required, and manual-combat settings save were verified on screen.
- Desktop layout was visually inspected. OS notification delivery and live game behavior were not tested.

## Equivalent microbenchmark

One synthetic three-step plan, fixed clocks, after_sequence at the latest cursor, 1,000 in-process
status calls per view, including JSON serialization. No game screenshots or model calls in either run.
These are local microbenchmarks, not network timings, gameplay speedup, or resource guarantees.

| View | Before median / p95 | After median / p95 | Response bytes before / after |
| --- | --- | --- | --- |
| compact | 0.0201 / 0.0326 ms | 0.0226 / 0.0377 ms | 536 / 657 |
| full | 0.0238 / 0.0416 ms | 0.0270 / 0.0453 ms | 2117 / 2443 |

The compact view adds control/settings needed by the agent. The full view adds display metadata.
The browser polls once one second after each completed request, using an event cursor. This traffic
is local and does not go through a model. Actual model input/output token cost is unmeasured.
The UI does not add game captures or inference requests. Browser rendering CPU and long-duration
background throttling were not measured. No claim of cheaper model tokens is made.

## Package check

The wheel was built and installed into an isolated target directory without dependencies. Both
HTML and JavaScript assets were present in the installed package, and CLI import did not load the
native-input backend. The active installation was not upgraded.
