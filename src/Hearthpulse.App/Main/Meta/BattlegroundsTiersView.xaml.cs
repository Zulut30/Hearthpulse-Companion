using System.Windows;
using System.Windows.Controls;
using Hearthpulse.App.Infrastructure;

namespace Hearthpulse.App.Main.Meta;

/// <summary>
/// The Battlegrounds tier tabs; the page's tab bar picks which one shows (<see cref="ShowTab"/>). A tab's card is built
/// from its template the first time the tab is opened, so a page opened on My stats builds none of them. The cards share
/// one cell and cross-fade (<see cref="PageSwitch"/>): the card that leaves stays drawn, without clicks, until the new
/// one has been laid out.
/// </summary>
public partial class BattlegroundsTiersView : UserControl
{
    private readonly Dictionary<string, FrameworkElement> _cards = new(StringComparer.Ordinal);

    /// <summary>Each card's list, collapsed with its card.</summary>
    private readonly Dictionary<UIElement, UIElement> _lists = [];

    private readonly PageSwitch _switch;

    public BattlegroundsTiersView()
    {
        InitializeComponent();
        // A list under a collapsed card is still laid out when its rows change (it was measured once): collapsed itself,
        // a hidden tab's list builds no rows on a refresh or a language change. It goes once its card has faded out.
        _switch = new PageSwitch(rise: Motion.TabRise, hidden: card =>
        {
            if (_lists.TryGetValue(card, out var list))
            {
                list.Visibility = Visibility.Collapsed;
            }
        });
    }

    /// <summary>The tabs whose cards exist ("Heroes", "Comps", "Trinkets", "Cards").</summary>
    public IReadOnlyCollection<string> BuiltTabs => _cards.Keys;

    /// <summary>
    /// "Heroes", "Comps", "Trinkets" or "Cards": shows that tab's card, building it on its first opening, and returns
    /// it. Any other tab (My stats) hides the cards at once, builds nothing and returns the view itself.
    /// </summary>
    public FrameworkElement ShowTab(string tab)
    {
        if (tab is not ("Heroes" or "Comps" or "Trinkets" or "Cards"))
        {
            _switch.Clear(_cards.Values);
            return this;
        }

        if (!_cards.TryGetValue(tab, out var card))
        {
            card = (FrameworkElement)((DataTemplate)FindResource(tab + "Card")).LoadContent();
            // Built hidden: the switch brings it in (held unseen while another card fades out).
            card.Visibility = Visibility.Collapsed;
            _cards[tab] = card;
            if (card.FindName(ListName(tab)) is ItemsControl built)
            {
                _lists[card] = built;
            }

            Cards.Children.Add(card);
        }

        if (_lists.TryGetValue(card, out var list))
        {
            list.Visibility = Visibility.Visible;
        }

        _switch.Show(card, _cards.Values);
        return card;
    }

    private static string ListName(string tab) => tab switch
    {
        "Heroes" => "HeroTierList",
        "Comps" => "CompTierList",
        "Trinkets" => "TrinketTierList",
        _ => "CardTierList",
    };
}
