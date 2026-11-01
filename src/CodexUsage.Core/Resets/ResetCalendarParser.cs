using System.Globalization;
using System.Text.Json;

namespace CodexUsage.Resets;

public static class ResetCalendarParser
{
    public static (IReadOnlyList<ResetEvent> Events, DateTimeOffset? UpdatedAt) ParseHistory(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("events", out var events)
            || events.ValueKind != JsonValueKind.Array)
            throw new JsonException("Historie neobsahuje pole events.");
        var result = new List<ResetEvent>();
        foreach (var item in events.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Text(item, "id") is not { Length: > 0 } id)
                continue;
            var type = Text(item, "type");
            if (type is not ("global_reset" or "banked_reset")) continue;
            var status = Text(item, "status");
            var sourceType = Text(item, "source_type");
            var verified = status == "confirmed" && Instant(item, "verified_at") is not null
                && sourceType is "official" or "first_party";
            var timeBasis = Text(item, "time_basis");
            var dayOnly = Text(item, "time_precision") == "day";
            var rawEffective = Instant(item, "effective_at");
            var announced = dayOnly ? null : Instant(item, "announced_at");
            var observed = !dayOnly && timeBasis == "observation" ? rawEffective : null;
            var effective = !dayOnly && timeBasis == "effective" && verified ? rawEffective : null;
            DateOnly? date = ParseDate(Text(item, "announced_at")) ?? ParseDate(Text(item, "effective_at"));
            if (announced is null && observed is null && effective is null && date is null) continue;
            var kind = verified ? type == "banked_reset" ? ResetKind.Banked : ResetKind.ConfirmedGlobal
                : status == "announced" ? ResetKind.Announced : ResetKind.Uncertain;
            result.Add(new ResetEvent(id, Text(item, "summary") ?? Text(item, "title") ?? "Bez popisu",
                SafeUrl(Text(item, "source_url")), announced, effective, date, kind, null, status)
            {
                OriginalType = type,
                Scope = Text(item, "scope"),
                TimeKind = timeBasis,
                ObservedAt = observed,
                ArchiveUrl = SafeUrl(Text(item, "archive_url")),
                Notes = Text(item, "notes"),
                SourceType = sourceType,
                Audience = Text(item, "plan") is { Length: > 0 } plan ? [plan] : []
            });
        }
        // History has no published-update timestamp; retrieval and source-check times are not event updates.
        return (result.DistinctBy(e => e.Id).ToArray(), null);
    }

    public static IReadOnlyList<ResetEvent> ParseLatest(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Latest neobsahuje objekt.");
        var events = new List<JsonElement>();
        foreach (var name in new[] { "latestGlobalReset", "latestBankedReset", "latestResetEvent", "latestVerifiedReset" })
            if (document.RootElement.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Object)
                events.Add(item);
        return ParseHistory(JsonSerializer.Serialize(new { events })).Events;
    }

    public static bool IsPartialHistory(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Text(document.RootElement, "coverage") != "complete";
    }

    public static ResetForecast ParseAlternativeForecast(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("probability_24h", out _)
            || !root.TryGetProperty("probability_48h", out _))
            throw new JsonException("Odhad neobsahuje probability_24h / probability_48h.");
        var explanations = root.TryGetProperty("explanation", out var explanation) && explanation.ValueKind == JsonValueKind.Array
            ? explanation.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()) : [];
        // The live API publishes null and its docs do not establish numeric units. Do not guess a percent scale.
        return new ResetForecast(null, null, Text(root, "confidence"), string.Join(" ", explanations),
            Instant(root, "generated_at"), null, false);
    }

    private static DateOnly? ParseDate(string? text) => DateOnly.TryParseExact(text, "yyyy-MM-dd",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    public static (IReadOnlyList<ResetEvent> Events, DateTimeOffset? UpdatedAt) ParseTimeline(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            throw new JsonException("Timeline neobsahuje pole events.");
        var result = new List<ResetEvent>();
        foreach (var item in events.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Text(item, "id") is not { Length: > 0 } id)
                continue;
            var announced = Instant(item, "announced_at");
            var effective = Instant(item, "effective_at");
            var timeKind = Text(item, "time_kind");
            if (timeKind?.Contains("announcement", StringComparison.OrdinalIgnoreCase) == true)
                effective = null;
            DateOnly? date = DateOnly.TryParseExact(Text(item, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedDate) ? parsedDate : null;
            if (announced is null && effective is null && date is null)
                continue;
            var group = Text(item, "group");
            var banked = Text(item, "reset_kind") == "banked";
            if (group != "reset" && !banked)
                continue;
            var verification = Text(item, "reset_verification_status");
            var state = Text(item, "announcement_state");
            var bankedState = Text(item, "banked_state");
            var conflict = verification is not null && verification is not ("confirmed" or "verified")
                || Boolean(item, "preview") == true || Boolean(item, "confirmed") == false;
            var kind = ResetKind.Uncertain;
            if (!conflict && banked && Boolean(item, "confirmed") == true && bankedState == "available")
                kind = ResetKind.Banked;
            else if (!conflict && !banked && group == "reset" && state == "announced"
                && Text(item, "scope") == "global")
                kind = ResetKind.ConfirmedGlobal;
            else if (!conflict && (bankedState is "announced" or "arriving" || state is "scheduled" or "upcoming"))
                kind = ResetKind.Announced;
            result.Add(new ResetEvent(id, Text(item, "summary") ?? "Bez popisu", SafeUrl(Text(item, "url")),
                announced, effective, date, kind, Text(item, "confidence"), verification)
            {
                Scope = Text(item, "scope"),
                Audience = item.TryGetProperty("audience", out var audience) && audience.ValueKind == JsonValueKind.Array
                    ? audience.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()!).ToArray() : [],
                TimeKind = timeKind
            });
        }
        return (result.DistinctBy(e => e.Id).ToArray(), Instant(root, "updated_at"));
    }

    public static ResetForecast ParseForecast(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("probabilities", out var probabilities)
            || probabilities.ValueKind != JsonValueKind.Object)
            throw new JsonException("Forecast neobsahuje probabilities.");
        var experimental = root.TryGetProperty("backtest", out var backtest)
            && Text(backtest, "status") == "experimental";
        return new ResetForecast(Probability(probabilities, "rounded_24h"), Probability(probabilities, "rounded_48h"),
            Text(root, "confidence"), Text(root, "confidence_note"), Instant(root, "updated_at"),
            Instant(root, "last_reset_at"), experimental)
        {
            RecentMedianDays = root.TryGetProperty("cadence", out var cadence) && cadence.ValueKind == JsonValueKind.Object
                && cadence.TryGetProperty("recent_median_days", out var median) && median.ValueKind == JsonValueKind.Number
                && median.TryGetDouble(out var days) && double.IsFinite(days) && days > 0 ? days : null
        };
    }

    private static double? Probability(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number) && double.IsFinite(number) && number is >= 0 and <= 100 ? number : null;
    private static string? Text(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool? Boolean(JsonElement item, string name) => item.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    private static DateTimeOffset? Instant(JsonElement item, string name)
    {
        var value = Text(item, name);
        // Reject timestamps without a zone rather than silently interpreting the workstation's zone.
        return value is not null && (value.EndsWith('Z') || value.LastIndexOf('+') > 9 || value.LastIndexOf('-') > 9)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;
    }
    private static string? SafeUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http" ? uri.AbsoluteUri : null;
}
