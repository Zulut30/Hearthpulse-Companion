using System.Text.Json.Nodes;
using Hearthpulse.Core.GameStats;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Updates;

namespace Hearthpulse.Tests;

/// <summary>
/// The network clients tell the caller's own cancellation (the app closing, a loop stopped) from a timeout: the first
/// goes up to the caller as <see cref="OperationCanceledException"/>, never logged or counted as an offline pool or a
/// failed check; a timeout (a token of the client's own, or the transport's) stays the ordinary failure it was.
/// </summary>
public sealed class CancellationTests
{
    private const string Endpoint = "https://pool.test";

    [Fact]
    public async Task Game_stats_stopped_by_the_caller_throw_and_a_timeout_is_a_later()
    {
        var records = new[] { new JsonObject { ["id"] = new string('a', 32), ["mode"] = "ranked", ["res"] = "W" } };
        var caller = new Cancelling();
        using var stopped = new HttpClient(caller);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GameStatsClient.SubmitAsync(stopped, Endpoint, new string('1', 32), records, caller.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GameStatsClient.ForgetAsync(stopped, Endpoint, new string('1', 32), caller.Token));

        using var slow = new HttpClient(new TimingOut());
        Assert.Equal(GameStatsAnswer.Later, await GameStatsClient.SubmitAsync(slow, Endpoint, new string('1', 32), records));
        Assert.False(await GameStatsClient.ForgetAsync(slow, Endpoint, new string('1', 32)));
    }

    [Fact]
    public async Task Pool_stats_stopped_by_the_caller_throw_and_a_timeout_is_no_stats()
    {
        var caller = new Cancelling();
        using var stopped = new HttpClient(caller);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PoolClient.LoadStatsAsync(stopped, Endpoint, caller.Token));
        var metaCaller = new Cancelling();
        using var stoppedMeta = new HttpClient(metaCaller);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PoolClient.LoadMetaAsync(stoppedMeta, Endpoint, metaCaller.Token));

        using var slow = new HttpClient(new TimingOut());
        Assert.Null(await PoolClient.LoadStatsAsync(slow, Endpoint));
        Assert.Null(await PoolClient.LoadMetaAsync(slow, Endpoint));
    }

    [Fact]
    public async Task An_update_check_stopped_by_the_caller_throws_and_a_timeout_is_a_failed_check()
    {
        var caller = new Cancelling();
        using var stopped = new HttpClient(caller);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UpdateService.CheckForUpdateAsync(stopped, UpdateTarget.WindowsInstalled, UpdateTests.Context(), caller.Token));

        using var slow = new HttpClient(new TimingOut());
        var check = await UpdateService.CheckForUpdateAsync(slow, UpdateTarget.WindowsInstalled, UpdateTests.Context(), CancellationToken.None);
        Assert.Equal(UpdateCheckOutcome.Failed, check.Outcome);
    }

    /// <summary>The caller gives up while the request is on its way: its token is cancelled from inside the request.</summary>
    private sealed class Cancelling : HttpMessageHandler
    {
        private readonly CancellationTokenSource _stop = new();

        /// <summary>The caller's token, cancelled by the first request.</summary>
        public CancellationToken Token => _stop.Token;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _stop.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("the request token is not linked to the caller's");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stop.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>A request that ran out of time: cancelled, but not by the caller.</summary>
    private sealed class TimingOut : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("the request took too long");
    }
}
