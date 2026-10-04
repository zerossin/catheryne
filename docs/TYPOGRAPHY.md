# Language typography

Verified 2026-09-28. Font routing is centralized in `Typography.cs`. All ordinary desktop text inherits the composite family. Icon glyphs and code retain their dedicated fonts.

| Language/script | Selected face | Evidence and limit |
| --- | --- | --- |
| Korean (including Latin, digits and punctuation) | NanumGothic Bold base with existing UI emphasis | Redistributable upstream NAVER font. Community identification of the game's Korean basis is not proof that every modified game glyph is identical. |
| English/Latin | Locally installed SDK_SC_Web 85W | Uses the Latin glyphs actually present in the game SDK font, not an assumed stock Latin face. |
| Japanese | Locally installed SDK_JP_Web 85W | Kana and Japanese-language Han text use the Japanese file. |
| Simplified Chinese | Locally installed SDK_SC_Web 85W | Simplified Han font. |
| Traditional Chinese | Locally installed SDK_SC_Web 85W | File contains traditional glyphs too; no separate traditional file was found. This does not establish a separate officially named traditional typeface. |

The game SDK files are not redistributed or copied. Resolve them from the configured game executable's sibling data directory. Missing files fall back to Windows language fonts. WPF family metadata is read from the actual file: its display family includes `85W`, unlike its Win32 family name. The game SDK font is not asserted to be an exhaustive dump of all in-game UI fonts.

WPF language tags select Japanese and traditional Chinese Han forms. Hangul and kana also select their respective faces in mixed text. The current UI translations remain Korean and English; this change does not invent Japanese/Chinese translations or add untranslated language options.

The upstream, unmodified NanumGothic Regular, Bold and ExtraBold files are bundled (about 6 MB), with its OFL license in `apps/desktop/fonts/OFL.txt`. No Windows-wide installation is performed.

Sources:
- HoYoverse creator guide names HYWenHei-85W: https://webstatic.hoyoverse.com/upload/static-resource/2022/11/21/da810850fb688769c0d1c492893a01ce_4676816367714686132.pdf
- Hanyi WenHei foundry page: https://www.hanyi.com.cn/productdetail?id=986
- NAVER distribution terms: https://hangeul.naver.com/font
- Upstream font and OFL: https://github.com/google/fonts/tree/main/ofl/nanumgothic
- Secondary identification of modified game glyphs: https://genshin-impact.fandom.com/wiki/Typeface

Validation: five-language WPF rendering, Japanese/Chinese file metadata and cmap inspection, desktop build and existing self-tests. Missing-game fallback is supported without network downloads.

Correction: forcing ExtraBold for Hangul and routing Korean Latin/digits to the game SDK face produced inconsistent, overly heavy text. Korean Latin and punctuation now use the same family as Hangul. Korean UI now uses Bold as its base weight, with 14 DIP default body text. WPF uses Ideal formatting and grayscale antialiasing to avoid excessive pixel snapping. See LOCALIZATION.md for the current shared policy.


