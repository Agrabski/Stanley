using Avalonia.Threading;

namespace Stanley.App.Documents;

/// <summary>Runs an action once after a delay, on the UI thread; disposing the result cancels it. Behind an interface so autosave/recovery timing is testable without real time passing.</summary>
public interface IDelayScheduler
{
    IDisposable Schedule(TimeSpan delay, Action action);
}

public sealed class DispatcherDelayScheduler : IDelayScheduler
{
    public IDisposable Schedule(TimeSpan delay, Action action) => DispatcherTimer.RunOnce(action, delay);
}
