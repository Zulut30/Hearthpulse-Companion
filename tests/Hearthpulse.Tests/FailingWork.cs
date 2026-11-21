using Hearthpulse.UI.Infrastructure;

namespace Hearthpulse.Tests;

/// <summary>
/// A view model's work off the UI thread, run at once on the caller; the work whose result type is named <c>failing</c>
/// (a page's own result) throws while <see cref="Fail"/> is set, as a failed history read or count would.
/// </summary>
internal sealed class FailingWork(string failing) : BackgroundWork
{
    public bool Fail { get; set; }

    public override Task<T> RunAsync<T>(Func<T> work) =>
        Fail && typeof(T).Name == failing ? Task.FromException<T>(new InvalidOperationException("made-up failure")) : Inline.RunAsync(work);
}
