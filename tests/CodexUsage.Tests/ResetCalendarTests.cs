using System.Net;
using System.Text;
using System.Text.Json;
using CodexUsage.Resets;

namespace CodexUsage.Tests;

internal static class ResetCalendarTests
{
    public static int Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task<int> RunAsync()
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Reset calendar: " + message);
        }
        const string events = """
            {"updated_at":"2026-11-01T10:00:00Z","events":[
             {"id":"global","group":"reset","scope":"global","audience":["codex","chatgpt_work",null],"announcement_state":"announced","confirmed":true,"announced_at":"2026-10-31T19:00:00Z","effective_at":"2026-11-01T01:00:00Z","summary":"Global","url":"https://example.com/source"},
             {"id":"pending","group":"reset","scope":"global","announcement_state":"announced","confirmed":true,"reset_verification_status":"pending","date":"2026-10-28"},
             {"id":"banked","group":"credits","reset_kind":"banked","banked_state":"available","confirmed":true,"date":"2026-10-31"},
             {"id":"arriving","group":"credits","reset_kind":"banked","banked_state":"arriving","date":"2026-10-31"},
             {"id":"unknown-banked","group":"credits","reset_kind":"banked","banked_state":"unknown","confirmed":true,"date":"2026-10-31"},
             {"id":"other","group":"boost","confirmed":true,"date":"2026-10-31"},
             {"id":"invalid","group":"reset","date":"bad","announced_at":"2026-10-31T19:00:00"}, null, {}]}
            """;
        const string forecast = """
            {"updated_at":"2026-11-01T09:00:00Z","probabilities":{"rounded_24h":50,"rounded_48h":75},"confidence":"low","confidence_note":"Experimental","last_reset_at":"2026-10-28T06:35:38Z","backtest":{"status":"experimental"}}
            """;
        const string alternativeEvents = """
            {"events":[
             {"id":"global","type":"global_reset","status":"confirmed","source_type":"official","verified_at":"2026-11-01T02:00:00Z","announced_at":"2026-10-31T19:00:00Z","effective_at":"2026-11-01T01:00:00Z","time_basis":"effective","time_precision":"minute","scope":"Eligible accounts","summary":"Global"},
             {"id":"pending","type":"global_reset","status":"recorded","source_type":"community","verified_at":null,"announced_at":null,"effective_at":"2026-10-28T07:00:00Z","time_basis":"observation","time_precision":"minute"},
             {"id":"banked","type":"banked_reset","status":"confirmed","source_type":"official","verified_at":"2026-09-12T15:45:27Z","announced_at":"2026-06-11","effective_at":"2026-06-11","time_precision":"day","notes":"Historical feature launch","plan":"Plus / Pro"},
             {"id":"arriving","type":"global_reset","status":"announced","source_type":"official","announced_at":"2026-10-31T07:00:00Z","effective_at":"2026-11-01T07:00:00Z","time_basis":"effective"},
             {"id":"unknown-banked","type":"banked_reset","status":"recorded","source_type":"community","verified_at":null,"announced_at":"2026-11-01T00:43:20Z","effective_at":"2026-11-01T00:43:20Z","time_basis":"announcement","archive_url":"https://example.com/archive","notes":"Not independently verified"}],"count":5,"coverage":"partial","timezone":"UTC"}
            """;
        const string alternativeForecast = """
            {"generated_at":null,"probability_24h":null,"probability_48h":null,"confidence":"insufficient","sample_size":0,"model_version":"historical-gap-v1","explanation":["Insufficient history"]}
            """;
        var alternative = ResetCalendarParser.ParseHistory(alternativeEvents);
        Check(alternative.Events.Count == 5 && alternative.UpdatedAt is null, "alternative history exact schema, no invented update time");
        Check(alternative.Events[0].Kind == ResetKind.ConfirmedGlobal && alternative.Events[0].EffectiveAt is not null, "verified official effective global event");
        Check(alternative.Events[1].Kind == ResetKind.Uncertain && alternative.Events[1].EffectiveAt is null
            && alternative.Events[1].ObservedAt == DateTimeOffset.Parse("2026-10-28T07:00:00Z")
            && alternative.Events[1].DisplayAt == alternative.Events[1].ObservedAt, "community observation stays unverified with observation timestamp");
        Check(alternative.Events[2].Kind == ResetKind.Banked && alternative.Events[2].Date == new DateOnly(2026, 6, 11)
            && alternative.Events[2].DisplayAt is null, "verified historical banked date remains date-only");
        Check(alternative.Events[2].Audience.SequenceEqual(new[] { "Plus / Pro" }), "documented eligible plan retained as audience");
        Check(alternative.Events[3].Kind == ResetKind.Announced && alternative.Events[3].EffectiveAt is null, "planned time not verified effect");
        Check(alternative.Events[4].OriginalType == "banked_reset" && alternative.Events[4].Kind == ResetKind.Uncertain
            && alternative.Events[4].EffectiveAt is null && alternative.Events[4].Notes == "Not independently verified"
            && alternative.Events[4].ArchiveUrl == "https://example.com/archive", "archived banked retains original type and provenance without confirmation");
        Check(ResetCalendarParser.ParseHistory(alternativeEvents.Replace("\"verified_at\":\"2026-11-01T02:00:00Z\"", "\"verified_at\":null", StringComparison.Ordinal)).Events[0].Kind == ResetKind.Uncertain,
            "confirmed status without verification timestamp is insufficient");
        Check(!ResetCalendarParser.ParseAlternativeForecast(alternativeForecast).HasData
            && ResetCalendarParser.ParseAlternativeForecast(alternativeForecast).DisplayText == "Není dostatek dat", "alternative null probabilities unknown");
        Check(!ResetCalendarParser.ParseAlternativeForecast(alternativeForecast.Replace("\"probability_24h\":null", "\"probability_24h\":0.5", StringComparison.Ordinal)).HasData,
            "undocumented probability scale not guessed");
        var parsed = ResetCalendarParser.ParseTimeline(events);
        Check(parsed.Events.Count == 5, "ignore unrelated and undated malformed items");
        Check(parsed.Events.Single(e => e.Id == "global").Kind == ResetKind.ConfirmedGlobal, "confirmed global");
        Check(parsed.Events.Single(e => e.Id == "pending").Kind == ResetKind.Uncertain, "pending verification overrides confirmed flag");
        Check(parsed.Events.Single(e => e.Id == "banked").Kind == ResetKind.Banked, "available banked distinct from global");
        Check(parsed.Events.Single(e => e.Id == "arriving").Kind == ResetKind.Announced, "arriving banked is announcement");
        Check(parsed.Events.Single(e => e.Id == "unknown-banked").Kind == ResetKind.Uncertain, "unknown banked not confirmed");
        var global = parsed.Events[0];
        Check(global.Scope == "global" && global.Audience.SequenceEqual(new[] { "codex", "chatgpt_work" }), "provider scope and valid audience retained");
        Check(ResetCalendarParser.ParseTimeline(events.Replace("\"scope\":\"global\"", "\"scope\":\"selected\"", StringComparison.Ordinal)).Events[0].Kind == ResetKind.Uncertain,
            "limited audience scope is not a confirmed global reset");
        Check(global.DisplayAt == global.EffectiveAt && global.AnnouncedAt != global.EffectiveAt, "effective and announcement times distinct");
        Check(parsed.Events[1].DisplayAt is null && parsed.Events[1].Date.HasValue, "date-only time stays unknown");
        var announcementOnly = ResetCalendarParser.ParseTimeline(events.Replace("\"summary\":\"Global\"", "\"time_kind\":\"announcement / confirmation time\",\"scope_extra\":null,\"summary\":\"Global\"", StringComparison.Ordinal)).Events[0];
        Check(announcementOnly.EffectiveAt is null && announcementOnly.DisplayAt == announcementOnly.AnnouncedAt,
            "announcement time kind cannot become effective time");
        var odds = ResetCalendarParser.ParseForecast(forecast);
        Check(odds.HasData && odds.Probability24Hours == 50 && odds.Probability48Hours == 75, "documented percent units");
        Check(odds.Confidence == "low" && odds.IsExperimental, "provider uncertainty retained");
        Check(odds.DisplayText.Contains("codexreset.dev", StringComparison.Ordinal), "forecast attribution");
        Check(!ResetCalendarParser.ParseForecast(forecast.Replace("\"rounded_48h\":75", "\"rounded_48h\":25", StringComparison.Ordinal)).HasData,
            "inconsistent 48-hour probability does not become valid forecast");
        Check(odds.RecentMedianDays is null, "missing optional historical median stays unknown");
        Check(ResetCalendarParser.ParseForecast(forecast.Replace("\"confidence\":\"low\"", "\"cadence\":{\"recent_median_days\":1.5},\"confidence\":\"low\"", StringComparison.Ordinal)).RecentMedianDays == 1.5,
            "optional historical provider median is parsed");
        foreach (var value in new[] { "null", "{}", "{\"rounded_24h\":-1,\"rounded_48h\":101}", "{\"rounded_24h\":null,\"rounded_48h\":50}" })
        {
            try
            {
                var missing = ResetCalendarParser.ParseForecast("{\"probabilities\":" + value + "}");
                Check(!missing.HasData && missing.DisplayText == "Není dostatek dat", "missing/invalid is unknown");
            }
            catch (JsonException) { Check(value == "null", "null probabilities rejected safely"); }
        }
        foreach (var malformed in new[] { "null", "[]", "{}", "{\"events\":null}", "broken" })
        {
            try { ResetCalendarParser.ParseTimeline(malformed); Check(false, "malformed timeline rejected"); }
            catch (JsonException) { Check(true, "malformed timeline rejected"); }
        }
        var zone = ResetData.PragueTimeZone;
        Check(TimeZoneInfo.ConvertTime(DateTimeOffset.Parse("2026-03-29T00:30:00Z"), zone).Offset == TimeSpan.FromHours(1), "Prague before spring DST");
        Check(TimeZoneInfo.ConvertTime(DateTimeOffset.Parse("2026-03-29T01:30:00Z"), zone).Offset == TimeSpan.FromHours(2), "Prague after spring DST");
        Check(TimeZoneInfo.ConvertTime(DateTimeOffset.Parse("2026-10-25T01:30:00Z"), zone).Offset == TimeSpan.FromHours(1), "Prague autumn DST");
        var dateEvents = Enumerable.Range(0, 32).Select(i => new ResetEvent(i.ToString(), "", null, null, null,
            new DateOnly(2026, 11, 1).AddDays(-i), ResetKind.Uncertain, null, null)).ToArray();
        var period = new ResetData { Events = dateEvents }.GetLast30Days(DateTimeOffset.Parse("2026-11-01T12:00:00Z"));
        Check(period.Count == 30 && period.Min(e => e.Date) == new DateOnly(2026, 10, 3), "30 inclusive local calendar dates");
        var utcBoundary = global with { EffectiveAt = DateTimeOffset.Parse("2026-11-01T23:30:00Z") };
        Check(new ResetData { Events = [utcBoundary] }.GetLast30Days(DateTimeOffset.Parse("2026-11-01T12:00:00Z")).Count == 0,
            "UTC tomorrow boundary localized to Prague");

        var directory = Path.Combine(Path.GetTempPath(), "CodexResetTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await RunFallbackTestsAsync(Check, directory, alternativeEvents, alternativeForecast);
            var cachePath = Path.Combine(directory, "cache.json");
            var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
            var timeline = alternativeEvents;
            var forecastJson = alternativeForecast;
            var timelineStatus = HttpStatusCode.OK;
            var forecastStatus = HttpStatusCode.OK;
            var calls = 0;
            using var http = new HttpClient(new Handler(request =>
            {
                calls++;
                Check(request.Headers.Authorization is null && !request.Headers.Contains("Cookie")
                    && request.Headers.UserAgent.ToString().Contains("CZPavel/Codex_usage_notif", StringComparison.Ordinal), "anonymous identifying reader without credentials or cookies");
                Check(request.RequestUri!.Host == "codexreset.dev" && request.RequestUri.Query == "?lang=en", "only alternative provider production endpoints");
                var isTimeline = request.RequestUri.AbsolutePath.EndsWith("history", StringComparison.Ordinal);
                var response = new HttpResponseMessage(isTimeline ? timelineStatus : forecastStatus)
                { Content = new StringContent(isTimeline ? timeline : forecastJson, Encoding.UTF8, "application/json") };
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
                return response;
            }));
            using (var service = new ResetCalendarService(cachePath, http, () => now))
            {
                Check(service.LoadCached().Events.Count == 0, "missing cache allowed");
                var first = await service.FetchAsync();
                Check(first.Events.Count == 5 && first.Forecast is { HasData: false }, "independent alternative endpoints loaded with unknown forecast");
                Check(service.ConsumeNewConfirmedEvents().Count == 0, "first run silent baseline");
                await service.FetchAsync();
                Check(calls == 2, "manual refresh obeys minimum one minute");
                now += TimeSpan.FromMinutes(2);
                timeline = alternativeEvents.Replace("\"id\":\"global\"", "\"id\":\"new-global\"", StringComparison.Ordinal)
                    .Replace("2026-11-01T01:00:00Z", "2026-11-01T12:01:00Z", StringComparison.Ordinal);
                forecastStatus = HttpStatusCode.ServiceUnavailable;
                var partial = await service.FetchAsync();
                Check(partial.TimelineFetchedAt == now && partial.ForecastFetchedAt == first.ForecastFetchedAt && partial.ForecastError is not null,
                    "timeline success retains forecast last-good and timestamp");
                Check(service.ConsumeNewConfirmedEvents().Single().Id == "new-global", "new confirmed event once");
                Check(service.ConsumeNewConfirmedEvents().Count == 0, "no duplicate on repeated consume");
                now += TimeSpan.FromMinutes(2);
                timeline = "null";
                forecastStatus = HttpStatusCode.OK;
                forecastJson = alternativeForecast.Replace("Insufficient history", "Still insufficient", StringComparison.Ordinal);
                var reverse = await service.FetchAsync();
                Check(reverse.TimelineFetchedAt == partial.TimelineFetchedAt && reverse.TimelineError is not null
                    && reverse.ForecastFetchedAt == now && reverse.Forecast!.Note == "Still insufficient", "malformed timeline independent of successful forecast");
            }
            using (var restarted = new ResetCalendarService(cachePath, http, () => now))
            {
                var cached = restarted.LoadCached();
                Check(cached.Events.Any(e => e.Id == "new-global") && cached.Forecast!.Note == "Still insufficient", "last-good independently restored offline");
                Check(restarted.ConsumeNewConfirmedEvents().Count == 0, "persistent dedup survives restart");
                timelineStatus = HttpStatusCode.TooManyRequests;
                forecastStatus = HttpStatusCode.ServiceUnavailable;
                await restarted.FetchAsync();
                var afterThrottle = calls;
                now += TimeSpan.FromMinutes(2);
                await restarted.FetchAsync();
                Check(calls == afterThrottle + 1, "429 Retry-After blocks only timeline endpoint");
            }
            File.WriteAllText(cachePath, "broken");
            using (var corrupt = new ResetCalendarService(cachePath, http))
                Check(corrupt.LoadCached().Events.Count == 0, "corrupt cache tolerated");
            File.WriteAllText(cachePath, JsonSerializer.Serialize(new ResetCalendarService.Cache
            {
                Timeline = "null",
                Forecast = alternativeForecast,
                ForecastFetchedAt = now
            }));
            using (var partialCache = new ResetCalendarService(cachePath, http))
            {
                var restored = partialCache.LoadCached();
                Check(restored.Events.Count == 0 && restored.TimelineError is not null && restored.Forecast is { HasData: false },
                    "damaged timeline cache does not hide valid forecast cache");
            }
            using (var unavailable = new ResetCalendarService(directory, http, () => now))
            {
                timelineStatus = forecastStatus = HttpStatusCode.OK;
                timeline = alternativeEvents;
                var data = await unavailable.FetchAsync();
                Check(data.Events.Count > 0 && data.Forecast is not null, "unwritable cache does not break data refresh");
                Check(unavailable.ConsumeNewConfirmedEvents().Count == 0, "cache failure suppresses notification safely");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
        return assertions;
    }

    private static async Task RunFallbackTestsAsync(Action<bool, string> check, string directory, string archive, string forecast)
    {
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
        var history = archive;
        var status = archive;
        var latest = "{}";
        var historyCode = HttpStatusCode.OK;
        var statusCode = HttpStatusCode.OK;
        var latestCode = HttpStatusCode.OK;
        var requests = new List<string>();
        var statusRetry = TimeSpan.FromMinutes(5);
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requests.Add(path);
            check(request.RequestUri.Host == "codexreset.dev" && request.RequestUri.Query == "?lang=en"
                && request.Headers.Authorization is null && !request.Headers.Contains("Cookie"), "fallback uses anonymous same-host endpoints");
            var (code, body) = path switch
            {
                "/api/history" => (historyCode, history),
                "/api/status" => (statusCode, status),
                "/api/latest" => (latestCode, latest),
                "/api/forecast" => (HttpStatusCode.OK, forecast),
                _ => throw new InvalidOperationException("Unexpected endpoint")
            };
            var response = new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (path == "/api/status" && code == HttpStatusCode.TooManyRequests)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(statusRetry);
            return response;
        }));
        var cachePath = Path.Combine(directory, "fallback.json");
        using (var service = new ResetCalendarService(cachePath, http, () => now))
        {
            var first = await service.FetchAsync();
            check(first.Events.Any(e => e.Date == new DateOnly(2026, 6, 11)) && first.Events.Count == 5,
                "first fetch contains earlier archive dates without waiting for new events");
            check(first.TimelineSource == "history" && first.IsPartial, "provider partial coverage retained");
            check(service.ConsumeNewConfirmedEvents().Count == 0, "archive baseline silent");
            now += TimeSpan.FromMinutes(2);
            historyCode = HttpStatusCode.ServiceUnavailable;
            status = archive.Replace("\"id\":\"pending\"", "\"id\":\"status-only\"", StringComparison.Ordinal);
            var fallback = await service.FetchAsync();
            check(fallback.TimelineSource == "status" && fallback.IsPartial && fallback.StatusNote is not null
                && fallback.Events.Count == 6, "failed history uses status and merges stable IDs without losing archive");
            now += TimeSpan.FromMinutes(2);
            historyCode = HttpStatusCode.OK;
            history = "{\"events\":[]}";
            check((await service.FetchAsync()).TimelineSource == "status", "empty valid history uses status");
            now += TimeSpan.FromMinutes(2);
            statusCode = HttpStatusCode.TooManyRequests;
            using var document = JsonDocument.Parse(archive);
            latest = JsonSerializer.Serialize(new
            {
                latestGlobalReset = document.RootElement.GetProperty("events")[0],
                latestVerifiedReset = document.RootElement.GetProperty("events")[0],
                latestBankedReset = (object?)null,
                latestResetEvent = (object?)null
            });
            var minimal = await service.FetchAsync();
            check(minimal.TimelineSource == "latest" && minimal.IsPartial && minimal.Events.Count == 6,
                "status unavailable uses latest nullable objects, deduplicates and retains old history");
            var statusCalls = requests.Count(p => p == "/api/status");
            var calls = requests.Count;
            await service.FetchAsync();
            check(requests.Count == calls, "every endpoint respects minimum 60 seconds");
            now += TimeSpan.FromMinutes(2);
            await service.FetchAsync();
            check(requests.Count(p => p == "/api/status") == statusCalls, "status Retry-After isolated from history/latest/forecast");
        }
        using (var restart = new ResetCalendarService(cachePath, http, () => now))
        {
            var restored = restart.LoadCached();
            check(restored.TimelineSource == "latest" && restored.IsPartial && restored.Events.Count == 6
                && restored.StatusNote is not null, "merged history and fallback provenance survive restart");
            check(restart.ConsumeNewConfirmedEvents().Count == 0, "restart does not repeat baseline");
            historyCode = statusCode = latestCode = HttpStatusCode.ServiceUnavailable;
            check((await restart.FetchAsync()).Events.Count == 6, "offline cache retained across all failed endpoints");
            now += TimeSpan.FromMinutes(2);
            historyCode = HttpStatusCode.OK;
            history = archive.Replace("\"id\":\"global\"", "\"id\":\"recovered-historical\"", StringComparison.Ordinal);
            var recovered = await restart.FetchAsync();
            check(recovered.Events.Count == 7 && recovered.TimelineSource == "history", "recovered history merges previously missing old IDs");
            check(restart.ConsumeNewConfirmedEvents().Count == 0, "historical backfill never floods notifications");
        }
        using (var initialFallback = new ResetCalendarService(Path.Combine(directory, "initial-fallback.json"), http, () => now))
        {
            history = "broken";
            statusCode = HttpStatusCode.OK;
            var data = await initialFallback.FetchAsync();
            check(data.TimelineSource == "status" && data.GetLast30Days(now).Count > 0, "parse failure uses status on first run");
            check(initialFallback.ConsumeNewConfirmedEvents().Count == 0, "first successful fallback establishes silent baseline");
            now += TimeSpan.FromMinutes(2);
            history = archive.Replace("\"id\":\"global\"", "\"id\":\"old-backfill-after-fallback\"", StringComparison.Ordinal);
            await initialFallback.FetchAsync();
            check(initialFallback.ConsumeNewConfirmedEvents().Count == 0, "archive recovery after initial fallback suppresses historical notification flood");
        }
        using (var oldHistory = new ResetCalendarService(Path.Combine(directory, "old-history.json"), http, () => now))
        {
            history = archive.Replace("2026-11-01", "2026-09-01", StringComparison.Ordinal)
                .Replace("2026-10-", "2026-08-", StringComparison.Ordinal);
            var data = await oldHistory.FetchAsync();
            check(data.TimelineSource == "status" && data.GetLast30Days(now).Count > 0, "archive missing recent window checks status without losing old records");
        }
        using (var oldStatus = new ResetCalendarService(Path.Combine(directory, "old-status.json"), http, () => now))
        {
            historyCode = HttpStatusCode.ServiceUnavailable;
            status = archive.Replace("2026-11-01", "2026-09-01", StringComparison.Ordinal)
                .Replace("2026-10-", "2026-08-", StringComparison.Ordinal)
                .Replace("\"id\":\"global\"", "\"id\":\"old-status-global\"", StringComparison.Ordinal);
            statusCode = latestCode = HttpStatusCode.OK;
            var data = await oldStatus.FetchAsync();
            check(data.TimelineSource == "latest" && data.IsPartial && data.GetLast30Days(now).Count > 0
                && data.Events.Any(e => e.Id == "old-status-global"),
                "old-only status is retained while latest supplies recent calendar events");
        }
        using (var onlyOldFallback = new ResetCalendarService(Path.Combine(directory, "only-old-fallback.json"), http, () => now))
        {
            latest = "{\"latestResetEvent\":null}";
            var data = await onlyOldFallback.FetchAsync();
            check(data.Events.Count > 0 && data.GetLast30Days(now).Count == 0 && data.IsPartial
                && data.TimelineSource == "status" && data.TimelineFetchedAt is not null && data.TimelineError is null,
                "old-only fallback remains valid partial evidence when latest has no recent data");
        }
        using (var timeoutHttp = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/forecast") throw new OperationCanceledException();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri.AbsolutePath == "/api/history" ? "broken" : archive)
            };
        })))
        using (var timeoutService = new ResetCalendarService(Path.Combine(directory, "timeout.json"), timeoutHttp, () => now))
        {
            var data = await timeoutService.FetchAsync();
            check(data.TimelineSource == "status" && data.Events.Count == 5 && data.ForecastError is not null,
                "forecast timeout cannot suppress successful fallback timeline");
        }
        using (var unknown = new ResetCalendarService(Path.Combine(directory, "unknown.json"), http, () => now))
        {
            history = status = "{\"events\":[]}";
            latest = "{\"latestResetEvent\":null}";
            latestCode = HttpStatusCode.OK;
            var data = await unknown.FetchAsync();
            check(data.Events.Count == 0 && data.TimelineFetchedAt is null && data.TimelineError is not null,
                "all unknown remains unknown with no fabricated events");
            check(unknown.ConsumeNewConfirmedEvents().Count == 0, "no-data cannot create baseline notifications");
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
