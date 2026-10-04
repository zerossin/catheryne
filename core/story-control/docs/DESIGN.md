# Design boundaries

Plan data -> operator observation -> one director -> bounded executor -> observed result -> journal.

The director uses a separate 50ms watchdog, one input owner and one canonical progress state.
The journal holds full state snapshots at meaningful events, not images or every timer tick.
Reports are derived views, with cursor loss indicated. Invalid/corrupt history fails rather than
silently resetting progress. Process shutdown is supported; crash/power-loss durability is not guaranteed.

Urgent modes default to a two-second attention budget. A configured B pause request is sent once,
never repeatedly toggled, and remains unconfirmed until the operator supplies fresh evidence.
These are initial operating thresholds, not empirically optimal game parameters.

Native Windows input is optional and lazily imported. It verifies the exact configured executable,
checks focus and F12, refuses already-held keys, and releases keys after cancellation or errors.
The core owns managed calls only; it cannot cancel an in-flight external computer-use API or prevent
another process/user from sending input. Never run competing input tools.

The generic engine ships only synthetic task examples. Real walkthroughs and task evidence belong
in separately maintained task data. No chapter, character, installation path or personal account is
part of the engine. The Sky adapter is injected, not a bundled proprietary dependency.
