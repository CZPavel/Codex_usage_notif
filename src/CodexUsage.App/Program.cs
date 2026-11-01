using CodexUsage.App.UI;

namespace CodexUsage.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (PreviewRenderer.TryRender(args))
        {
            return;
        }

        using var singleInstance = new SingleInstanceGuard();
        if (!singleInstance.IsFirstInstance)
        {
            if (singleInstance.LegacyInstanceRunning)
                MessageBox.Show("The original Codex Usage is running. Quit it from its tray menu, then open Reset Calendar again. Your original installation is preserved.",
                    "Codex Usage Reset Calendar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Application.Run(new TrayApplicationContext(singleInstance,
            showOnStart: !args.Contains("--background", StringComparer.OrdinalIgnoreCase)));
    }
}
