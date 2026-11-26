using Hearthpulse.Core.Localization;

namespace Hearthpulse.Core.CardPool;

/// <summary>
/// The pool panel's texts in the interface language, made when they are shown: the view keeps the English labels and
/// tags (matched and compared), so a language switch only needs the panel rebuilt.
/// </summary>
public static class PoolDisplay
{
    /// <summary>A histogram's title ("Cost", "Attack", "Health").</summary>
    public static string Stat(string title) => Loc.Or("Pool.Stat." + title, title);

    /// <summary>A mechanic's label (<see cref="PoolKeywords.Shown"/>) by its tag.</summary>
    public static PoolMechanic Mechanic(PoolMechanic mechanic) =>
        mechanic with { Label = Loc.Or("Pool.Mechanic." + mechanic.Tag.TrimStart('#'), mechanic.Label) };

    /// <summary>Why the featured card is highlighted (<see cref="CardPoolView.FeaturedReason"/>).</summary>
    public static string FeaturedReason(string? reason) => reason is null ? string.Empty : Loc.T("Pool.FeaturedReason");
}
