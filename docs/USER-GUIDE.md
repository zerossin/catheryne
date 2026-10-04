# Catheryne user guide

**English** · [한국어](USER-GUIDE.ko.md)

## Install and start

Requires Windows 10/11 x64 and an existing Genshin Impact installation.

1. Run `Catheryne-Setup` and open Catheryne.
2. Confirm the game location and prepare the components you want. FPS unlocking is optional.
3. Use the app. No account connection is needed for launching the game, screenshots, local data, or file import and export.

The app starts in Korean. Choose English in **Settings → General settings → Language**, then reopen it. You can prepare additional components later from **Settings → Installation and components**.

## Connect accounts

Open the account menu at the top and choose the service you need:

- **ChatGPT:** built-in AI chat. A compatible Codex desktop installation is also required; it is not bundled. Follow the [official setup guide](https://learn.chatgpt.com/docs/quickstart).
- **HoYoLAB:** account syncing, check-ins, resin, and the Traveler's Diary.
- **Redemption account:** redeeming codes; this connection is separate from HoYoLAB.

Complete sign-in in the login window and select your Genshin account when prompted. Automatic check-ins and code redemption start only after you enable them. They do not run while the PC is shut down or asleep.

## Refresh your data

Open **My account → Refresh** to update your records or import a GOOD/account or achievement file. Use HoYoLAB for characters and equipped items. Scan the game for unequipped inventory and individual achievements.

For scanning, follow the preparation screen: the scanners require English game text; inventory scanning also requires a supported screen ratio and HDR turned off. Keep the game visible and avoid manual input during a scan. You can stop and resume collection from the same screen.

A failed refresh does not replace your existing records. HoYoLAB achievement totals do not identify individual completed achievements.

## Review characters and equipment

Select a character or item in **My account** to see its details. Weapons show stats and refinement effects where the required data is available; artifacts show main and substats and their evaluation. Main stats also appear on artifact cards.

Use the artifact list's **Cleanup review** filter to find candidates, items worth upgrading, and protected items. Select a piece for its reasons and alternatives, or choose **AI consultation** for a closer review. Equipped, locked, and saved-build items are protected. Check the current in-game state before consuming anything; Catheryne does not dispose of artifacts automatically.

Open **Build plan** or **Compare build** from a character's details to check materials and compare builds against role-specific criteria.

## Use AI

Connect ChatGPT, enter a question or task in chat, and send it. Try “What should I improve on this character?” or “Look at my current game screen.”

Use the attachment menu to add files or capture the game. You can also paste a copied image or file into the message box with **Ctrl+V**. Supported files are PNG, JPG, JPEG, WebP, TXT, MD, JSON, and CSV; text files must be 500 KB or smaller. Attachments are sent only when you send the message.

Drag over chat text to select and copy it. Stop an AI task from its task card to return to manual play. For the latest interrupted request, choose **Edit and resend**, revise it, and send it again. The original conversation stays in your history; the revised request starts a new conversation with the preceding messages.

For execution requests, AI keeps the requested goal and verifies its outcome. Repeated failures call for a revised approach; lack of progress calls for help. Questions appear in chat, with required answers accessible from **Response needed** in the header. Stopped work does not resume automatically.

## Prepare for challenges

Open **Play → Spiral Abyss** or **Stygian Onslaught** to get team recommendations based on your roster. Save a team build, ask for AI advice, and record results to compare attempts.

Opening the screen displays saved recommendations. Choose **Preparation → Refresh recommendations** to recalculate from your current account and season data. When no recommendations exist, preparation opens with **Recommend teams**. **Goal note** is for saved builds and AI advice; it does not filter by floor, stars, or time. Abyss recommendations cover the two halves of floor 12. Stygian difficulty allows reuse at 1–3 and restricts repeated characters, weapons, and artifacts at 4–6; it does not predict whether you can clear that difficulty.

In **Play → Imaginarium Theater**, choose **Start in chat** to begin or resume a challenge. Share selection and result screenshots for advice on the next choices and teams. You perform the battles and in-game selections yourself.

## Display settings

Use **Display and performance** to configure FPS, display modes, and supported Windows HDR/Auto HDR. **AI preset** saves your current settings and switches to 1920×1080/60 FPS with HDR off. **My preset** restores the saved preferences. Windows HDR changes immediately; game launch settings apply on the next launch.

## Manage mods

Open **Display and performance → Mods** to import a ZIP, 7z, RAR, or folder, then enable the mods you want. Imported and built-in mods start disabled; required components are prepared when needed.

Use search and categories to find items, and open an item for its settings and source page. Turning a mod off keeps its files. Change mods while the game is closed; compatibility depends on the mod and game version.

For editable shortcuts, click the key in the item's details and press a new combination; use ↺ to restore the original. Possible overlaps with enabled mods are shown, and changes apply on the next game launch.

## Take screenshots

Open **Display and performance → Capture** or press **Ctrl+Shift+F12** while the configured game is in the foreground.

Shots are saved in **Pictures/Catheryne** by default. Change the hotkey or folder in the capture screen. HDR shots are converted to ordinary PNGs for sharing. Recent shots can be attached to chat, copied, or opened.

You can also choose **Capture** from the chat attachment menu. Restore a minimized game window first.

## Notifications

Screenshots, enabled resin/check-in reminders, and AI requests for help use Windows notifications. Turn on story alerts in the story settings if you want to be notified when control needs attention. Required questions can be reopened from the notification center, and their notifications are removed when answered. Urgent alerts follow Windows permissions and Do not disturb settings. Clicking a notification opens the relevant screen and does not resume gameplay.

Open **Settings → General settings → Windows notification settings** to manage notifications. Windows can block alerts when notifications are disabled.

## Update or uninstall

Check **Settings → General settings → App updates** for a new version. Automatic app updates are optional and install at a later start when no task is running. Updates become available when a public release is provided.

Uninstall through Windows **Installed apps**. Personal settings and records are kept for reinstalling; Genshin Impact and separately installed tools are not removed.
