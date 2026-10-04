# Branding assets

## Application icon and welcome mascot

The project owner created `launcher.png` and `welcome.png` (authorship confirmed 2026-10-04). The welcome drawing was edited with built-in ImageGen on 2026-09-27 to preserve pose, proportions and colors while removing the white background and motion marks. The original supplied asset was not overwritten.

## Background

`background.png` is an AI-edited version of official Columbina artwork from Genshin Impact's **A Traveler on a Winter's Night** wallpaper series, published by the official Genshin Impact account on 2026-02-05 and selected by the project owner on 2026-10-04.

- Official post: https://www.hoyolab.com/article/43631587
- Official download link listed in that post: https://hoyo.link/4ylu2AI01
- Original 1920×1080 JPEG: https://upload-os-bbs.hoyolab.com/upload/2026/02/05/7ce216d20ae797ac3ac7a30fb3b2b783_2978883075573055948.jpg
- Original JPEG SHA-256: `c4e5a46cd8a223bd0332a98dfa790f99d93092ed2d026dfbd62735a9861f2b52`
- Original, unedited PNG SHA-256: `06dcb89e01a702d101f70768121b0a5b24348b5cb0ee198cb13b8b99a359bcd1`
- Bundled edited PNG: 1672×941, RGB; SHA-256: `71efe3fe968afdd94064987b35dc073261d56f44800666accd461c1425e3ec97`

The original JPEG was first converted losslessly to PNG. On 2026-10-04, the project owner requested removal of the upper-right promotional title (원신 공월의 노래). Built-in ImageGen produced the current opaque PNG, preserving the bottom-left ©COGNOSPHERE notice. AI editing also changed some other fine details and returned 1672×941 rather than the requested 1920×1080; this is not a pixel-identical copy of the original. The unedited version is backed up privately outside the repository. The launcher retains its existing centered UniformToFill layout and UI shading.

### Edit record

- Mode: built-in ImageGen, referenced-image edit; opaque background.
- Saved project asset: `apps/desktop/branding/background.png`.
- Prompt: Remove only the upper-right white Korean promotional title “원신 공월의 노래”, its moon emblem and associated tiny English Genshin Impact lettering. Reconstruct the surrounding blue water and flower reflections. Preserve the 16:9 framing, requested 1920×1080 size, character, composition, lighting, colors and sharpness with maximum fidelity; do not crop, resize, restyle, recolor or add objects/text. Absolutely preserve the bottom-left ©COGNOSPHERE notice, unchanged and readable. The only intended edit is local promotional-title removal; opaque wallpaper, no transparency.
- Verification: promotional title removed; bottom-left copyright remains readable. Actual output dimensions and hash are recorded above.

© All rights reserved by COGNOSPHERE. Other properties belong to their respective owners.

The wallpaper is excluded from Catheryne's MIT code license. Catheryne is an unofficial companion and is not affiliated with or endorsed by HoYoverse. The official post supplies the wallpaper for download; the [Genshin Impact legal FAQ](https://www.hoyolab.com/article/143107) discusses noncommercial personal fan use, but does not explicitly license bundling the original image in an open-source app. Public availability and the project owner's selection are not recorded as a separate redistribution license.

The former Reo (@hiimreoart) background and the fallback JPEG were removed from the current source and future installer payload. Personal backups remain outside the repository. Earlier local Git history still contains the former artwork; a public source snapshot must exclude that history.
