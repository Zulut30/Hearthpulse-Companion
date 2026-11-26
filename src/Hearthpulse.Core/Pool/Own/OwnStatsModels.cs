namespace Hearthpulse.Core.Pool.Own;

/// <summary>
/// What a block of Hearthpulse's own numbers can say after one load of a view of the stats pool
/// (<c>GET /v2/analytics/records/&lt;view&gt;</c>, numbers computed from game records); each value is its own state.
/// </summary>
public enum OwnStatsState
{
    /// <summary>No stats pool is set up, or its address is not plain https: nothing was asked, the block is hidden.</summary>
    NotConfigured,

    /// <summary>
    /// The pool did not answer: a network error or a timeout, an error status (404, 500...), a body over 4 MB, or one
    /// that is not this view's answer. There is no older copy behind it: a copy past its age is deleted, never shown.
    /// </summary>
    Offline,

    /// <summary>The pool answered: the view has no published snapshot yet (<c>not_published</c>, or a reason this client does not know).</summary>
    NotPublished,

    /// <summary>The pool answered: the numbers were withdrawn after a deletion and the next build is pending (<c>rebuilding</c>).</summary>
    Rebuilding,

    /// <summary>
    /// The pool answered: the window or patch asked for has no numbers (<c>no_window</c>);
    /// <see cref="OwnStatsAnswer{T}.Windows"/> lists the ones it has.
    /// </summary>
    NoWindow,

    /// <summary>
    /// The pool does not serve the view for the mode asked (<c>unsupported_mode</c>), or the request is not one this
    /// client sends (a view or a parameter that is not a key) and nothing was asked: the block is hidden.
    /// </summary>
    Unsupported,

    /// <summary>
    /// Published: <see cref="OwnStatsAnswer{T}.Data"/> and <see cref="OwnStatsAnswer{T}.Period"/> are set. Single
    /// numbers in the data may still be null (too few installations or games behind them).
    /// </summary>
    Ok,
}

/// <summary>
/// A share (<c>RATE</c>): <see cref="Num"/> of <see cref="Den"/> with its Wilson interval, all three as fractions
/// 0..1. <see cref="Value"/>, <see cref="Low"/> and <see cref="High"/> are null below the minimum number of games, the
/// counts stay. <see cref="Rankable"/>: the interval is narrow enough to sort a row by this number.
/// </summary>
public sealed record OwnRate(int Num, int Den, double? Value, double? Low, double? High, bool Rankable);

/// <summary>An average (<c>MEAN</c>) of <see cref="N"/> observations with its interval; null values below the minimum.</summary>
public sealed record OwnMean(int N, double? Mean, double? Low, double? High, bool Rankable);

/// <summary>
/// Battlegrounds places (<c>PLACEMENT</c>) of <see cref="N"/> games: the mean place with its interval and the count of
/// each place (1..8 solo, 1..4 duos); the values and the distribution are null below the minimum number of games.
/// </summary>
public sealed record OwnPlacement(int N, double? Mean, double? Low, double? High, IReadOnlyList<int>? Distribution, bool Rankable);

/// <summary>
/// The games an answer is computed from: the line (<c>constructed</c>, <c>battlegrounds</c>, <c>arena</c>), the
/// balance patch (<c>unknown</c> for games of an unknown build), the game builds it holds (null when the pool names
/// none), the window (<c>patch</c>, <c>7d</c>, <c>30d</c>) and its first and last day (days since 1970-01-01, UTC).
/// </summary>
public sealed record OwnPeriod(string Line, string Patch, int? FirstBuild, int? LastBuild, string Window, int FromDay, int ToDay);

/// <summary>A window the pool publishes for the view: a patch id (or <c>unknown</c>) and <c>patch</c>, <c>7d</c> or <c>30d</c>.</summary>
public sealed record OwnWindow(string Patch, string Window);

/// <summary>
/// One load of a view. <see cref="Data"/> and <see cref="Period"/> are set exactly when <see cref="State"/> is
/// <see cref="OwnStatsState.Ok"/>; <see cref="Metric"/>, <see cref="RulesVersion"/>, <see cref="Published"/> (Unix
/// milliseconds, 0 when never), <see cref="Stale"/> and <see cref="Windows"/> are what the pool said, also when the
/// numbers are not available. <see cref="FetchedAt"/> (UTC) is when the pool gave this answer, which may be a copy
/// kept on disk for a few hours; <c>default</c> when nothing was answered (<see cref="OwnStatsState.NotConfigured"/>,
/// <see cref="OwnStatsState.Offline"/>, a request that was not sent).
/// </summary>
public sealed record OwnStatsAnswer<T>(OwnStatsState State, string View, string? Metric, string? RulesVersion, long Published,
    bool Stale, OwnPeriod? Period, IReadOnlyList<OwnWindow> Windows, T? Data, DateTime FetchedAt) where T : class;
