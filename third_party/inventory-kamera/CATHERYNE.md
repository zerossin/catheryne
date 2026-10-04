Upstream: https://github.com/taiwenlee/Inventory_Kamera
Local changes: headless entry point, progress callback, cooperative cancellation and foreground input checks. The WinForms UI is never created in headless mode. Build using MSBuild /restore, Release x64.

Pinned upstream commit: 427b868362e1b01926c0e10d27f67c331307e96f

2026-09-27: Korean alias-only OCR fallback (exact normalized names), custom character identity/level observations in Catheryne extension. Unsupported GOOD keys are not fabricated. Korean model from https://github.com/tesseract-ocr/tessdata_fast, Apache-2.0; SHA256 6b85e11d9bbf07863b97b3523b1b112844c43e713df8b66418a081fd1060b3b2. Removed upstream recursive catalog-file string patch for mannequin placeholders.
