using System.Text.RegularExpressions;

namespace Hearthpulse.Tests;

/// <summary>
/// The WPF and the Avalonia markup of the collection side by side (every user-facing feature goes into both UIs): the
/// same named parts, the same Loc keys and the same bound properties in the meta view, the deck page and the shared
/// templates; the label is a button with an accessible name and help text, and nothing is written as literal text.
/// </summary>
public sealed partial class CollectionMarkupTests
{
    private static readonly (string Wpf, string Avalonia)[] Pairs =
    [
        ("src/Hearthpulse.App/Main/Meta/ConstructedMetaView.xaml", "src/Hearthpulse.UI/Main/Meta/ConstructedMetaView.axaml"),
        ("src/Hearthpulse.App/Main/Decks/DeckDetailsView.xaml", "src/Hearthpulse.UI/Main/Decks/DeckDetailsView.axaml"),
        ("src/Hearthpulse.App/Main/CollectionResources.xaml", "src/Hearthpulse.UI/Main/CollectionResources.axaml"),
    ];

    private static string Read(string path) => File.ReadAllText(Path.Combine(RepoFiles.Root, path.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Both_markups_use_the_same_collection_texts()
    {
        foreach (var (wpf, avalonia) in Pairs)
        {
            var (w, a) = (LocKey().Matches(Read(wpf)).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList(),
                LocKey().Matches(Read(avalonia)).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList());
            Assert.Equal(w, a);
        }

        // Between them they show the filter, its empty state, the order, the missing cards' head and the block's title.
        var all = Pairs.SelectMany(p => LocKey().Matches(Read(p.Avalonia)).Select(m => m.Groups[1].Value)).ToHashSet(StringComparer.Ordinal);
        Assert.Superset(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Collection.Filter.CanBuild", "Collection.Filter.CanBuildTip", "Collection.Filter.Empty", "Collection.Sort.Label",
                "Collection.Missing.Title", "Collection.Title", "Collection.Dust.ColumnTip", "Collection.Pricing", "Collection.PricingFailed",
                "Collection.PricingRetry",
            },
            all);
    }

    [Fact]
    public void Both_markups_have_the_same_named_parts_bound_to_the_same_properties()
    {
        string[] names =
        [
            "CollectionBar", "CollectionStatusLine", "CanBuildFilter", "DeckSortPicker", "CanBuildNote", "BestListsCollection",
            "DeckCostsFailedLine", "MetaPricing",
            "DeckCollection", "DeckBuild", "DeckMissing", "DeckCollectionStatus",
        ];
        string[] bindings =
        [
            "{Binding CollectionStatus}", "{Binding CanBuildOnly, Mode=TwoWay}", "{Binding CanBuildAvailable}", "{Binding MetaDeckSorts}",
            "{Binding CanBuildNote}", "{Binding MetaCanBuildEmpty", "{Binding MetaPricingShown", "{Binding DeckCostsFailed", "{Binding RetryDeckCosts}", "{Binding Build}", "{Binding Build.Name}", "{Binding Build.Details}",
            "{Binding HasBuild", "{Binding Collection}", "{Binding Status}", "{Binding Glyph}", "{Binding Name}", "{Binding Opacity}",
            "{Binding Missing}", "{Binding Total", "{Binding Price}", "{Binding Note}", "{Binding Tip}",
        ];
        var wpf = string.Concat(Pairs.Select(p => Read(p.Wpf)));
        var avalonia = string.Concat(Pairs.Select(p => Read(p.Avalonia)));

        Assert.All(names, name => Assert.Contains($"x:Name=\"{name}\"", wpf, StringComparison.Ordinal));
        Assert.All(names, name => Assert.Contains($" Name=\"{name}\"", avalonia, StringComparison.Ordinal));
        Assert.All(bindings, binding => Assert.Contains(binding, wpf, StringComparison.Ordinal));
        Assert.All(bindings, binding => Assert.Contains(binding, avalonia, StringComparison.Ordinal));

        // The same three shared templates, under the same keys.
        foreach (var key in new[] { "DeckBuildLabel", "MissingCardTemplate", "DeckMissingList" })
        {
            Assert.Contains($"x:Key=\"{key}\"", Read(Pairs[2].Wpf), StringComparison.Ordinal);
            Assert.Contains($"x:Key=\"{key}\"", Read(Pairs[2].Avalonia), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_label_is_a_named_button_that_opens_the_missing_cards_in_both_markups()
    {
        var wpf = Read(Pairs[0].Wpf);
        var avalonia = Read(Pairs[0].Avalonia);
        var wpfLabel = Element(wpf, "ContentTemplate=\"{StaticResource DeckBuildLabel}\"");
        var avaloniaLabel = Element(avalonia, "ContentTemplate=\"{StaticResource DeckBuildLabel}\"");

        foreach (var label in new[] { wpfLabel, avaloniaLabel })
        {
            Assert.StartsWith("<Button ", label, StringComparison.Ordinal);
            Assert.Contains("AutomationProperties.Name=\"{Binding Build.Name}\"", label, StringComparison.Ordinal);
            Assert.Contains("AutomationProperties.HelpText=\"{Binding Build.Details}\"", label, StringComparison.Ordinal);
            // Reachable by Tab like any button: nothing takes it out of the tab order.
            Assert.DoesNotContain("Focusable=\"False\"", label, StringComparison.Ordinal);
            Assert.DoesNotContain("IsTabStop=\"False\"", label, StringComparison.Ordinal);
        }

        // WPF opens a popup from the click, made in code for that opening and never part of the view's markup (a
        // popup in the tree kept the closed window alive, docs/collection.md); Avalonia has the flyout on the button,
        // with focus handled on opening and closing. Both show the same template.
        Assert.Contains("Click=\"OnDeckBuild\"", wpfLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("<Popup", wpf, StringComparison.Ordinal);
        var wpfCode = Read("src/Hearthpulse.App/Main/Meta/ConstructedMetaView.Collection.cs");
        Assert.Contains("FindResource(\"DeckMissingList\")", wpfCode, StringComparison.Ordinal);
        Assert.DoesNotContain("PlacementTarget =", wpfCode, StringComparison.Ordinal);
        Assert.Contains("<Flyout ", avalonia, StringComparison.Ordinal);
        Assert.Contains("Opened=\"OnDeckBuildOpened\" Closed=\"OnDeckBuildClosed\"", avalonia, StringComparison.Ordinal);
        Assert.Equal(1, Count(avalonia, "ContentTemplate=\"{StaticResource DeckMissingList}\""));

        // The filter says why it is off limits where a disabled control would otherwise say nothing.
        Assert.Contains("ToolTipService.ShowOnDisabled=\"True\"", Element(wpf, "x:Name=\"CanBuildFilter\""), StringComparison.Ordinal);
        Assert.Contains("ToolTip.ShowOnDisabled=\"True\"", Element(avalonia, " Name=\"CanBuildFilter\""), StringComparison.Ordinal);
    }

    [Fact]
    public void No_view_shows_the_split_of_copies_by_finish()
    {
        // The reader's plain/golden split is an assumption (docs/collection.md): the views bind none of it.
        foreach (var path in Pairs.SelectMany(p => new[] { p.Wpf, p.Avalonia }))
        {
            var text = Read(path);
            Assert.DoesNotContain("Golden", text, StringComparison.Ordinal);
            Assert.DoesNotContain("OwnedCopies", text, StringComparison.Ordinal);
        }

        foreach (var file in new[] { "src/Hearthpulse.Presentation/Main/CollectionRows.cs", "src/Hearthpulse.Presentation/Main/MainViewModel.Collection.cs" })
        {
            var code = Read(file);
            Assert.DoesNotMatch(Finish(), code);
        }
    }

    /// <summary>The opening tag that holds <paramref name="marker"/>, from its "&lt;" to its "&gt;".</summary>
    private static string Element(string markup, string marker)
    {
        var at = markup.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, marker);
        var start = markup.LastIndexOf('<', at);
        return markup[start..(markup.IndexOf('>', at) + 1)];
    }

    private static int Count(string text, string part) => text.Split(part).Length - 1;

    /// <summary>A read of one finish of <c>OwnedCopies</c> (its Total is the only number a view may use).</summary>
    [GeneratedRegex(@"\.(Normal|Golden|Other)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Finish();

    [GeneratedRegex(@"\{i:Loc (Collection\.[A-Za-z.]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex LocKey();
}
