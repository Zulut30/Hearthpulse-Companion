using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.UI.Infrastructure;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// Where a view model's work off the UI thread runs (<see cref="BackgroundWork"/>): the app keeps the thread pool, a test
/// that builds a page takes the calling thread, so it waits for nothing.
/// </summary>
public sealed class BackgroundWorkTests
{
    [Fact]
    public void A_view_model_builds_its_pages_on_the_thread_pool_unless_told_otherwise()
    {
        var vm = new MainViewModel(HistoryStore.InMemory(), new AppSettings(), TestDataFolder.App);

        Assert.Same(BackgroundWork.Pool, vm.Background);
    }

    [Fact]
    public async Task Inline_work_is_done_on_the_calling_thread_before_the_call_returns()
    {
        var caller = Environment.CurrentManagedThreadId;

        var work = BackgroundWork.Inline.RunAsync(() => Environment.CurrentManagedThreadId);

        Assert.True(work.IsCompletedSuccessfully);
        Assert.Equal(caller, await work);
    }

    [Fact]
    public void Inline_work_that_throws_fails_its_task_as_pool_work_does()
    {
        var work = BackgroundWork.Inline.RunAsync<int>(() => throw new InvalidOperationException("built wrong"));

        Assert.True(work.IsFaulted);
        Assert.Equal("built wrong", Assert.IsType<InvalidOperationException>(work.Exception!.InnerException).Message);
    }
}
