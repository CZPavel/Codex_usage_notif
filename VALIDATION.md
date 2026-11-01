# Validation — 2026-11-01

## Follow-up fixes

- Final suite now passes 288 assertions (149 reset-service assertions), plus UI/DPI and single-instance guard checks. Release build and formatting pass without warnings.
- An opt-in live test starts with a completely empty temporary cache: first fetch returned 71 historical events, 13 within the displayed 30 Prague calendar days, source history, partial coverage. No Codex credentials are read by this check.
- Actual installed application cache renders those 13 events. Previously all recent recorded/unverified entries were tiny gray dots; counts, highlighted dates and outlined type colors now make loaded data explicit without claiming verification.
- History failure, malformed/empty/old-only results try status and then latest. Old-only status does not stop the latest fallback. Tests cover retained archive, partial provenance, restart, throttling and silent recovery of historical notifications.
- Named activation event reopens the existing popup when another fork copy launches. A shared legacy mutex also prevents simultaneous upstream/fork tray instances; original installation and startup settings remain unchanged. An actual second launch from the build directory exited while one installed process remained.
- Desktop shortcut created and verified: Codex Usage Reset Calendar.lnk, targeting the installed fork executable. Tests also cover restart and conflict with a legacy instance.
- Current final binary SHA-256: d3fdbd72a55dcc8fff433b9bac2fdc9eb40381582ec3d7a71b07e1ce50b75700 . The baseline validation below describes the initial version before this follow-up.

Endpoint assessment: [reset-api.md](docs/reset-api.md).

Upstream identified from running installed process and file metadata: VictorZakharov/codex-usage v0.2.7, commit 75cf1eaad84c5794fdf733157bd443f8d1103800. Original local read-only-auth patch retained. Original process/install, settings and autostart left intact. Original history/settings copied once to a separate runtime directory; never included in Git.

## Passed in final version

- .NET SDK 10.0.401: restore, dotnet format --verify-no-changes --no-restore, Release solution build: 0 warnings, 0 errors.
- Full custom harness: PASS 217 assertions, including 78 reset-service checks. Additional UI suite checks 30-day boundaries, three themes and 96/144/192 DPI. Existing history/usage/token tests retained.
- Live usage GET successfully returned current plan and both quotas. Credentials and raw responses were not logged or committed. Final application also refreshed its independent usage history.
- Both 401 and 403 fixtures prove one usage GET, unchanged synthetic auth.json and no credential temporary files.
- Reset parser: distinct global/banked/announced/uncertain, contradictory/missing verification, scope/audience, original event type and archive provenance, announcement/observation/effective distinction, day-only evidence, Prague spring/autumn DST, UTC/local date boundaries.
- Injected HTTP/cache tests: separate last-good endpoints/timestamps, offline reload, null/malformed JSON, unwritable/corrupt cache, silent initial baseline, new-ID notification selection, persistent dedup across restart, minimum polling interval, HTTP 429 Retry-After, no credential/cookie headers.
- Synthetic dark/light calendar previews inspected. Original installed EXE preview measured 382x409 px; calendar 382x650 = 1.59x area. Also below 4x the smallest baseline 382x333 (1.95x). Calendar growth is clamped to monitor working area.
- Self-contained Windows x64 publish completed and installed under %LOCALAPPDATA%/Programs/CodexUsageNotif/CodexUsage.exe. Final EXE launched, responds, and runs alongside the original EXE. Its new cache contains 71 events and successful timestamps from both alternative endpoints; forecast confidence is insufficient. Silent notification baseline established.
- New autostart entry remains absent; original CodexUsage startup value and running process retained.

Final local binary SHA-256: 7d6c85dc8ce0161f33c140ba80777bce2cdb2ab8251e7e21460a39f8bf9bee23. Binary is intentionally excluded from Git; artifacts/win-x64/CodexUsage.exe is the build copy. Installed binary has the same digest. No release tag or public release was created.

## External-source limitation and safe remediation

The initially integrated primary provider codex-reset.com had its forecast URL blocked by AVG Antivirus (Avast engine) as URL:Phishing. AVG then quarantined that initial installed build with the same classification. No quarantine restore, exception or protection change was made.

The user explicitly listed codexreset.dev as an alternative. Its documented read APIs were inspected and both endpoints returned local HTTP 200. Production HTTP now uses ONLY this alternate domain and a new reset-calendar-alt.json cache, never the blocked domain or old cache. The rebuilt final version started successfully and fetched both endpoints without a block observed during validation. This demonstrates current local operation, not a guarantee of future antivirus classification.

The alternative is partial community history: recent records are mostly status=recorded, not independently verified. These remain Uncertain, retain global/banked original type, and never generate confirmed-reset alerts. Announcement/observation timestamps are not effective resets. Current probability values are null with confidence insufficient; the UI says Neni dostatek dat (with Czech accents). Undocumented numeric probability units are not guessed. Exact last confirmed global elapsed time may correctly stay unknown.

## Remaining practical limits

Actual delivery of a future Windows balloon and a full Windows reboot were not tested. Notification/restart logic was tested using deterministic fixtures. Preview data is synthetic. Public reset records do not prove that this personal account received a reset. External services and antivirus classifications can change.
