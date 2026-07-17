namespace Pix.Launcher;

/// <summary>
/// Guarantees one launcher per Windows user session and lets later launches
/// request that the existing floating window opens its control panel.
/// </summary>
internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = @"Local\PixLauncher.SingleInstance";
    private const string ActivationEventName = @"Local\PixLauncher.Activate";

    private readonly Mutex mutex;
    private readonly EventWaitHandle activationEvent;
    private RegisteredWaitHandle? registeredWait;

    public bool IsPrimaryInstance { get; }

    public SingleInstanceCoordinator()
    {
        mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        IsPrimaryInstance = createdNew;
    }

    public void SignalExistingInstance() => activationEvent.Set();

    public void StartListening(Action activationRequested)
    {
        if (!IsPrimaryInstance || registeredWait is not null) return;
        registeredWait = ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            (_, timedOut) => { if (!timedOut) activationRequested(); },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        registeredWait?.Unregister(null);
        activationEvent.Dispose();
        if (IsPrimaryInstance)
        {
            try { mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* Ownership was already released during teardown. */ }
        }
        mutex.Dispose();
    }
}
