# Project rules

- Keep the supervisor independent of game chapters, characters, accounts and model providers.
- Task plans are data; operator evidence is private runtime state. Do not add real game screenshots,
  credentials, walkthrough copies or personal journals to this repository.
- Use fake executors in tests. Never send native game input during automated validation.
- Preserve no-input startup defaults, exact target checks, cancellation and key release behavior.
- Run Python tests and the Node adapter tests before committing. Do not publish or push without
  explicit user authorization.
- A capture is evidence at one time, an input return is not game success, and input stop is not world pause.

## Upgrade evaluation

- Evaluate every upgrade for performance, speed, usefulness, and model token cost. Lightweight design includes model calls, screenshots, repeated context, and tool output, as well as runtime load.
- Establish a before/after comparison under equivalent conditions. Track elapsed time, calls, screenshots, and response bytes; measure input/output tokens when available. Mark unavailable token savings as unmeasured. Never present byte reduction or hidden polling reductions as measured model token savings.
- Report implementation, automated validation, runtime loading, and live verification separately. Label estimates explicitly.
- Prefer reusable execution and compact routine observations, expanding details for changes, failures, or decisions. Preserve fresh evidence, cancellation, and user control when optimizing cost.
