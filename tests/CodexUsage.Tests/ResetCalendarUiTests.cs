using System.Runtime.InteropServices;
using CodexUsage.App.Settings;
using CodexUsage.App.UI;
using CodexUsage.Resets;

namespace CodexUsage.Tests;

internal static class ResetCalendarUiTests
{
    public static void Run()
    {
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var data = new ResetData
        {
            Events =
            [
                new ResetEvent("1", "global", null, null, now.AddDays(-29), null, ResetKind.ConfirmedGlobal, null, null),
                new ResetEvent("2", "banked", null, null, now.AddDays(-30), null, ResetKind.Banked, null, null),
                new ResetEvent("3", "announced", null, now, null, null, ResetKind.Announced, null, null),
                new ResetEvent("4", "uncertain", null, now.AddDays(-1), null, null, ResetKind.Uncertain, null, null),
            ],
        };
        if (data.GetLast30Days(now).Count != 3) throw new InvalidOperationException("30-day boundary failed");
        foreach (var theme in new[] { ThemeMode.Dark, ThemeMode.Light, ThemeMode.System })
        {
            using var form = new PopupForm(new AppSettings { Theme = theme });
            _ = form.Handle;
            form.UpdateResetData(data, false);
            form.SetCalendarVisible(true);
            form.PrepareOffscreenPreview();
            form.Show();
            Application.DoEvents();
            foreach (var dpi in new[] { 96, 144, 192 })
            {
                ChangeDpi(form, dpi);
                var expected = new Size(form.LogicalToDeviceUnits(382), form.LogicalToDeviceUnits(650));
                if (form.ClientSize != expected) throw new InvalidOperationException($"Calendar size at {dpi} DPI: {form.ClientSize}");
                var control = form.Controls.OfType<ResetCalendarControl>().Single();
                if (control.Bottom > form.ClientSize.Height || control.Right > form.ClientSize.Width)
                    throw new InvalidOperationException($"Calendar exceeds popup bounds at {dpi} DPI: control {control.Bounds}, popup {form.ClientSize}, actual DPI {form.DeviceDpi}");
                using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            }
            form.SetCalendarVisible(false);
            if (form.ClientSize.Height != form.LogicalToDeviceUnits(333)) throw new InvalidOperationException("Usage layout was not restored");
        }
        if (382 * 650 >= 4 * 382 * 333) throw new InvalidOperationException("Calendar exceeds area limit");
    }

    private static void ChangeDpi(Form form, int dpi)
    {
        var old = form.DeviceDpi;
        var bounds = form.Bounds;
        var rect = new NativeRectangle { Left = bounds.Left, Top = bounds.Top, Right = bounds.Left + (int)Math.Round(bounds.Width * (double)dpi / old), Bottom = bounds.Top + (int)Math.Round(bounds.Height * (double)dpi / old) };
        SendMessage(form.Handle, 0x02E0, new IntPtr(dpi | (dpi << 16)), ref rect);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, ref NativeRectangle lParam);
}
