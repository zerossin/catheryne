# Catheryne working agreements

- This repository is the canonical development source. Do not develop duplicate copies in the vault or installed launcher directory.
- Preserve module-specific AGENTS.md and licenses. Keep personal account data, credentials, screenshots and runtime journals outside this repository.
- Verified local changes should also be applied to the installed app (user instruction, 2026-09-29). A running game alone is not an update blocker: preserve the game and unlocker session, verify there is no active Catheryne control/scan task, then back up and replace only the changed app files and restart the GUI. Keep files used by active control/scan workers frozen; do not stop the game or those workers without authorization. Verify installed hashes and GUI response after replacement.
- Automated tests use fake input only. Input transmission is not verified completion.
- Keep one canonical execution path; gameplay implementations live in core/story-control, and clients must reuse them rather than restore labs/gameplay or create a second input manager.
- No remote push, publication or deployment without explicit authorization. Local installed-app updates follow the standing user instruction above.

- Public-source boundary is mandatory: no personal settings, records, account exports, captures or credentials. Runtime defaults must be outside this checkout. Run scripts/check-public.py before commits and release preparation. Gitignore does not remove already tracked data; inspect staged files.

- UI changes must follow docs/UX-PRINCIPLES.ko.md; verify the shared spacing, action hierarchy, terminology, and result-first navigation before completion.
- Every tab/UI change must also review entry latency: move I/O and expensive calculation off the UI thread, create folded detail on demand, reuse bounded image/data caches, and verify narrow/wide rendering and meaningful interaction without changing established behavior.
