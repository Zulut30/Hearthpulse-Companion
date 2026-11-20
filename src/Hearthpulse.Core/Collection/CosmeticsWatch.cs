namespace Hearthpulse.Core.Collection;

/// <summary>
/// When the card backs and coins are read and where they are kept (docs/collection-cosmetics.md): the rules of
/// <see cref="ClientReadWatch{T}"/>, the file of <see cref="CosmeticsStore"/>.
/// </summary>
internal sealed class CosmeticsWatch(CosmeticsStore? store) : ClientReadWatch<CosmeticsCollection>
{
    protected override string What => "cosmetics";

    protected override CosmeticsCollection? Load() => store?.Load();

    protected override void Save(CosmeticsCollection value) => store?.Save(value);

    protected override void RemoveLeftovers() => store?.RemoveLeftovers();

    protected override CosmeticsCollection Confirmed(CosmeticsCollection value, bool confirmed) =>
        value.Confirmed == confirmed ? value : value with { Confirmed = confirmed };

    protected override bool IsConfirmed(CosmeticsCollection value) => value.Confirmed;

    protected override DateTime ReadAt(CosmeticsCollection value) => value.ReadAt;

    protected override string Describe(CosmeticsCollection value) =>
        $"{value.CardBacks.Count} card backs ({value.OwnedCardBacks.Count} owned), {value.Coins.Count} coins ({value.OwnedCoins.Count} owned)";
}
