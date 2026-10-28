# UI rebuild verification — 2026-10-19

The five reference screens were rebuilt in WinForms. Later user instructions are included: no visible scrollbars; fix the reported JIT font exception and black/overlapping button paint; reduce the AI-slop appearance. The final theme uses flat charcoal surfaces, smaller Segoe UI typography, muted file icons, a red delete button and restrained progress motion.

## Delivered artifact

- Published executable: artifacts/ForceDeleter.exe (self-contained Windows x64).
- Icons: 27 original downloaded Tabler SVGs pinned to commit a4ce1404bc6d24d3c365afe7b258d6bf6f48d62d. Full MIT license and hashes are embedded and saved in src/ForceDeleter/Assets/Icons.
- No new runtime packages. The existing deletion engine, Program CLI and context-menu backend are unchanged from concurrent task commit bf35383.

## Observed checks

- Release build and test build: warnings treated as errors, zero errors/warnings.
- Real WinForms message loop: 158 passing assertions at actual 144 DPI (150%).
- 96-DPI logical layout: 158 passing assertions using a separate DPI-unaware test process. This does not claim a second physical 96-DPI monitor was tested.
- The real deletion engine deleted disposable fixture files and a folder only; queue, history filters/search, CSV escaping, persistence/retention, busy navigation, completion/timer shutdown, drag/drop, remove/clear, advanced options and maximize/restore were exercised.
- All five settings rows fit at minimum size without scrolling. Overflow lists still accept wheel/key movement and have no native scrollbar styles.
- Actual HWND render captures use PrintWindow and are saved in docs/screenshots. Direct desktop capture was verified during the rendering repair; later foreground desktop captures were occluded and were rejected, not used as evidence. Final captures are native window renders, not claimed as unobstructed desktop screenshots.
- The font lifetime fix removes disposal during nested label layout. Owner-drawn buttons explicitly repaint their parent surface, removing the black transparent background and accumulated icon remnants.
- Final batch: .adhd/batches/f0517dbf36a649a7987f815567bd1681/report.json — passed. Build, UI and publish receipts were reused only after unchanged input hashes were checked; artifact hash/license verification executed against the new binary.

## Reproduction

pwsh -File scripts/verify-ui.ps1 -Mode Build
pwsh -File scripts/verify-ui.ps1 -Mode Ui
pwsh -File scripts/verify-ui.ps1 -Mode Publish
pwsh -File scripts/verify-ui.ps1 -Mode Artifact

Optional unobstructed desktop screenshot check: run the test project with an output directory and --screen. It fails if another window covers the target. Native window capture and functional checks run by default.

## Limits and workflow record

Privileged/protected-file deletion and Explorer registry changes were not exercised; their backend is preserved. Folder sizes are not recursively scanned, so the UI labels combined totals as known file size when folders are present. No external release or Git commit was requested.

The original ADHD coding-scope baseline was removed by a concurrent task's cleanup/commits. Its audit fails with a missing .adhd/scope-base.json. The baseline is not fabricated or replaced. Native formal acceptance cannot be attested from that run; an independent read-only code/artifact review is recorded separately.


Independent artifact re-review: approved. All 13 source hashes matched; minimum Add preview clipping was fixed and both DPI runs passed 158 assertions. Details: docs/independent-review.json. Formal native approval is not claimed.

The selected adhd-native skill requires: 'Git coding tasks need a CLI-captured scope baseline and an observed scope-verification receipt.' Skill source: C:/Users/js100/.codex/adhd/releases/0.1.7-20261018-175334-dced50f2/skills/adhd-native/SKILL.md. The externally deleted baseline prevents that bookkeeping gate; it was not fabricated.
