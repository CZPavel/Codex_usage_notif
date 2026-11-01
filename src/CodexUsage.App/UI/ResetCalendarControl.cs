using System.Diagnostics;
using CodexUsage.Resets;

namespace CodexUsage.App.UI;

public sealed class ResetCalendarControl : Control
{
    private readonly ToolTip _tooltip = new();
    private readonly LinkLabel _source = new() { Text = "Open event source" };
    private readonly LinkLabel _attribution = new() { Text = "Data: codexreset.dev" };
    private readonly Button _refresh = new() { Text = "Refresh", FlatStyle = FlatStyle.Flat };
    private ThemePalette _palette = ThemePalette.Resolve(Settings.ThemeMode.Dark);
    private ResetData? _data;
    private ResetEvent? _selected;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public ResetCalendarControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        Controls.AddRange([_source, _attribution, _refresh]);
        _refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        _source.LinkClicked += (_, _) => OpenHttps(_selected?.SourceUrl);
        _attribution.LinkClicked += (_, _) => OpenHttps("https://codexreset.dev/");
        MouseMove += (_, e) => UpdateTooltip(e.Location);
        MouseClick += (_, e) => SelectDay(e.Location);
    }

    public event EventHandler? RefreshRequested;

    public void UpdateClock(DateTimeOffset now)
    {
        _now = now;
        Invalidate();
    }

    public void SetData(ResetData? data, bool refreshing, DateTimeOffset? now = null)
    {
        _data = data;
        _now = now ?? DateTimeOffset.UtcNow;
        _selected = data?.Events.OrderByDescending(EventDate).ThenByDescending(EventInstant).FirstOrDefault();
        _refresh.Enabled = !refreshing;
        _refresh.Text = refreshing ? "Refreshing…" : "Refresh";
        UpdateSource();
        Invalidate();
    }

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        BackColor = palette.Background;
        ForeColor = palette.Text;
        foreach (var link in new[] { _source, _attribution })
        {
            link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = palette.Accent;
            link.BackColor = palette.Background;
        }
        _refresh.BackColor = palette.Card;
        _refresh.ForeColor = palette.Text;
        _refresh.FlatAppearance.BorderColor = palette.Border;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        var s = DeviceDpi / 96f;
        _source.SetBounds((int)(18 * s), (int)(470 * s), (int)(150 * s), (int)(22 * s));
        _attribution.SetBounds((int)(18 * s), (int)(563 * s), (int)(180 * s), (int)(22 * s));
        _refresh.SetBounds(Width - (int)(96 * s), (int)(556 * s), (int)(78 * s), (int)(30 * s));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.ScaleTransform(DeviceDpi / 96f, DeviceDpi / 96f);
        using var title = new Font("Segoe UI", 14f, FontStyle.Bold);
        using var font = new Font("Segoe UI", 8.5f);
        using var small = new Font("Segoe UI", 7.5f);
        using var bold = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var text = new SolidBrush(_palette.Text);
        using var muted = new SolidBrush(_palette.SecondaryText);
        using var border = new Pen(_palette.Border);
        g.DrawString("Reset calendar", title, text, 18, 7);
        g.DrawString("Last 30 days · Europe/Prague", font, muted, 18, 35);
        var start = PragueDate(_now).AddDays(-29);
        var offset = ((int)start.DayOfWeek + 6) % 7;
        string[] days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
        for (var i = 0; i < 7; i++) g.DrawString(days[i], small, muted, 20 + (i * 49), 59);
        for (var i = 0; i < 30; i++)
        {
            var date = start.AddDays(i);
            var slot = offset + i;
            var x = 18 + (slot % 7 * 49);
            var y = 77 + (slot / 7 * 36);
            g.DrawRectangle(border, x, y, 45, 33);
            g.DrawString(date.Day == 1 || i == 0 ? date.ToString("d MMM") : date.Day.ToString(), small, text, x + 3, y + 1);
            var events = EventsOn(date).Take(4).ToArray();
            for (var j = 0; j < events.Length; j++)
            {
                using var marker = new SolidBrush(KindColor(events[j].Kind));
                g.FillEllipse(marker, x + 3 + (j * 10), y + 21, 7, 7);
            }
        }
        var legend = new[] { ("Global", _palette.Success), ("Banked", _palette.Accent), ("Announced", _palette.Warning), ("Uncertain", _palette.MutedText) };
        var lx = 18;
        foreach (var (name, color) in legend)
        {
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, lx, 304, 7, 7);
            g.DrawString(name, small, muted, lx + 9, 300);
            lx += name == "Announced" ? 82 : 66;
        }
        g.DrawString(_selected is null ? "No event selected" : "Selected / latest event", bold, text, 18, 322);
        DrawWrapped(g, _selected is null ? "No events available." : EventDetails(_selected), font, text, 18, 346, 344, 120);
        var last = _data?.Events.Where(x => x.Kind == ResetKind.ConfirmedGlobal && x.EffectiveAt is not null && x.EffectiveAt <= _now).OrderByDescending(x => x.EffectiveAt).FirstOrDefault();
        var elapsed = last?.EffectiveAt is DateTimeOffset effective ? FormatElapsed(_now - effective) + " since confirmed global reset" : "Time since confirmed global reset: unknown";
        g.DrawString(elapsed, small, muted, 18, 494);
        var forecast = _data?.Forecast;
        var median = forecast?.RecentMedianDays is double medianDays ? $"{medianDays:0.##} days" : "unknown";
        var forecastText = forecast?.HasData == true ? $"Experimental, low confidence, not guaranteed\n24h {Percent(forecast.Probability24Hours)}, 48h {Percent(forecast.Probability48Hours)} · recent historical median: {median}\nProvider confidence: {forecast.Confidence ?? "unknown"}. {forecast.Note}" : "Není dostatek dat";
        DrawWrapped(g, forecastText, small, muted, 18, 512, 344, 41);
        var age = _data?.TimelineUpdatedAt is DateTimeOffset updated ? $"Timeline data {FormatElapsed(_now - updated)} old" : _data?.TimelineFetchedAt is DateTimeOffset fetched ? $"Timeline fetched {FormatElapsed(_now - fetched)} ago (source age unknown)" : "Timeline unavailable / offline";
        var forecastAge = _data?.Forecast?.UpdatedAt is DateTimeOffset forecastUpdated ? $"Forecast data {FormatElapsed(_now - forecastUpdated)} old" : _data?.ForecastFetchedAt is DateTimeOffset forecastFetched ? $"Forecast fetched {FormatElapsed(_now - forecastFetched)} ago (source age unknown)" : "Forecast unavailable / offline";
        DrawWrapped(g, $"{age} · {_data?.TimelineError}\n{forecastAge} · {_data?.ForecastError}", small, muted, 18, 590, 344, 35);
    }

    private static void DrawWrapped(Graphics g, string value, Font font, Brush brush, int x, int y, int width, int height) => g.DrawString(value, font, brush, new RectangleF(x, y, width, height));
    private static string Percent(double? value) => value is null ? "unknown" : $"{value:0.#}%";
    private static string FormatElapsed(TimeSpan span) => span < TimeSpan.Zero ? "unknown" : span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {span.Hours}h" : $"{(int)span.TotalHours}h {span.Minutes}m";
    private static DateTimeOffset EventInstant(ResetEvent e) => e.DisplayAt ?? DateTimeOffset.MinValue;
    private static DateOnly PragueDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, ResetData.PragueTimeZone).DateTime);
    private static DateOnly? EventDate(ResetEvent e) => e.DisplayAt is DateTimeOffset instant ? PragueDate(instant) : e.Date;
    private IEnumerable<ResetEvent> EventsOn(DateOnly date) => _data?.Events.Where(e => EventDate(e) == date) ?? [];
    private Color KindColor(ResetKind kind) => kind switch { ResetKind.ConfirmedGlobal => _palette.Success, ResetKind.Banked => _palette.Accent, ResetKind.Announced => _palette.Warning, _ => _palette.MutedText };
    private static string TimeText(DateTimeOffset? time) => time is null ? "unknown" : TimeZoneInfo.ConvertTime(time.Value, ResetData.PragueTimeZone).ToString("dd MMM yyyy HH:mm zzz");
    private static string EventDetails(ResetEvent e) => $"Type: {e.OriginalType ?? e.Kind.ToString()} ({e.Kind}) · Verification: {e.VerificationStatus ?? "unknown"}\nEffective: {TimeText(e.EffectiveAt)}\nAnnounced: {TimeText(e.AnnouncedAt)}\nObserved: {TimeText(e.ObservedAt)}\nDate: {e.Date?.ToString("dd MMM yyyy") ?? "unknown"} · Confidence: {e.Confidence ?? "unknown"}\nScope: {e.Scope ?? "unknown"} · Audience: {(e.Audience.Count == 0 ? "unknown" : string.Join(", ", e.Audience))}\n{e.Summary}\n{e.Notes}\nArchive: {e.ArchiveUrl ?? "unknown"}";
    private DateOnly? DayAt(Point point)
    {
        var s = DeviceDpi / 96f;
        var x = point.X / s - 18;
        var y = point.Y / s - 77;
        if (x < 0 || y < 0 || x >= 343 || y >= 216 || x % 49 >= 45 || y % 36 >= 33) return null;
        var start = PragueDate(_now).AddDays(-29);
        var index = ((int)y / 36 * 7) + ((int)x / 49) - (((int)start.DayOfWeek + 6) % 7);
        return index is >= 0 and < 30 ? start.AddDays(index) : null;
    }
    private void UpdateTooltip(Point point)
    {
        var day = DayAt(point);
        var value = day is null ? string.Empty : string.Join("\n\n", EventsOn(day.Value).Select(EventDetails));
        if (_tooltip.GetToolTip(this) != value) _tooltip.SetToolTip(this, value);
    }
    private void SelectDay(Point point)
    {
        var day = DayAt(point);
        if (day is null) return;
        _selected = EventsOn(day.Value).OrderByDescending(EventInstant).FirstOrDefault();
        UpdateSource();
        Invalidate();
    }
    private void UpdateSource() => _source.Visible = IsHttps(_selected?.SourceUrl);
    private static bool IsHttps(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    private static void OpenHttps(string? url)
    {
        if (!IsHttps(url)) return;
        try { Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _tooltip.Dispose();
        base.Dispose(disposing);
    }
}
