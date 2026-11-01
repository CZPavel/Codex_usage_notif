# Public reset API assessment (2026-11-01)

Source: https://codexreset.dev/api/ . All seven documented read endpoint families were inspected; local GETs returned HTTP 200, including one event detail. These community records do not prove personal quota recovery.

| Endpoint | Use and observed limits |
| --- | --- |
| /api/history?lang=en | Initial historical backfill: 71 events, partial coverage; 13 in the displayed 30 Prague calendar dates at inspection. |
| /api/status?lang=en | Combined source/service state, event array and forecast. Fallback; not guaranteed to contain the entire archive. |
| /api/latest?lang=en | Latest global, banked, newest and independently verified records. Final network fallback, recent records only. Null is unknown. |
| /api/events/:id?lang=en | Full record and original evidence. Sample matched the archive, with recorded/community status and announcement-based timestamp. |
| /api/forecast?lang=en | Null probabilities, insufficient confidence and zero samples. No valid numeric forecast. |
| /api/accuracy?lang=en | Zero evaluable 24/48-hour forecasts in 30 days; Brier scores null. |
| /api/signals?lang=en | Public signals awaiting review, including unverified issue reports; not confirmed reset history. |

Most recent entries were recorded/community, without independent verification. latestVerifiedReset was null. Preserve original type, source URLs, scope, notes and archive provenance. time_basis separates announcement, observation and effective times. Date-only evidence stays date-only.

Historical records load immediately at startup. Fallback order: history, status, latest, retained local archive. Results merge by stable ID, so a small fallback response never erases older records. Old-only status data does not stop the latest fallback. The UI exposes record counts, source and partial coverage. Old archive records discovered after a fallback baseline do not trigger new-reset balloons.

Requests are anonymous GETs, timeout-limited, with per-endpoint minimum one minute and Retry-After. Normal refresh is 15 minutes. No antivirus configuration or credential behavior was changed; the previously blocked provider is not contacted.
