# Story Control

A lightweight supervisor for an agent or human playing story-driven tasks through computer-use tools.
The operator interprets the screen and supplies the plan. Story Control owns task state,
bounded action leases, independent timers, evidence-based progress, and an append-only journal.

It is not an autonomous game-playing model or an enemy detector. No walkthrough,
game asset, account data, private screenshot, API key, or proprietary computer-use runtime is bundled.

## Requirements

- Python 3.11+. No third-party runtime dependencies for the supervisor.
- Windows for optional native input. The core, RPC, fake-input tests and plan tools are portable.
- Node 22+ only for the optional injected Sky adapter and its tests.

## Install and try without game input

```sh
python -m venv .venv
# Activate the virtual environment using your shell's standard command.
python -m pip install .
story-control check-plan examples/demo.json
story-control serve --plan examples/demo.json --state-dir .runtime/demo
```

In another terminal:

```sh
story-control call status --state-dir .runtime/demo
story-control call shutdown --state-dir .runtime/demo
```

The default host cannot send game input. Starting it does not activate an app or capture a screen.
State files and connection credentials are private runtime data; keep them out of version control.

## Prepare your own task

```sh
story-control new-plan --id my-task-v1 --title "My task" --output my-task.json
```

Fill the ordered steps, sources, allowed modes/tools, completion evidence, checkpoint behavior and
failure costs. Set `draft` to `false`, validate with `check-plan`, then start the same host with your plan.
Use a separate state directory per task/revision. To switch plans, shut down the old host first.
The engine has no chapter-specific logic and does not infer completion from a walkthrough.

## Optional native input

On Windows, explicitly select the installed target executable:

```sh
story-control serve --plan my-task.json --state-dir .runtime/my-task --enable-input --target-exe "C:/Games/Example/Game.exe"
```

The configured executable must be foreground. There is no automatic activation, discovery guess,
privilege escalation, game-memory access, or input on startup. Escape, F12, focus loss and explicit stop cancel
active native actions. Key releases run in a finally block. A sequence lasts at most five seconds.
Dialogue can explicitly repeat a click/release cycle while the operator inspects the screen,
until explicitly switched off, without model renewal calls. Focus loss and user stop still release input.
Ordinary story choices at the observed click location continue automatically; the operator switches modes at dialogue end or an unexpected layout; screenshots do not pause the click cycle.
The fixed native key vocabulary is documented in [the API](docs/API.md).

The optional pause action is B. Enable `pause_available` only after verifying B pauses the current
context. A successful key send never confirms a paused world. No universal pause guarantee is made.

## Operator dashboard

With a host running, open its local progress and control screen:

```sh
story-control ui --state-dir .runtime/my-task
```

The dashboard shows registered-step progress, elapsed time, last observed scene, and activity.
It shares stop/handoff controls and persisted combat/notification settings with the same supervisor.
No extra model calls, captures, web framework, or runtime dependencies are needed.
It does not start or wake an AI session. Browser notifications require permission and an open tab.
See [operator guide (Korean)](docs/OPERATOR-UI.md) and [validation](docs/UI-VALIDATION.md).

## Computer-use integration

`integrations/sky/bridge.mjs` takes a caller-provided `sky` object and a window returned by its official API.
The proprietary runtime is not bundled or required by the core. Other clients can use the local JSON API
or inject an executor implementing `act` and `stop` into `StoryDirector`.

See [API and integration](docs/API.md), [design](docs/DESIGN.md), and [Korean overview](docs/README.ko.md).

## Development

```sh
python -m pip install -e .
python -m unittest discover -s tests
node --test integrations/sky/bridge.test.mjs
```

Tests use synthetic plans and fake input only. CI is configured for Windows and Linux; a configured
matrix is not evidence that its remote runs have passed. Native end-to-end input needs separate manual
validation in the intended app. No speedup, survival rate, or complete story-play success is claimed.

## Privacy and scope

Reports contain operator-written evidence and action logs. Do not publish runtime directories or use
sensitive text as evidence in public fixtures. The loopback token protects requests, not a hostile
process running under your own account. State-directory locks reject duplicate journals; an additional
per-user lock rejects competing input-enabled hosts even with different task directories. These locks
do not stop unrelated tools or physical user input.

MIT licensed. This project is not affiliated with a game publisher or computer-use provider.
