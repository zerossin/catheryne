# Catheryne scanner changes

Upstream: akrios-d/AkashaScanner, MIT (see LICENSE.txt). This maintained build is based on 0.6.4 and targets .NET Desktop Runtime 8.

## Achievement recovery

- Select an existing result or progress file and explicitly confirm it belongs to the current account. Only completed IDs are reused; unknown and absent IDs are not treated as incomplete facts.
- Verified positives are seeded before inputs begin. After each search, an atomic `progress_<id>.json` stores results, searched count, reused count, and observed unresolved IDs. A later run can select this file.
- Progress status separates `running`, `interrupted`, `failed`, and `scan_finished`. None of these states proves that every in-game achievement was collected. Unvisited IDs are not included in `ObservedUnknownIds`.
- Overlapping search names still require review. Zero-star results remain unknown. Two consistent star samples reduce transient reads but are not identity verification. Compare identities and the live completed total before exporting a complete account record.
- This is incremental reuse of positives, not a new high-speed recognition engine. No 10x throughput claim is made. Negative candidates may be searched again on a later run.

## Build and integration

Run `apps/desktop/build-scanner.ps1` from the repository (optionally pass `-Dotnet <SDK executable>`), then `apps/desktop/package.ps1`. The checkpoint self-test does not control the game.

The release contains a clean executable template at `integrations/scanner`. The launcher prepares a writable copy under its local application data components/scanner/catheryne-8 directory. Scan results and configuration are never copied from the build tree. A user-selected upstream release keeps its existing separate version directory and does not receive these fork changes.

Do not replace a running scanner. Completed count alone is insufficient evidence for a Paimon import. Preserve the original export, merge only verified completed IDs, and compare a fresh export after importing.

## Catheryne 2 search changes

Plan a shortest unique ASCII substring against the entire loaded achievement catalog. Preserve original names/IDs; do not transliterate Unicode. Ambiguous or unsearchable titles remain unresolved. Positive results retain double sampling; zero-star unknowns skip the redundant sample. Existing completion reuse remains in place.

`--search-plan-check <catalog.json> <report.json>` validates uniqueness and reports planning time and typed character counts without game input. This is not an end-to-end scan benchmark. Catalog uniqueness does not prove live result identity when the catalog is stale.

## Catheryne 8 completion guard

The last tier requires its visible Completed/Claim label as well as consistent star samples. A single gold shape with a progress label or unreadable status remains unknown. Earlier completed tiers remain readable from stars. This does not turn OCR output into independently verified account facts.
