# Validation — 2026-11-01

Upstream identified from running installed process and file metadata: VictorZakharov/codex-usage v0.2.7, commit 75cf1eaad84c5794fdf733157bd443f8d1103800. Original local read-only-auth patch retained. Original process/install, settings and autostart left intact. Original history/settings copied once to a separate runtime directory; never included in Git.

## Passed

- .NET SDK 10.0.401: restore, dotnet format --verify-no-changes --no-restore, Release solution build: 0 warnings, 0 errors.
- Full custom harness: PASS 196 assertions, including 57 reset-service checks. Additional UI suite checks 30-day boundaries, three themes and 96/144/192 DPI. Existing history/usage/token tests retained.
- Live usage GET successfully returned current plan and both quotas. Credentials and raw responses were not logged or committed.
- Both 401 and 403 fixtures prove one usage GET, unchanged synthetic auth.json and no credential temporary files.
- Reset parser: distinct global/banked/announced/uncertain, conflicting verification, missing fields, scope/audience, announcement/effective distinction, Prague spring/autumn DST, UTC/local date boundaries.
- Injected HTTP/cache tests: separate last-good endpoints/timestamps, offline reload, null/malformed JSON, unwritable/corrupt cache, silent initial baseline, new-ID notification selection, persistent dedup across restart, minimum polling interval, HTTP 429 Retry-After, no credential/cookie headers.
- Synthetic calendar dark/light PNGs inspected. Original installed EXE preview measured 382x409 px; calendar 382x650 = 1.59x area. Also below 4x the smallest baseline 382x333 (1.95x). Calendar growth is clamped to monitor working area.
- Self-contained Windows x64 publish completed. A prior installed build ran alongside the original, responded, loaded timeline into the new cache and established its silent notification baseline. The new autostart entry remained absent; original entry retained.

## Runtime blocker

AVG Antivirus (Avast engine) blocked the public forecast URL as URL:Phishing. Its quarantine index then recorded the final installed CodexUsageNotif executable as URL:Phishing on 2026-11-01. The executable was removed from the installation path by AVG. No quarantine restore, exception, security-policy change or alternate build intended to evade the detection was attempted.

Final self-contained artifact remains under artifacts/win-x64/CodexUsage.exe (ignored by Git), SHA-256 912471009f6ff9de11ea348793ccdf3a1fbadf07a9fc27f7cf2a60107625e0b8. Build and automated checks passed; **final installed EXE runtime acceptance remains blocked by antivirus**. Do not claim immediate installed availability.

## Limits

Actual delivery of a future Windows balloon and a full Windows reboot were not tested. Deduplication/restart logic was tested with fixtures; the actual app was restarted during installation checks. Latest external data may omit exact effective times or contain contradictory verification, so the global reset elapsed time may correctly remain unknown. Provider forecast is unofficial, experimentally calibrated, low-confidence and not guaranteed. No forecast probabilities were obtained locally because of the security block. No release tag or public release was created.
