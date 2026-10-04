# Contributing

Keep the core independent of model vendors, chapters, accounts and local installation paths.
Use synthetic plans/fake executors in automated tests. Add regression tests for lifecycle, cancellation,
timing and ownership changes. Do not send game input in CI. Keep runtime state out of commits.

Run the Python and Node tests in README. Describe what was actually tested; distinguish fake input,
official capture, native input and full task outcomes. Do not add gameplay claims based on unit tests.
