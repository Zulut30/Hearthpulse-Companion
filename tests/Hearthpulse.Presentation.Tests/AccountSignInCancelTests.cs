using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Text;
using Hearthpulse.Core;
using Hearthpulse.Core.Accounts;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// Cancel during a sign-in, in both apps' view models: the async command passed as an Action must end in the idle card
/// with "Sign-in cancelled." and no code, not in an exception that reaches the dispatcher's crash window. Cancel comes
/// while the site is still asked for a code and while the app waits for the player to confirm it.
/// </summary>
public sealed class AccountSignInCancelTests
{
    private const string Base = "https://site.test/api/v1/";

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Cancel_during_sign_in_leaves_the_card_idle_with_the_cancelled_message(bool wpf, bool whileAskingForCode)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var site = new HeldSite(holdCode: whileAskingForCode);
        var polling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var account = new AccountService(new AccountClient(new HttpClient(site), Base),
            new TokenStore(Path.Combine(AppPaths.Root, $"account-cancel-{Guid.NewGuid():N}.dat")), _ => true, new AccountActivity(null))
        {
            // Waiting for the player: the delay before each poll lasts until Cancel.
            Delay = (_, token) =>
            {
                polling.TrySetResult();
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
        };
        var history = new HistoryStore(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));
        dynamic model = wpf ? new WpfMain(history, new AppSettings(), TestApp.Data) : new AvaMain(history, new AppSettings(), TestApp.Data);
        model.AttachAccount(account);

        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ((INotifyPropertyChanged)model).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "AccountWaiting" && !(bool)model.AccountWaiting)
            {
                idle.TrySetResult();
            }
        };

        model.SignIn.Execute(null);
        Assert.True((bool)model.AccountWaiting);
        // The backstop only bounds a hang; the signal decides.
        await (whileAskingForCode ? site.Asked : polling.Task).WaitAsync(TimeSpan.FromSeconds(30));

        model.CancelSignIn.Execute(null);
        await idle.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False((bool)model.AccountWaiting);
        Assert.True((bool)model.AccountSignedOut);
        Assert.False((bool)model.AccountHasCode);
        Assert.Equal(Loc.T("Core.Account.Error.Cancelled"), (string)model.AccountMessage);
        Assert.False(account.SignedIn);
        // Cancelled before any token: the site was never polled.
        Assert.Equal(0, site.Polls);
    }

    /// <summary>hearthpulse.net's device flow: a code (or a request held until it is cancelled); a poll is only counted.</summary>
    private sealed class HeldSite(bool holdCode) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _polls;

        public Task Asked => _asked.Task;

        public int Polls => Volatile.Read(ref _polls);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/oauth/device/code", StringComparison.Ordinal))
            {
                _asked.TrySetResult();
                if (holdCode)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return Json("""
                    {"device_code": "dev-1", "user_code": "ABCD-EFGH", "verification_uri": "https://site.test/connect",
                     "expires_in": 600, "interval": 5}
                    """);
            }

            Interlocked.Increment(ref _polls);
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error": "authorization_pending"}""", Encoding.UTF8, "application/json") };
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
