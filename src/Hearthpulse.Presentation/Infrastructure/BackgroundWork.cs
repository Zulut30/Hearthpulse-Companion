#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Infrastructure;
#else
namespace Hearthpulse.UI.Infrastructure;
#endif

/// <summary>
/// Where a view model's work off the UI thread runs. The app uses <see cref="Pool"/>. A test gives <see cref="Inline"/>,
/// so a page is built before the call that asked for it returns: its result then depends neither on the clock nor on
/// a free thread of the pool the whole test run shares.
/// </summary>
internal abstract class BackgroundWork
{
    /// <summary>A thread of the thread pool (<see cref="Task.Run(Action)"/>).</summary>
    public static BackgroundWork Pool { get; } = new Pooled();

    /// <summary>The calling thread, at once: the returned task is already complete (or failed).</summary>
    public static BackgroundWork Inline { get; } = new OnCaller();

    public abstract Task<T> RunAsync<T>(Func<T> work);

    private sealed class Pooled : BackgroundWork
    {
        public override Task<T> RunAsync<T>(Func<T> work) => Task.Run(work);
    }

    private sealed class OnCaller : BackgroundWork
    {
        public override Task<T> RunAsync<T>(Func<T> work)
        {
            try
            {
                return Task.FromResult(work());
            }
            catch (Exception ex)
            {
                // As Task.Run does: the failure is the task's, for the caller's await to throw.
                return Task.FromException<T>(ex);
            }
        }
    }
}
