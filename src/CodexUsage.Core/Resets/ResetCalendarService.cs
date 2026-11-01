using System.Text.Json;

namespace CodexUsage.Resets;

/// <summary>Independent public data reader; never reads or writes Codex credentials.</summary>
public sealed class ResetCalendarService : IDisposable
{
    public const string SourceUrl = "https://codexreset.dev/";
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _cachePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _now;
    private Cache _cache = new();
    private DateTimeOffset _timelineNextAttempt;
    private DateTimeOffset _forecastNextAttempt;
    private DateTimeOffset _statusNextAttempt;
    private DateTimeOffset _latestNextAttempt;

    public ResetData Current { get; private set; } = new();

    public ResetCalendarService(string? cachePath = null, HttpClient? httpClient = null,
        Func<DateTimeOffset>? now = null)
    {
        _cachePath = cachePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexUsageNotif", "reset-calendar-alt.json");
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Call before starting refreshes. Corrupt endpoint data is isolated from the other endpoint.</summary>
    public ResetData LoadCached()
    {
        try
        {
            if (File.Exists(_cachePath))
                _cache = JsonSerializer.Deserialize<Cache>(File.ReadAllText(_cachePath)) ?? new Cache();
        }
        catch (Exception ex) when (IsFileError(ex) || ex is JsonException)
        {
            _cache = new Cache();
        }
        _cache.Seen ??= [];
        Current = new ResetData();
        if (_cache.Timeline is { } timeline)
        {
            try
            {
                var parsed = ResetCalendarParser.ParseHistory(timeline);
                Current = Current with { Events = parsed.Events, TimelineUpdatedAt = parsed.UpdatedAt, TimelineFetchedAt = _cache.TimelineFetchedAt };
            }
            catch (JsonException) { Current = Current with { TimelineError = "Uložená historie resetů je poškozená." }; }
        }
        if (_cache.Events is { } savedEvents)
            Current = Current with { Events = savedEvents, TimelineFetchedAt = _cache.TimelineFetchedAt, TimelineError = null };
        Current = Current with
        {
            TimelineSource = _cache.TimelineSource ?? (Current.Events.Count > 0 ? "history" : null),
            IsPartial = _cache.IsPartial || (_cache.TimelineSource is null && Current.Events.Count > 0),
            StatusNote = _cache.StatusNote
        };
        if (_cache.Forecast is { } forecast)
        {
            try
            {
                Current = Current with { Forecast = ResetCalendarParser.ParseAlternativeForecast(forecast), ForecastFetchedAt = _cache.ForecastFetchedAt };
            }
            catch (JsonException) { Current = Current with { ForecastError = "Uložený odhad je poškozený." }; }
        }
        return Current;
    }

    /// <summary>The caller schedules 15-minute refreshes. Manual calls also respect provider throttling.</summary>
    public async Task<ResetData> FetchAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _now();
            var timelineTask = RefreshTimelineAsync(now, cancellationToken);
            var forecastTask = now >= _forecastNextAttempt ? ReadAsync("api/forecast?lang=en", cancellationToken) : Task.FromResult<ReadResult?>(null);
            await Task.WhenAll(timelineTask, forecastTask).ConfigureAwait(false);
            if (await forecastTask.ConfigureAwait(false) is { } forecast)
            {
                _forecastNextAttempt = _now() + Delay(forecast);
                if (forecast.Json is { } json)
                {
                    try
                    {
                        var parsed = ResetCalendarParser.ParseAlternativeForecast(json);
                        // Missing probabilities are a valid unknown result, never an implicit zero.
                        _cache.Forecast = json;
                        _cache.ForecastFetchedAt = now;
                        Current = Current with { Forecast = parsed, ForecastFetchedAt = now, ForecastError = null };
                    }
                    catch (JsonException) { Current = Current with { ForecastError = "Neplatná odpověď odhadu; zobrazena poslední platná data." }; }
                }
                else Current = Current with { ForecastError = forecast.Error };
            }
            SaveCache();
            return Current;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Call after each refresh, even when notifications are disabled. First data is a silent baseline.
    /// Persistence must succeed before returning notifications, preventing repeats after a restart.</summary>
    public IReadOnlyList<ResetEvent> ConsumeNewConfirmedEvents()
    {
        // Called by the UI after awaited FetchAsync, never concurrently with a refresh.
        if (Current.TimelineFetchedAt is null)
            return [];
        var confirmed = Current.Events.Where(e => e.IsConfirmed).ToArray();
        var unseen = confirmed.Where(e => !_cache.Seen.Contains(Key(e), StringComparer.Ordinal)).ToArray();
        var baseline = _cache.BaselineEstablished;
        _cache.Seen = _cache.Seen.Concat(unseen.Select(Key)).Distinct(StringComparer.Ordinal).ToArray();
        _cache.BaselineEstablished = true;
        _cache.BaselineAt ??= _now();
        var baselineDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_cache.BaselineAt.Value, ResetData.PragueTimeZone).DateTime);
        var newEvents = unseen.Where(e => e.DisplayAt is { } at ? at >= _cache.BaselineAt : e.Date >= baselineDate).ToArray();
        return SaveCache() && baseline ? newEvents : [];
    }

    private async Task RefreshTimelineAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (now < _timelineNextAttempt) return;
        var history = await ReadAsync("api/history?lang=en", cancellationToken).ConfigureAwait(false);
        _timelineNextAttempt = _now() + Delay(history);
        IReadOnlyList<ResetEvent>? historyEvents = null;
        string? error = history?.Error;
        if (history?.Json is { } json)
        {
            try { historyEvents = ResetCalendarParser.ParseHistory(json).Events; }
            catch (JsonException) { error = "Neplatná odpověď historie resetů; zobrazena poslední platná data."; }
        }
        if (historyEvents is { Count: > 0 })
        {
            // Preserve the entire archive, including entries outside the visible 30-day window.
            ApplyTimeline(historyEvents, "history", now, history!.Json);
            if (HasRecent(historyEvents, now)) return;
        }
        else error ??= "Historie resetů je prázdná; náhradní zdroje nejsou dostupné.";

        var fallbackApplied = false;
        foreach (var source in new[] { "status", "latest" })
        {
            var nextAttempt = source == "status" ? _statusNextAttempt : _latestNextAttempt;
            if (now < nextAttempt) continue;
            var response = await ReadAsync($"api/{source}?lang=en", cancellationToken).ConfigureAwait(false);
            if (source == "status") _statusNextAttempt = _now() + Delay(response);
            else _latestNextAttempt = _now() + Delay(response);
            if (response?.Json is not { } fallback) continue;
            try
            {
                var events = source == "status" ? ResetCalendarParser.ParseHistory(fallback).Events : ResetCalendarParser.ParseLatest(fallback);
                if (events.Count == 0) continue;
                ApplyTimeline(events, source, now, null);
                fallbackApplied = true;
                if (source == "latest" || HasRecent(events, now)) return;
            }
            catch (JsonException) { /* An unavailable fallback must never replace a good archive. */ }
        }
        if (!fallbackApplied && historyEvents is not { Count: > 0 })
            Current = Current with { TimelineError = error ?? "Historie resetů není dostupná; zobrazena poslední platná data." };
    }

    private void ApplyTimeline(IReadOnlyList<ResetEvent> events, string source, DateTimeOffset now, string? historyJson)
    {
        var merged = events.Concat(Current.Events).DistinctBy(e => e.Id).ToArray();
        var partial = source != "history" || ResetCalendarParser.IsPartialHistory(historyJson!);
        var note = source != "history" ? $"Náhradní zdroj /api/{source}; historie může být neúplná."
            : partial ? "Poskytovatel nezaručuje úplný archiv resetů." : null;
        _cache.Events = merged;
        if (historyJson is not null) _cache.Timeline = historyJson;
        _cache.TimelineFetchedAt = now;
        _cache.TimelineSource = source;
        _cache.IsPartial = partial;
        _cache.StatusNote = note;
        Current = Current with
        {
            Events = merged,
            TimelineFetchedAt = now,
            TimelineUpdatedAt = null,
            TimelineError = null,
            TimelineSource = source,
            IsPartial = partial,
            StatusNote = note
        };
    }

    private static bool HasRecent(IReadOnlyList<ResetEvent> events, DateTimeOffset now)
        => new ResetData { Events = events }.GetLast30Days(now).Count > 0;

    private static TimeSpan Delay(ReadResult? response)
        => response?.RetryAfter is { } delay && delay > TimeSpan.FromMinutes(1) ? delay : TimeSpan.FromMinutes(1);

    private async Task<ReadResult?> ReadAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, SourceUrl + relativeUrl);
            request.Headers.UserAgent.ParseAdd("CodexUsage/1.0 (+https://github.com/CZPavel/Codex_usage_notif)");
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - _now());
                if (delay < TimeSpan.FromMinutes(1)) delay = TimeSpan.FromMinutes(1);
                return new(null, $"codexreset.dev: HTTP {(int)response.StatusCode}; zobrazena poslední platná data.", delay);
            }
            return new(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false), null, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(null, "codexreset.dev: vypršel časový limit; zobrazena poslední platná data.", null);
        }
        catch (HttpRequestException)
        {
            return new(null, "codexreset.dev je nedostupný; zobrazena poslední platná data.", null);
        }
    }

    private bool SaveCache()
    {
        string? temporary = null;
        try
        {
            var fullPath = Path.GetFullPath(_cachePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_cache));
            File.Move(temporary, fullPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (IsFileError(ex)) { return false; }
        finally
        {
            try { if (temporary is not null) File.Delete(temporary); }
            catch (Exception ex) when (IsFileError(ex)) { }
        }
    }

    private static string Key(ResetEvent e) => $"{e.Kind}:{e.Id}";
    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;
    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    private sealed record ReadResult(string? Json, string? Error, TimeSpan? RetryAfter);
    public sealed class Cache
    {
        public string? Timeline { get; set; }
        public ResetEvent[]? Events { get; set; }
        public string? TimelineSource { get; set; }
        public bool IsPartial { get; set; }
        public string? StatusNote { get; set; }
        public DateTimeOffset? BaselineAt { get; set; }
        public string? Forecast { get; set; }
        public DateTimeOffset? TimelineFetchedAt { get; set; }
        public DateTimeOffset? ForecastFetchedAt { get; set; }
        public bool BaselineEstablished { get; set; }
        public string[] Seen { get; set; } = [];
    }
}
