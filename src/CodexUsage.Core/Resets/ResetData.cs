namespace CodexUsage.Resets;

public enum ResetKind { ConfirmedGlobal, Banked, Announced, Uncertain }

public sealed record ResetEvent(string Id, string Summary, string? SourceUrl,
    DateTimeOffset? AnnouncedAt, DateTimeOffset? EffectiveAt, DateOnly? Date,
    ResetKind Kind, string? Confidence, string? VerificationStatus)
{
    public string? Scope { get; init; }
    public IReadOnlyList<string> Audience { get; init; } = [];
    public string? TimeKind { get; init; }
    public string? OriginalType { get; init; }
    public DateTimeOffset? ObservedAt { get; init; }
    public string? ArchiveUrl { get; init; }
    public string? Notes { get; init; }
    public string? SourceType { get; init; }
    public DateTimeOffset? DisplayAt => EffectiveAt ?? AnnouncedAt ?? ObservedAt;
    /// <summary>Provider verification of this dated record, not current availability on the user's account.
    /// A banked record may describe a historical feature launch; retain Notes and Scope in presentation.</summary>
    public bool IsConfirmed => Kind is ResetKind.ConfirmedGlobal or ResetKind.Banked;
}

public sealed record ResetForecast(double? Probability24Hours, double? Probability48Hours,
    string? Confidence, string? Note, DateTimeOffset? UpdatedAt, DateTimeOffset? LastResetAt,
    bool IsExperimental)
{
    public double? RecentMedianDays { get; init; }
    public bool HasData => Probability24Hours.HasValue && Probability48Hours.HasValue
        && Probability48Hours >= Probability24Hours;
    public string DisplayText => HasData
        ? $"Odhad codexreset.dev: {Probability24Hours:0.#} % / 24 h, {Probability48Hours:0.#} % / 48 h"
        : "Není dostatek dat";
}

public sealed record ResetData
{
    public static TimeZoneInfo PragueTimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
    public IReadOnlyList<ResetEvent> Events { get; init; } = [];
    public ResetForecast? Forecast { get; init; }
    public DateTimeOffset? TimelineFetchedAt { get; init; }
    public DateTimeOffset? ForecastFetchedAt { get; init; }
    public DateTimeOffset? TimelineUpdatedAt { get; init; }
    public string? TimelineSource { get; init; }
    public bool IsPartial { get; init; }
    public string? StatusNote { get; init; }
    public string? TimelineError { get; init; }
    public string? ForecastError { get; init; }

    // Thirty local calendar dates, including today; date-only entries stay date-only.
    public IReadOnlyList<ResetEvent> GetLast30Days(DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= PragueTimeZone;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        return Events.Where(e =>
        {
            var date = e.DisplayAt is { } at
                ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime) : e.Date;
            return date >= today.AddDays(-29) && date <= today;
        }).OrderByDescending(e => e.DisplayAt).ThenByDescending(e => e.Date).ToArray();
    }
}
