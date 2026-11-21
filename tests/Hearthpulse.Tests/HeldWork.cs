using Hearthpulse.UI.Infrastructure;

namespace Hearthpulse.Tests;

/// <summary>
/// A view model's work off the UI thread under the test's control: each piece of work whose result type is named
/// <c>held</c> (a page's own result, such as <c>CollectionPageData</c>) waits until the test releases it by its number,
/// in any order; the view model's other work (a history read) runs at once on the caller. Used inside
/// <see cref="LocBindingTests.Headless"/>, whose dispatcher runs a continuation the release did not run inline.
/// </summary>
internal sealed class HeldWork(string held) : BackgroundWork
{
    private readonly List<Action> _work = [];

    /// <summary>How many pieces of held work were asked for so far.</summary>
    public int Count => _work.Count;

    public override Task<T> RunAsync<T>(Func<T> work)
    {
        if (typeof(T).Name != held)
        {
            return Inline.RunAsync(work);
        }

        var done = new TaskCompletionSource<T>();
        _work.Add(() => done.SetResult(work()));
        return done.Task;
    }

    /// <summary>Finishes the piece of work asked for <paramref name="index"/>-th (from 0) and runs what waited for it.</summary>
    public void Release(int index)
    {
        _work[index]();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
}
