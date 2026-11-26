using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Xunit.Abstractions;

namespace Hearthpulse.Tests;

/// <summary>
/// Agent-ready names (skill hearthpulse-agent-ready, whose table holds the naming rules of WPF and Avalonia read here):
/// per UI, controls with no name, a fixed name twice in one view and a fixed name in a row template are counted, and the
/// counts may not grow, a ratchet like DesignTokenTests. The list is in the test output.
/// </summary>
public sealed partial class AgentReadyNameTests(ITestOutputHelper output)
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>What is left on 2026-11-26. Lower these when a fix lands; never raise them.</summary>
    private static readonly Dictionary<string, (int Unnamed, int Duplicate, int Row)> Baseline = new(StringComparer.Ordinal)
    {
        ["wpf"] = (44, 7, 17),
        ["avalonia"] = (47, 8, 17),
    };

    private static readonly string[] ContentNamed =
        ["Button", "ToggleButton", "RepeatButton", "CheckBox", "RadioButton", "ToggleSwitch", "HyperlinkButton", "SplitButton", "DropDownButton"];

    private static readonly string[] HeaderNamed = ["MenuItem", "TabItem", "Expander"];

    private static readonly string[] Inputs =
        ["TextBox", "PasswordBox", "ComboBox", "Slider", "ListBox", "NumericUpDown", "DatePicker", "CalendarDatePicker", "TimePicker", "AutoCompleteBox"];

    private static readonly string[] TextElements = ["TextBlock", "SelectableTextBlock", "AccessText", "Run", "Label"];

    private static readonly string[] RowTemplates = ["DataTemplate", "HierarchicalDataTemplate", "TreeDataTemplate"];

    private static readonly string[] Scopes = [.. RowTemplates, "ItemsPanelTemplate", "ContextMenu", "ContextFlyout", "Popup", "Flyout", "MenuFlyout", "ToolTip"];

    private static readonly string[] ItemsControls = ["ItemsControl", "ListBox", "ComboBox", "ItemsRepeater"];

    [Theory]
    [InlineData("wpf", "src/Hearthpulse.App")]
    [InlineData("avalonia", "src/Hearthpulse.UI")]
    public void Unnamed_controls_and_repeated_names_do_not_grow(string ui, string folder)
    {
        var root = Path.Combine(RepoFiles.Root, folder);
        var files = Directory.EnumerateFiles(root, "*.*xaml", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".xaml", StringComparison.Ordinal) || f.EndsWith(".axaml", StringComparison.Ordinal))
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(f => (Path.GetRelativePath(root, f).Replace('\\', '/'), XDocument.Load(f, LoadOptions.SetLineInfo).Root!))
            .ToList();
        var findings = Scan(files);
        foreach (var finding in findings)
        {
            output.WriteLine(finding.ToString());
        }

        var found = (Unnamed: Count(findings, Kind.Unnamed), Duplicate: Count(findings, Kind.Duplicate), Row: Count(findings, Kind.Row));
        var limit = Baseline[ui];
        Assert.True(found.Unnamed <= limit.Unnamed && found.Duplicate <= limit.Duplicate && found.Row <= limit.Row,
            $"{ui}: agent-ready names grew to {found} (limit {limit}). Name the new control, or make its name unique in its view and " +
            "carry the row in a row action's name (hearthpulse-agent-ready). Every finding:\n" + string.Join('\n', findings));
    }

    [Theory]
    [InlineData("View.xaml", "")]
    [InlineData("View.axaml", "expander")]
    public void The_scanner_names_controls_as_the_frameworks_do(string file, string avaloniaOnly)
    {
        var view = XElement.Parse("""
            <UserControl xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <UserControl.Resources>
                <Style x:Key="Reset" TargetType="Button"><Setter Property="Content" Value="{i:Loc Settings.Reset.Group}" /></Style>
                <DataTemplate x:Key="DeckRow">
                  <StackPanel>
                    <Button x:Name="rowFixed" Content="{i:Loc Deck.Delete}" />
                    <Button x:Name="rowBound" Content="{i:Loc Deck.Delete}" AutomationProperties.Name="{Binding DeleteName}" />
                  </StackPanel>
                </DataTemplate>
              </UserControl.Resources>
              <StackPanel>
                <Button x:Name="text" Content="{i:Loc Common.Save}" />
                <Button x:Name="textAgain" Content="{i:Loc Common.Save}" />
                <Button x:Name="block"><TextBlock Text="{i:Loc Common.Open}" /></Button>
                <Button x:Name="inline">Open</Button>
                <Button x:Name="panel"><StackPanel><TextBlock Text="&#xF0156;" /><TextBlock Text="{i:Loc Common.Delete}" /></StackPanel></Button>
                <Button x:Name="panelNamed" AutomationProperties.Name="{i:Loc Common.DeleteAll}"><StackPanel><TextBlock Text="&#xF0156;" /></StackPanel></Button>
                <Button x:Name="glyph" Content="&#xF0156;" ToolTip="Hide" />
                <Button x:Name="record" Content="{Binding}" />
                <Button x:Name="bound" Content="{Binding Title}" />
                <ToggleButton x:Name="drawn" Style="{StaticResource MoreToggle}" />
                <Button x:Name="templated"><Button.Template><ControlTemplate><TextBlock Text="{i:Loc Main.Update.Ready}" /></ControlTemplate></Button.Template></Button>
                <Button x:Name="styled" Style="{StaticResource Reset}" />
                <Button x:Name="styledNamed" Style="{StaticResource Reset}" AutomationProperties.Name="{i:Loc Reset.Overlay}" />
                <Button x:Name="card" CardLink.CardName="{Binding Name}" />
                <CheckBox x:Name="check" Content="{i:Loc Settings.Timers}" />
                <TextBox x:Name="box" PlaceholderText="Search" />
                <TextBox x:Name="labeled" AutomationProperties.LabeledBy="{Binding ElementName=NickLabel}" />
                <Slider x:Name="slider" />
                <Expander x:Name="expander" Header="{i:Loc Common.More}" />
                <TextBlock><Hyperlink x:Name="link"><Run Text="{i:Loc About.Policy}" /></Hyperlink></TextBlock>
                <ItemsControl ItemTemplate="{StaticResource DeckRow}" />
                <Border><Border.ContextMenu><ContextMenu><MenuItem x:Name="menuSave" Header="{i:Loc Common.Save}" /></ContextMenu></Border.ContextMenu></Border>
              </StackPanel>
            </UserControl>
            """, LoadOptions.SetLineInfo);

        var findings = Scan([(file, view)]);

        string[] Names(Kind kind) => [.. findings.Where(f => f.Kind == kind).Select(f => f.Name ?? f.Where)];
        Assert.Equal(
            new[] { "panel", "glyph", "record", "drawn", "templated", "box", "slider", avaloniaOnly }.Where(n => n.Length > 0),
            Names(Kind.Unnamed));
        Assert.Equal(["textAgain"], Names(Kind.Duplicate));
        Assert.Equal(["rowFixed"], Names(Kind.Row));
    }

    private enum Kind
    {
        Unnamed,
        Duplicate,
        Row,
    }

    private sealed record Finding(Kind Kind, string Where, string? Name, string Detail)
    {
        public override string ToString() => $"{Kind,-9} {Where}  {Detail}";
    }

    /// <summary>A control's name as its markup gives it: none, fixed (the same for every copy) or bound to its data.</summary>
    private readonly record struct Naming(bool Named, string? Fixed, string How);

    private static int Count(List<Finding> findings, Kind kind) => findings.Count(f => f.Kind == kind);

    private static List<Finding> Scan(IReadOnlyList<(string Path, XElement Root)> files)
    {
        var styles = StyleNames(files.Select(f => f.Root));
        var rowKeys = files.SelectMany(f => f.Root.DescendantsAndSelf().Attributes())
            .Where(a => a.Name.LocalName == "ItemTemplate")
            .Select(a => ResourceKey(a.Value))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var findings = new List<Finding>();
        foreach (var (path, root) in files)
        {
            var avalonia = path.EndsWith(".axaml", StringComparison.Ordinal);
            var seen = new Dictionary<XElement, HashSet<string>>();
            foreach (var control in root.DescendantsAndSelf().Where(Interactive))
            {
                if (control.Ancestors().Any(a => a.Name.LocalName == "ControlTemplate"))
                {
                    continue;
                }

                var naming = NameOf(control, styles, avalonia);
                var where = $"{path}:{((IXmlLineInfo)control).LineNumber} {control.Name.LocalName}";
                var name = (string?)control.Attribute(X + "Name");
                if (!naming.Named)
                {
                    findings.Add(new(Kind.Unnamed, where, name, naming.How));
                    continue;
                }

                if (naming.Fixed is null)
                {
                    continue;
                }

                var scope = control.Ancestors().FirstOrDefault(a => Scopes.Contains(a.Name.LocalName)) ?? root;
                if (Repeated(scope, rowKeys))
                {
                    findings.Add(new(Kind.Row, where, name, naming.Fixed + " in every row"));
                    continue;
                }

                if (!seen.TryGetValue(scope, out var names))
                {
                    seen[scope] = names = new HashSet<string>(StringComparer.Ordinal);
                }

                if (!names.Add(naming.Fixed))
                {
                    findings.Add(new(Kind.Duplicate, where, name, naming.Fixed + " again in this view"));
                }
            }
        }

        return findings;
    }

    private static bool Interactive(XElement element)
    {
        var tag = element.Name.LocalName;
        return ContentNamed.Contains(tag) || HeaderNamed.Contains(tag) || Inputs.Contains(tag) || tag == "Hyperlink";
    }

    /// <summary>A template every item of a list builds: an item template, inline or by key.</summary>
    private static bool Repeated(XElement scope, HashSet<string> rowKeys)
    {
        if (!RowTemplates.Contains(scope.Name.LocalName))
        {
            return false;
        }

        if ((string?)scope.Attribute(X + "Key") is { } key && rowKeys.Contains(key))
        {
            return true;
        }

        var holder = scope.Parent?.Name.LocalName ?? string.Empty;
        return holder.EndsWith(".ItemTemplate", StringComparison.Ordinal) || ItemsControls.Any(c => holder == c + ".DataTemplates");
    }

    private static Naming NameOf(XElement control, Dictionary<string, (string Property, string Value)> styles, bool avalonia)
    {
        foreach (var attribute in control.Attributes())
        {
            switch (attribute.Name.LocalName)
            {
                case "AutomationProperties.Name":
                    return Value(attribute.Value, "AutomationProperties.Name");
                case "AutomationProperties.LabeledBy":
                    return new(true, null, "LabeledBy");
                case "CardLink.CardName":
                    return new(true, null, "CardLink.CardName");
            }
        }

        if (control.Elements().Any(e => e.Name.LocalName == "AutomationProperties.Name"))
        {
            return new(true, null, "AutomationProperties.Name");
        }

        var style = ResourceKey((string?)control.Attribute("Style")) is { } key && styles.TryGetValue(key, out var setter) ? setter : default;
        if (style.Property == "AutomationProperties.Name")
        {
            return Value(style.Value, "the style's AutomationProperties.Name");
        }

        var tag = control.Name.LocalName;
        if (Inputs.Contains(tag))
        {
            return new(false, null, "an input takes its name only from AutomationProperties.Name or LabeledBy");
        }

        if (tag == "Expander" && avalonia)
        {
            return new(false, null, "an Avalonia Expander takes no name from its header");
        }

        if (tag == "Hyperlink")
        {
            return Text(control, "Hyperlink");
        }

        var property = HeaderNamed.Contains(tag) ? "Header" : "Content";
        if (control.Attribute(property) is { } given)
        {
            return Value(given.Value, property);
        }

        var holder = control.Elements().FirstOrDefault(e => e.Name.LocalName == tag + "." + property);
        var children = (holder?.Elements() ?? control.Elements().Where(e => !e.Name.LocalName.Contains('.', StringComparison.Ordinal))).ToList();
        if (children.Count == 0)
        {
            var text = string.Concat((holder ?? control).Nodes().OfType<XText>().Select(t => t.Value)).Trim();
            if (text.Length > 0)
            {
                return Value(text, "text content");
            }

            return style.Property == "Content"
                ? Value(style.Value, "the style's Content")
                : new(false, null, "no content: a label the template draws is not a name");
        }

        return children.Count == 1 && TextElements.Contains(children[0].Name.LocalName)
            ? Text(children[0], children[0].Name.LocalName)
            : new(false, null, children[0].Name.LocalName + " as content: WPF reads an empty name, Avalonia the type name");
    }

    /// <summary>The name a text element gives: its Text, or its runs and plain text together.</summary>
    private static Naming Text(XElement text, string how)
    {
        if (text.Attribute("Text") is { } attribute)
        {
            return Value(attribute.Value, how);
        }

        var parts = new List<Naming>();
        foreach (var node in text.Nodes())
        {
            var part = node switch
            {
                XText plain when plain.Value.Trim().Length > 0 => Value(plain.Value, how),
                XElement inline when !inline.Name.LocalName.Contains('.', StringComparison.Ordinal) => Text(inline, how),
                _ => default,
            };
            if (part.Named)
            {
                parts.Add(part);
            }
        }

        return parts.Count == 0
            ? new(false, null, how + " with no text")
            : new(true, parts.Any(p => p.Fixed is null) ? null : string.Join(" + ", parts.Select(p => p.Fixed)), how);
    }

    private static Naming Value(string value, string how)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return new(false, null, how + " is empty");
        }

        var markup = Markup().Match(trimmed);
        if (!markup.Success)
        {
            return IsGlyph(trimmed) ? new(false, null, how + " is an icon glyph") : new(true, trimmed, how);
        }

        var arguments = markup.Groups["arguments"].Value.Trim();
        switch (markup.Groups["kind"].Value)
        {
            case "Loc":
                return new(true, "{i:Loc " + arguments.Split(',')[0].Trim() + "}", how);
            case "Binding" or "CompiledBinding" or "ReflectionBinding" or "MultiBinding":
                var path = arguments.Split(',')[0].Trim();
                return path.Length == 0 || path is "." or "Path=." || (path.Contains('=', StringComparison.Ordinal) && !path.StartsWith("Path=", StringComparison.Ordinal))
                    ? new(false, null, how + " is the data object: its ToString() is the name")
                    : new(true, null, how);
            case "TemplateBinding":
                return new(true, null, how);
            default:
                return new(true, trimmed, how);
        }
    }

    /// <summary>Keyed styles that set a name or a text content (not through a template part).</summary>
    private static Dictionary<string, (string Property, string Value)> StyleNames(IEnumerable<XElement> roots)
    {
        var named = new Dictionary<string, (string Property, string Value)>(StringComparer.Ordinal);
        foreach (var style in roots.SelectMany(r => r.DescendantsAndSelf()).Where(e => e.Name.LocalName == "Style" && e.Attribute(X + "Key") is not null))
        {
            var setters = style.Elements().Where(s => s.Name.LocalName == "Setter" && s.Attribute("TargetName") is null && s.Attribute("Value") is not null).ToList();
            var setter = setters.FirstOrDefault(s => (string?)s.Attribute("Property") == "AutomationProperties.Name")
                         ?? setters.FirstOrDefault(s => (string?)s.Attribute("Property") == "Content");
            if (setter is not null)
            {
                named.TryAdd((string)style.Attribute(X + "Key")!, ((string)setter.Attribute("Property")!, (string)setter.Attribute("Value")!));
            }
        }

        return named;
    }

    private static string? ResourceKey(string? value) =>
        value is null ? null : Resource().Match(value) is { Success: true } match ? match.Groups["key"].Value : null;

    /// <summary>Material Design Icons glyphs sit in the private use areas.</summary>
    private static bool IsGlyph(string text)
    {
        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
        {
            var c = char.ConvertToUtf32(text, i);
            if (c is not ((>= 0xE000 and <= 0xF8FF) or >= 0xF0000))
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"^\{\s*(?:\w+:)?(?<kind>\w+)(?<arguments>.*)\}$", RegexOptions.Singleline)]
    private static partial Regex Markup();

    [GeneratedRegex(@"^\{(?:StaticResource|DynamicResource)\s+(?<key>[^}\s]+)\s*\}$")]
    private static partial Regex Resource();
}
