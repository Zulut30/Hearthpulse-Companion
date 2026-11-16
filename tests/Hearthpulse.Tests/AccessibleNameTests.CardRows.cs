using System.Xml.Linq;

namespace Hearthpulse.Tests;

/// <summary>
/// The card rows that open the card page (docs/card-page.md) in the markup of both UIs: each is a button, so the
/// keyboard reaches it and Enter or Space opens the page (WCAG 2.1.1), and each hands the card's name to
/// <c>CardLink.CardName</c>, which names the button "Open the card page of ..." (4.1.2). The same rows in both UIs.
/// </summary>
public sealed partial class AccessibleNameTests
{
    [Theory]
    [InlineData("src/Hearthpulse.App", "{StaticResource CardRowButton}", null)]
    [InlineData("src/Hearthpulse.UI", null, "bare")]
    public void Every_card_row_that_opens_the_card_page_is_a_named_button(string folder, string? style, string? classes)
    {
        var rows = CardRows(folder);

        // The deck list, the version differences, the "Your mulligan" and "Card results" tables, the meta's mulligan guide.
        Assert.Equal(["DeckDetailsView:4", "ConstructedMetaView:1"], rows.GroupBy(r => r.File).Select(g => $"{g.Key}:{g.Count()}"));
        foreach (var (file, row) in rows)
        {
            var where = $"{file}:{((System.Xml.IXmlLineInfo)row).LineNumber}";
            Assert.True(row.Name.LocalName == "Button", $"{where}: a card row that opens the page must be a Button, not {row.Name.LocalName}");
            Assert.Equal("{Binding CardId}", Attribute(row, "CardLink.CardId"));
            Assert.True(Attribute(row, "CardLink.CardName") == "{Binding Name}", $"{where}: no CardLink.CardName, so the button has no name");
            Assert.Equal(style, (string?)row.Attribute("Style"));
            Assert.Equal(classes, (string?)row.Attribute("Classes"));
            // Nothing takes the row out of the keyboard's reach or hides where the focus is.
            Assert.Null(row.Attribute("Focusable"));
            Assert.Null(row.Attribute("IsTabStop"));
            Assert.Null(row.Attribute("FocusVisualStyle"));
            Assert.Null(row.Attribute("FocusAdorner"));
            // The hover preview of the deck page's rows stays on the row inside the button.
            if (file == "DeckDetailsView")
            {
                Assert.Contains(row.Descendants(), e => Attribute(e, "CardPreview.CardId") == "{Binding CardId}");
            }
        }
    }

    [Fact]
    public void Both_apps_link_the_same_card_rows()
    {
        // The row model each linked template is for: "DeckDetailsView DeckListRow", ...
        static IEnumerable<string> Shape(string folder) => CardRows(folder)
            .Select(r => r.File + " " + System.Text.RegularExpressions.Regex.Match(Attribute(r.Row.Parent!, "DataType") ?? string.Empty, @"(\w+)\}?$").Groups[1].Value);

        Assert.Equal(Shape("src/Hearthpulse.App").Order(StringComparer.Ordinal), Shape("src/Hearthpulse.UI").Order(StringComparer.Ordinal));
    }

    private static List<(string File, XElement Row)> CardRows(string folder)
    {
        var root = Path.Combine(RepoFiles.Root, folder);
        return Directory.EnumerateFiles(Path.Combine(root, "Main"), "*.*xaml", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xaml", StringComparison.Ordinal) || f.EndsWith(".axaml", StringComparison.Ordinal))
            .OrderByDescending(f => Path.GetFileName(f).StartsWith("DeckDetailsView", StringComparison.Ordinal))
            .SelectMany(f => XDocument.Load(f, LoadOptions.SetLineInfo).Descendants()
                .Where(e => Attribute(e, "CardLink.CardId") is not null)
                .Select(e => (Path.GetFileNameWithoutExtension(f), e)))
            .ToList();
    }

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value;
}
