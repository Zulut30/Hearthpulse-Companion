using Hearthpulse.Core.Collection;

namespace Hearthpulse.Core.Progression;

/// <summary>
/// When the achievements are read and where they are kept (docs/collection-cosmetics.md): the rules of
/// <see cref="ClientReadWatch{T}"/> and the file of <see cref="AchievementsStore"/>. The catalog is read once per session
/// of the client (<see cref="SessionCatalog"/>); later reads take only the player's states. Each read is joined with
/// the book known before it into the chains' gains (<see cref="AchievementBook.WithGainsSince"/>).
/// </summary>
internal sealed class AchievementsWatch(AchievementsStore? store) : ClientReadWatch<AchievementBook>
{
    /// <summary>
    /// The catalog read from the client of this session, for a read of states only; null before the session's first read,
    /// which then reads the catalog too.
    /// </summary>
    public AchievementBook? SessionCatalog => Current is { Confirmed: true } book ? book : null;

    protected override string What => "achievements";

    protected override AchievementBook? Load() => store?.Load();

    protected override void Save(AchievementBook value) => store?.Save(value);

    protected override void RemoveLeftovers() => store?.RemoveLeftovers();

    protected override AchievementBook Confirmed(AchievementBook value, bool confirmed) =>
        value.Confirmed == confirmed ? value : value with { Confirmed = confirmed };

    protected override bool IsConfirmed(AchievementBook value) => value.Confirmed;

    protected override DateTime ReadAt(AchievementBook value) => value.ReadAt;

    protected override string Describe(AchievementBook value) =>
        $"{value.Achievements.Count} achievements, {value.States.Count} states, {value.Summary.Completed} completed";

    protected override AchievementBook Merge(AchievementBook read, AchievementBook? known)
    {
        if (known is not null && read.LostTiersSince(known))
        {
            Diag.Info("Achievements read from the client have fewer tiers than the known ones (another account or region): gains start again");
        }

        return read.WithGainsSince(known);
    }
}
