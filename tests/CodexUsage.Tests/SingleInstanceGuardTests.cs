using CodexUsage.App;

namespace CodexUsage.Tests;

internal static class SingleInstanceGuardTests
{
    public static void Run()
    {
        var name = @"Local\CodexUsage.Test." + Guid.NewGuid().ToString("N");
        using (var first = new SingleInstanceGuard(name, name + ".Legacy"))
        {
            if (!first.IsFirstInstance || first.TakeActivationRequest()) throw new InvalidOperationException("First instance acquisition failed");
            using var second = new SingleInstanceGuard(name, name + ".Legacy");
            if (second.IsFirstInstance || !first.TakeActivationRequest() || first.TakeActivationRequest())
                throw new InvalidOperationException("Duplicate launch did not activate existing instance exactly once");
        }
        using var restarted = new SingleInstanceGuard(name, name + ".Legacy");
        if (!restarted.IsFirstInstance) throw new InvalidOperationException("Instance lock did not release on exit");
        using var legacy = new Mutex(false, name + ".OtherLegacy");
        using var conflict = new SingleInstanceGuard(name + ".Other", name + ".OtherLegacy");
        if (conflict.IsFirstInstance || !conflict.LegacyInstanceRunning)
            throw new InvalidOperationException("Original application coexistence guard failed");
    }
}
