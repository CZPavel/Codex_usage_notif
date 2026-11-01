namespace CodexUsage.App;

/// <summary>One interactive tray instance across fork copies and the preserved upstream installation.</summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly Mutex? _legacyMutex;
    private readonly EventWaitHandle _activation;
    public bool IsFirstInstance { get; }
    public bool LegacyInstanceRunning { get; }

    public SingleInstanceGuard(string name = @"Local\CodexUsageNotif.Tray.SingleInstance",
        string? legacyName = @"Local\CodexUsage.Tray.SingleInstance")
    {
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        _mutex = new Mutex(false, name, out var first);
        if (!first)
        {
            _activation.Set();
            return;
        }
        if (legacyName is not null)
        {
            _legacyMutex = new Mutex(false, legacyName, out var legacyFirst);
            if (!legacyFirst)
            {
                LegacyInstanceRunning = true;
                return;
            }
        }
        IsFirstInstance = true;
    }

    public bool TakeActivationRequest() => IsFirstInstance && _activation.WaitOne(0);

    public void Dispose()
    {
        _legacyMutex?.Dispose();
        _mutex.Dispose();
        _activation.Dispose();
    }
}
