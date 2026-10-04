# Third-party components and assets

- FPS engine: [34736384/genshin-fps-unlock](https://github.com/34736384/genshin-fps-unlock), MIT, Copyright (c) 2021-Present 34736384. The engine is not included in the installer. First-run setup downloads the official release, or the user selects an existing executable. Source and license: https://github.com/34736384/genshin-fps-unlock/blob/netcore/LICENSE . No engine source is incorporated in this launcher.
- Account and achievement scanner: [AkashaScanner](https://github.com/akrios-d/AkashaScanner), MIT. Catheryne's maintained scanner is built from the vendored MIT source in `third_party/akasha-scanner` and bundled as a template with its license; it is copied to private application data before use. When the template is unavailable, the pinned release in components.json is downloaded. Explicit approval may select another official release with a published SHA-256. See the maintained scanner notes and its upstream LICENSE.txt.
- Windows WPF / .NET Framework and the engine's .NET 8 Desktop Runtime are platform dependencies; they are not redistributed.
- `branding/launcher.png` is the project-owner-provided Catheryne application icon (2026-09-26). `launcher.ico` is its rounded, multi-resolution Windows build. The code MIT license does not grant rights to third-party character artwork.
- `branding/welcome.png` is a project-owner-created character drawing (authorship confirmed 2026-10-04), edited with ImageGen on 2026-09-27. The current `branding/background.png` is an AI-edited version of official HoYoverse / COGNOSPHERE Columbina artwork from the publicly released "A Traveler on a Winter's Night" wallpaper series (2026-02-05), edited with built-in ImageGen on 2026-10-04 to remove the upper-right promotional title while retaining the bottom-left copyright notice. The edited PNG is 1672×941 and other fine details differ from the original 1920×1080 JPEG. Source, hashes and copyright are recorded in `branding/README.md`. Previous third-party backgrounds are excluded from the current source and future installer payload. The code MIT license does not grant artwork rights; see `branding/README.md`. Legacy personal backgrounds remain outside the repository.
- Genshin Impact and HoYoPlay are third-party product names. This companion is unofficial and does not distribute game software.
- The personal installation optionally uses `color-icon.ico` and `assets/` from game/XXMI installations. They are excluded from public source and binary packages. The MIT license does not cover those optional assets. Packaging does not treat the XXMI software license as permission to redistribute game artwork.

- Microsoft WebView2 SDK 1.0.4191.47: redistributed Core/WPF assemblies and x64 loader under the Microsoft package terms. See WebView2-LICENSE.txt and WebView2-NOTICE.txt in the executable package. Edge WebView2 Runtime must already be installed separately.
- HoYoLAB request formats are based on the public genshin.py documentation and protocol reference (https://github.com/seriaati/genshin.py). This is an unofficial integration; upstream changes and additional verification may interrupt it. No third-party account credentials are bundled.

- Markdig 0.37.0 (BSD-2-Clause), standard Markdown parsing. System.Memory 4.5.5, System.Buffers 4.5.1, System.Runtime.CompilerServices.Unsafe 4.5.3 and System.Numerics.Vectors 4.5.0 (MIT) provide its .NET Framework runtime support. Versions and SHA-256 hashes are pinned in restore-markdown.ps1. Full notices are in Markdown-NOTICES.txt.

Inventory Kamera: https://github.com/taiwenlee/Inventory_Kamera — MIT. Catheryne adds a headless adapter; original license is included in integrations/kamera/LICENSE.txt.

## Optional external applications

BetterGI 0.66.0 is downloaded separately from https://github.com/babalae/better-genshin-impact (GPL-3.0). Snap Hutao Remastered 1.20.3 is downloaded separately from https://github.com/SnapHutaoRemasteringProject/Snap.Hutao.Remastered (MIT). Their binaries, settings and account data are not bundled in this source package. Pinned release hashes are in ExternalTools.cs. The optional archive runtime is upstream 7-Zip from BetterGI commit 0af68c2; 7-Zip license information: https://www.7-zip.org/license.txt.

## Display catalog and bundled runtime

GameCatalog imports game text verbatim from pinned revisions of [Genshin Optimizer](https://github.com/frzyc/genshin-optimizer) and [genshin-db](https://github.com/theBowja/genshin-db). Revisions are recorded in catalog/game.json; upstream licenses are included in catalog/. These are community-maintained extracts of game text, not official HoYoverse APIs. Public game portraits are downloaded only as needed from the pinned Optimizer asset tree and verified against its Git blob IDs. Game artwork rights remain with their owners.

Python 3.13.15 embeddable Windows runtime is redistributed under its included LICENSE.txt. MCP Python SDK and binary Python dependencies retain their licenses and package metadata under integrations/runtime/Lib/site-packages. Runtime packaging uses the official Python archive pinned in build-runtime.ps1.

## Build reference cache and artifact scores

The user-selected public Google workbook (ID `1sjVkeR8s41wW0oTtBHC1at9riOxWqyXPcbJscYI8fdE`) is fetched at runtime. Its two selected tabs retain author-maintained values, conditions and source row links. The workbook itself and its images are not redistributed. A disposable text/structure cache lives in the user's local app data and is refreshed on use after 24 hours. Malformed changes do not replace the last valid cache.

`catalog/artifact-scores.json` is a compact derivation of Genshin Optimizer's `artifact_sub_rolls.json` and rounding corrections at the revision recorded in that file. `update-game-catalog.py` rebuilds it from those upstream files; the Optimizer license is included in `catalog/LICENSE-GenshinOptimizer.txt`. KQM's artifact guide (https://keqingmains.com/misc/artifacts/) documents the linked CV/RV interpretation. No KQM guide text is bundled.

## Vendored scanner imagery

The vendored scanner sources include `AkashaScanner/Logo.ico`, `AkashaScanner/Resources/lock.png`, and Inventory Kamera's `InventoryKamera/Item_Special_Kamera.ico`. Their software licenses do not establish ownership of the depicted game imagery. Artwork rights remain with their respective owners.

## Resin icon
branding/resin.png is the Original Resin game icon (HoYoverse), obtained from the embedded 60px asset in https://gist.github.com/spencerwooo/2bf048c419cf5083be57ba1283f473ed (retrieved 2026-09-28). Game artwork remains the property of its respective owner; no script code from that gist is bundled.

## NanumGothic
Unmodified NAVER/NHN NanumGothic Regular, Bold and ExtraBold, SIL Open Font License 1.1. See fonts/OFL.txt. Upstream: https://github.com/google/fonts/tree/main/ofl/nanumgothic. Game SDK fonts are read from the user's existing game installation and are not distributed.



## Automatic artifact criteria

Numeric default stat weights, character identity metadata, statically inspected calculation sources, weapon substats and artifact set descriptions are imported from [miao-plugin](https://github.com/yoimiya-kokomi/miao-plugin), MIT, copyright (c) 2023 Yoimiya. The full license is bundled in `catalog/build-criteria/LICENSE.txt`. The bundled revision is recorded in `catalog/build-criteria/revision.txt`. Runtime refresh checks the license, validates the numeric grammar and coverage, and preserves the last validated cache. These inputs support estimated attribute suitability and talent dependencies; the upstream conditional scoring engine is not executed. GOOD ID mappings are supplied by Genshin Optimizer under its existing MIT attribution.

Automatic build identity refresh uses the same validated public game catalog bundle as display names and HoYoLAB conversion. Runtime game data is read from pinned [genshin-db-dist](https://github.com/theBowja/genshin-db-dist) gzip extracts (MIT) and Genshin Optimizer; only data is decoded. Each blob is verified against its Git object ID and the bundle preserves the two upstream revisions. Source revisions are retained in the local build-criteria cache; bundled bootstrap is in catalog/build-criteria/names.json. No account data is transmitted.

## Built-in ChatGPT

The built-in ChatGPT connection uses a separately installed official Codex app-server over local stdio. Codex is not bundled; its software and service terms remain applicable.

## HDR capture references

Catheryne.Capture.dll is original Catheryne code built with the Windows SDK. Microsoft Windows Graphics Capture documentation, HDR Corrector, hdrcapture, and HDR Screenshot were consulted; their code and applications are not bundled. See [capture notes](../../docs/CAPTURE.ko.md).

## Element icons

`catalog/elements.json` preserves the seven Genshin element SVG paths from Genshin Optimizer at revision `180a0a1015cb725c7570cd828eb086749960df98`, `libs/gi/svgicons/src/icons/Element/*Icon.tsx`. Paths are rendered as WPF vector geometry; no React code is executed. The existing bundled Optimizer MIT license applies; Genshin imagery remains HoYoverse property.


## Theater reference cache

KQM theorycrafting pages and the current public character-guide index are fetched at runtime with source links, timestamps and available version labels. Genshin-Builds public Theater example teams are parsed as bounded data; no GPL repository code or page script is copied or executed. Sample cohort metadata is unconfirmed and examples are never ranking weights. Reference prose, account snapshots, journals and screenshots are stored only in the user's private app data; none are bundled in the public repository. See [Theater reference investigation](../../docs/THEATER.ko.md).

### Lightkeepers team statistics

The endgame preparation screens request the public https://lightkeepers.moe/api/static data feed (YShelper statistics via Lightkeepers). The response is cached only in the private application data folder; account rosters are never sent to this service. No Lightkeepers source code or assets are bundled. Usage rates are not success probabilities; cohort metadata may be unavailable.

### WebP game images

Catheryne.Images.dll links Google's libwebp 1.6.0 decoder (BSD license, WebP-LICENSE.txt). build-images.ps1 verifies the SHA-256 of the official Windows package before compiling the small native wrapper. Enemy icons are fetched at runtime from the same public Lightkeepers asset service and cached privately; no enemy imagery is bundled.
