<p align="center">
  <img src="apps/desktop/branding/launcher.png" alt="Catheryne" width="128" height="128">
</p>

<h1 align="center">Catheryne</h1>

<p align="center">
  <strong>English</strong> · <a href="README.ko.md">한국어</a>
</p>

<p align="center">
  <strong>Genshin Impact, with ChatGPT by your side</strong><br>
  A Windows companion for your account, builds, daily routine, and gameplay.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-v0.2.0-438E7B" alt="Version 0.2.0">
  <img src="https://img.shields.io/badge/platform-Windows_10%2F11-0078D4" alt="Windows 10/11">
  <img src="https://img.shields.io/badge/status-preview-D5A34A" alt="Preview">
  <img src="https://img.shields.io/badge/language-한국어_%2F_English-64748B" alt="Korean and English support">
</p>

<p align="center">
  <a href="#getting-started">Getting started</a> ·
  <a href="#features">Features</a> ·
  <a href="#user-guide">User guide</a>
</p>

---

## Features

- **AI companion** — Chat with ChatGPT to review your account, get character-building advice, and request tasks. Connect external AI clients through MCP.
- **Gameplay assistance** — Let AI observe the game and perform actions such as dialogue and movement. Follow its progress, stop a task, or take control yourself.
- **Your account** — Browse and search characters, weapons, artifacts, materials, and achievements. Supports HoYoLAB syncing, in-game scanning, and GOOD file import and export.
- **Character development** — Compare character builds against role-specific criteria and check the materials you need to reach your goals. Review artifact cleanup candidates alongside upgrade potential and replacement pieces.
- **Challenge preparation** — Find teams for Spiral Abyss and Stygian Onslaught, save builds, and review results. Get Imaginarium Theater advice as you share selection and result screens.
- **Daily routine** — Automate HoYoLAB check-ins and new redemption codes, monitor resin, and manage schedules with Windows notifications.
- **Primogem ledger** — Review estimated primogems for each version alongside actual income from the Traveler's Diary, and record rewards you have claimed.
- **Screenshots** — Capture the game with a hotkey, tone-map HDR to shareable PNGs, and attach saved shots to chat.
- **Display, performance, and mods** — Configure FPS unlocking, resolution, display modes, Windows HDR/Auto HDR, and presets. Import your own mods and toggle them on or off.

Catheryne is under development. AI gameplay support varies by task and environment; automatic completion of every quest and equipment optimization are not currently supported.

## Getting started

Requires Windows 10/11 x64 and an existing Genshin Impact installation.

1. Run the `Catheryne-Setup` installer and open Catheryne.
2. Confirm the game location and prepare the components you want. FPS unlocking is optional.
3. Start using the app. Connect ChatGPT for AI, HoYoLAB for syncing and check-ins, or a redemption account when you need those features.

No account connection is required for game launching, screenshots, local data browsing, or file import and export. Display features and scanners need their respective components. The app starts in Korean; choose English in Settings → General settings → Language, then reopen it.

Built-in AI requires a compatible local Codex desktop installation and ChatGPT sign-in. Catheryne does not bundle Codex. See the [official setup guide](https://learn.chatgpt.com/docs/quickstart) and [AI user guide](docs/USER-GUIDE.md#use-ai).

Automatic check-ins and code redemption must be enabled manually. They do not run while your PC is shut down or asleep.

## User guide

[Using Catheryne](docs/USER-GUIDE.md) · [한국어 사용 안내](docs/USER-GUIDE.ko.md)

Account data and settings are stored on your PC. When you use AI, data needed for your request is sent to your chosen AI provider. Login credentials are excluded from AI tool responses.

## License

Catheryne is an unofficial project and is not affiliated with HoYoverse or OpenAI. Original code is [MIT-licensed](LICENSE). External tools, data, and artwork retain their respective rights; see [license scope](NOTICE.md) and [third-party notices](apps/desktop/THIRD-PARTY.md).
