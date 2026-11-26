using System.Xml.Linq;

namespace Hearthpulse.Tests;

/// <summary>
/// TASK-2026-11-26 AC7 (QA Q2, review M1) in the markup of both apps: every rating and rank block keeps its one reason
/// line for reading the client off (<c>RatingOffReason</c>, <c>RankOffReason</c>) and draws its empty state from the view
/// model, never the fixed "play rated games". A WPF binding with a wrong path fails silently; this pins the paths.
/// </summary>
public sealed class GameMemoryMarkupTests
{
    [Theory]
    [InlineData("src/Hearthpulse.App/Main/Pages/OverviewPage.xaml", "RatingChart", "{Binding BgRatingEmpty}", "{Binding RatingOffReason}")]
    [InlineData("src/Hearthpulse.UI/Main/Pages/OverviewPage.axaml", "RatingChart", "{Binding BgRatingEmpty}", "{Binding RatingOffReason}")]
    [InlineData("src/Hearthpulse.App/Main/Stats/BattlegroundsStatsView.xaml", "StatsLineChart", "{Binding BgRatingEmpty}", "{Binding RatingOffReason}")]
    [InlineData("src/Hearthpulse.UI/Main/Stats/BattlegroundsStatsView.axaml", "StatsLineChart", "{Binding BgRatingEmpty}", "{Binding RatingOffReason}")]
    [InlineData("src/Hearthpulse.App/Main/Stats/ConstructedStatsView.xaml", "StatsLineChart", "{Binding CtRankEmpty}", "{Binding RankOffReason}")]
    [InlineData("src/Hearthpulse.UI/Main/Stats/ConstructedStatsView.axaml", "StatsLineChart", "{Binding CtRankEmpty}", "{Binding RankOffReason}")]
    public void A_rating_or_rank_block_keeps_its_reason_line_and_takes_its_empty_text_from_the_model(string file, string chart, string emptyText, string reason)
    {
        var elements = XDocument.Load(Path.Combine(RepoFiles.Root, file)).Root!.Descendants().ToList();

        Assert.Contains(elements, e => e.Name.LocalName == "TextBlock" && (string?)e.Attribute("Text") == reason);
        Assert.Contains(elements, e => e.Name.LocalName == chart && (string?)e.Attribute("EmptyText") == emptyText);

        // No block of the file falls back on the fixed texts that ask for more games whatever the switch says.
        Assert.DoesNotContain(elements, e => e.Attributes().Any(a => a.Value is "{i:Loc Stats.Bg.RatingEmpty}" or "{i:Loc Stats.Ct.RankEmpty}"));
    }
}
