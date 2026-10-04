# Localization

UI text is resolved by `Locale` from the selected language catalog. Korean source strings are the canonical keys; translated catalogs use locale filenames such as `en-US.json`. `Locale.T` resolves app labels, `Locale.Format` formats translated sentences with placeholders, and `Locale.Options` translates presentation-only option labels without changing their stable values or indexes.

XAML and dynamically generated UI must use the same catalog. Do not translate a rendered UI tree or arbitrary stored text: chat messages, user goal titles, imported names and other user data must remain unchanged. Translate static app labels where they are constructed. Dates use the selected locale's culture. External content needs its provider's localized data, not a replacement of the user's stored record.

The language selector and AI language-setting action share `AvailableLanguages`; packaging includes the same named catalog files. A new catalog must have a valid .NET culture code. Add the catalog, verify provider-language mappings and localized game catalog coverage, run the localization checker and inspect actual UI layouts. Adding UI translation alone does not supply translated external content.

Run `python scripts/check-localization.py`. CI checks declared fixed UI keys and formatting placeholders; this is not a claim that every runtime/server-generated message has been exhaustively inspected. Keep translated content out of persisted domain identifiers.

Typography: Korean uses NanumGothic Bold for normal UI, preserving same-family Latin/digits/punctuation. Default body is 14 DIP; secondary text can be smaller. Game SDK faces retain their own metrics. WPF Ideal formatting with grayscale antialiasing replaces pixel-snapped Display formatting. Do not claim exact in-game rendering equivalence.
