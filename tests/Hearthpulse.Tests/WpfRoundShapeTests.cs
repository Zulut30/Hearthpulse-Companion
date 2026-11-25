using System.Globalization;
using System.Xml.Linq;

namespace Hearthpulse.Tests;

/// <summary>
/// WPF draws a <c>Border</c> whose corner radii do not fit a side by scaling them per axis: <c>RadiusPill</c> (999) on a
/// wide row became an ellipse across the row, and a 12 on an 18 high row got 12x9 ends (TASK-2026-11-25-polish S6, H1).
/// Avalonia scales the radii together and keeps a capsule, so only the WPF markup (themes included) is read here.
/// A capsule whose size is not fixed takes <c>c:RoundClip.Pill</c> (corner radius and clip follow half the shorter side).
/// </summary>
public sealed class WpfRoundShapeTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Dictionary<string, double> NamedRadii = new(StringComparer.Ordinal)
    {
        ["RadiusSmall"] = 4, ["RadiusMedium"] = 8, ["RadiusLarge"] = 12, ["RadiusPill"] = 999,
    };

    /// <summary>Borders allowed to break a rule, as "file:x:Name" with the reason. None now.</summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal);

    [Fact]
    public void Pill_radius_is_only_on_square_borders_or_uses_round_clip_pill()
    {
        var markup = Markup();
        var styles = Styles(markup);
        var bad = new List<string>();
        foreach (var (path, root) in markup)
        {
            foreach (var border in root.Descendants().Where(e => e.Name.LocalName == "Border"))
            {
                var props = Effective(border, styles);
                if (Radius(props.GetValueOrDefault("CornerRadius")) is not { Max: >= 999 } || IsPill(props) || Allowed.ContainsKey(Key(path, border)))
                {
                    continue;
                }

                if (Sizes(border, props, styles).All(s => s.Width is { } w && w == s.Height))
                {
                    continue;
                }

                bad.Add(Where(path, border));
            }
        }

        Assert.True(bad.Count == 0, "CornerRadius 999 / RadiusPill on a Border that is not square: WPF draws an ellipse. " +
            "Use c:RoundClip.Pill=\"True\":" + Environment.NewLine + string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void Corner_radii_fit_the_fixed_sides_they_round()
    {
        var markup = Markup();
        var styles = Styles(markup);
        var bad = new List<string>();
        foreach (var (path, root) in markup)
        {
            foreach (var border in root.Descendants().Where(e => e.Name.LocalName == "Border"))
            {
                var props = Effective(border, styles);
                if (Radius(props.GetValueOrDefault("CornerRadius")) is not { Max: > 0 and < 999 } r || IsPill(props) || Allowed.ContainsKey(Key(path, border)))
                {
                    continue;
                }

                foreach (var (width, height) in Sizes(border, props, styles))
                {
                    // WPF scales the two radii of a side when they add up to more than the side's length.
                    var tooWide = width is { } w && (r.TopLeft + r.TopRight > w + 0.01 || r.BottomLeft + r.BottomRight > w + 0.01);
                    var tooHigh = height is { } h && (r.TopLeft + r.BottomLeft > h + 0.01 || r.TopRight + r.BottomRight > h + 0.01);
                    if (tooWide || tooHigh)
                    {
                        bad.Add($"{Where(path, border)} (CornerRadius {props["CornerRadius"]} on {width?.ToString(CultureInfo.InvariantCulture) ?? "*"}x{height?.ToString(CultureInfo.InvariantCulture) ?? "*"})");
                        break;
                    }
                }
            }
        }

        Assert.True(bad.Count == 0, "Corner radii larger than half a fixed side: WPF squashes them into elliptic corners. " +
            "Use half the side, or c:RoundClip.Pill=\"True\" for a capsule:" + Environment.NewLine + string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void Images_in_rounded_borders_are_not_clipped_square()
    {
        var bad = Markup()
            .SelectMany(f => f.Root.Descendants()
                .Where(e => e.Name.LocalName == "Border" && (string?)e.Attribute("ClipToBounds") == "True"
                            && Radius((string?)e.Attribute("CornerRadius")) is { Max: > 0 }
                            && e.Descendants().Any(d => d.Name.LocalName == "Image"))
                .Select(e => Where(f.Path, e)))
            .ToList();
        Assert.True(bad.Count == 0, "ClipToBounds on a rounded WPF Border clips its image to the square box (the art's corners show). " +
            "Use c:RoundClip.Radius:" + Environment.NewLine + string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void The_scan_catches_the_shapes_it_is_for()
    {
        var root = XElement.Parse("""
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                  xmlns:c="clr-namespace:Hearthpulse.App.Controls">
              <Grid.Resources>
                <Style x:Key="Badge" TargetType="Border"><Setter Property="CornerRadius" Value="9" /><Setter Property="Height" Value="18" /></Style>
                <Style x:Key="Round"><Setter Property="Control.Template"><Setter.Value><ControlTemplate><Border CornerRadius="999" /></ControlTemplate></Setter.Value></Setter></Style>
                <Style x:Key="Icon" TargetType="Button"><Setter Property="Width" Value="30" /><Setter Property="Height" Value="30" /><Setter Property="FocusVisualStyle" Value="{StaticResource Round}" /></Style>
              </Grid.Resources>
              <Border x:Name="Ellipse" CornerRadius="{StaticResource RadiusPill}" />
              <Border x:Name="Circle" Width="24" Height="24" CornerRadius="999" />
              <Border x:Name="Capsule" CornerRadius="999" c:RoundClip.Pill="True" />
              <Border x:Name="Squashed" Style="{StaticResource Badge}" Height="16" />
              <Border x:Name="Strip" Width="4" CornerRadius="7,0,0,7" />
              <Border x:Name="Fits" Style="{StaticResource Badge}" />
              <Border x:Name="Square" CornerRadius="8" ClipToBounds="True"><Image /></Border>
            </Grid>
            """, LoadOptions.SetLineInfo);
        var markup = new List<(string, XElement)> { ("sample", root) };
        var styles = Styles(markup);
        string Name(XElement e) => (string)e.Attribute(X + "Name")!;
        var named = root.Descendants().Where(e => e.Name.LocalName == "Border" && e.Attribute(X + "Name") is not null).ToDictionary(Name);

        bool Pill(string name)
        {
            var props = Effective(named[name], styles);
            return !IsPill(props) && Sizes(named[name], props, styles).Any(s => s.Width is not { } w || w != s.Height);
        }

        Assert.True(Pill("Ellipse"));
        Assert.False(Pill("Circle"));
        Assert.True(IsPill(Effective(named["Capsule"], styles)));
        Assert.Equal(9, Radius(Effective(named["Squashed"], styles)["CornerRadius"])!.Value.Max);
        Assert.Equal("16", Effective(named["Squashed"], styles)["Height"]);
        Assert.Equal("18", Effective(named["Fits"], styles)["Height"]);
        var template = root.Descendants().Single(e => e.Name.LocalName == "Border" && e.Parent!.Name.LocalName == "ControlTemplate");
        Assert.All(Sizes(template, Effective(template, styles), styles), s => Assert.Equal((30.0, 30.0), (s.Width!.Value, s.Height!.Value)));
    }

    private static bool IsPill(Dictionary<string, string> props) =>
        props.Any(p => p.Key.EndsWith("RoundClip.Pill", StringComparison.Ordinal) && p.Value == "True");

    /// <summary>The border's attributes over the setters of its keyed style (and that style's BasedOn chain).</summary>
    private static Dictionary<string, string> Effective(XElement border, Dictionary<string, XElement> styles)
    {
        var props = Setters(StyleOf(border, styles), styles);
        foreach (var a in border.Attributes())
        {
            props[a.Name.Namespace == X ? "x:" + a.Name.LocalName : Prefixed(border, a)] = a.Value;
        }

        return props;
    }

    private static string Prefixed(XElement e, XAttribute a)
    {
        if (a.Name.Namespace == XNamespace.None)
        {
            return a.Name.LocalName;
        }

        return (e.GetPrefixOfNamespace(a.Name.Namespace) ?? "") + ":" + a.Name.LocalName;
    }

    private static XElement? StyleOf(XElement e, Dictionary<string, XElement> styles) =>
        ResourceKey((string?)e.Attribute("Style")) is { } key ? styles.GetValueOrDefault(key) : null;

    private static Dictionary<string, string> Setters(XElement? style, Dictionary<string, XElement> styles)
    {
        var props = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var depth = 0; style is not null && depth < 8; depth++)
        {
            foreach (var setter in style.Elements().Where(s => s.Name.LocalName == "Setter"))
            {
                if ((string?)setter.Attribute("Property") is { } property && (string?)setter.Attribute("Value") is { } value)
                {
                    props.TryAdd(property, value);
                }
            }

            style = ResourceKey((string?)style.Attribute("BasedOn")) is { } based ? styles.GetValueOrDefault(based) : null;
        }

        return props;
    }

    /// <summary>
    /// The sizes the border is drawn at, as far as the markup fixes them: its own, or for a border in a control template
    /// the styled control's (the template root fills it). A focus visual style takes the size of every style or element
    /// that names it. Unknown sides are null.
    /// </summary>
    private static List<(double? Width, double? Height)> Sizes(XElement border, Dictionary<string, string> props, Dictionary<string, XElement> styles)
    {
        var own = (Width: Number(props.GetValueOrDefault("Width")), Height: Number(props.GetValueOrDefault("Height")));
        var owner = border.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Style");
        if (own.Width is not null && own.Height is not null || owner is null || !border.Ancestors().Any(a => a.Name.LocalName == "ControlTemplate"))
        {
            return [own];
        }

        var sizes = new List<(double?, double?)>();
        var ownerProps = Setters(owner, styles);
        if (ownerProps.ContainsKey("Width") || ownerProps.ContainsKey("Height"))
        {
            sizes.Add((own.Width ?? Number(ownerProps.GetValueOrDefault("Width")), own.Height ?? Number(ownerProps.GetValueOrDefault("Height"))));
        }

        // A focus visual: the focused controls' sizes.
        if ((string?)owner.Attribute(X + "Key") is { } key)
        {
            var reference = "{StaticResource " + key + "}";
            foreach (var user in styles.Values.Distinct().Where(s => s.Elements().Any(e => (string?)e.Attribute("Property") == "FocusVisualStyle" && (string?)e.Attribute("Value") == reference)))
            {
                var p = Setters(user, styles);
                sizes.Add((Number(p.GetValueOrDefault("Width")), Number(p.GetValueOrDefault("Height"))));
            }
        }

        return sizes.Count == 0 ? [own] : sizes;
    }

    private static (double TopLeft, double TopRight, double BottomRight, double BottomLeft, double Max)? Radius(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (ResourceKey(value) is { } key)
        {
            return NamedRadii.TryGetValue(key, out var named) ? (named, named, named, named, named) : null;
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        var numbers = parts.Select(Number).ToArray();
        if (numbers.Any(n => n is null))
        {
            return null;
        }

        var v = numbers.Select(n => n!.Value).ToArray();
        return v.Length switch
        {
            1 => (v[0], v[0], v[0], v[0], v[0]),
            4 => (v[0], v[1], v[2], v[3], v.Max()),
            _ => null,
        };
    }

    private static double? Number(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string? ResourceKey(string? value) =>
        value is not null && (value.StartsWith("{StaticResource ", StringComparison.Ordinal) || value.StartsWith("{DynamicResource ", StringComparison.Ordinal))
            ? value[(value.IndexOf(' ', StringComparison.Ordinal) + 1)..^1].Trim()
            : null;

    /// <summary>Keyed styles of all the markup; a key defined twice keeps its first definition.</summary>
    private static Dictionary<string, XElement> Styles(List<(string Path, XElement Root)> markup)
    {
        var styles = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var style in markup.SelectMany(f => f.Root.DescendantsAndSelf()).Where(e => e.Name.LocalName == "Style"))
        {
            if ((string?)style.Attribute(X + "Key") is { } key)
            {
                styles.TryAdd(key, style);
            }
        }

        return styles;
    }

    private static string Key(string path, XElement e) => $"{path}:{(string?)e.Attribute(X + "Name")}";

    private static string Where(string path, XElement e) => $"{path}:{((System.Xml.IXmlLineInfo)e).LineNumber} {(string?)e.Attribute(X + "Name")}";

    /// <summary>Every WPF markup file, themes included, parsed with line numbers.</summary>
    private static List<(string Path, XElement Root)> Markup()
    {
        var root = RepoFiles.Root;
        var files = Directory.EnumerateFiles(Path.Combine(root, "src/Hearthpulse.App"), "*.xaml", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal))
            .ToList();
        Assert.Contains("src/Hearthpulse.App/Overlay/OverlayLook.xaml", files);
        Assert.Contains("src/Hearthpulse.App/Themes/Theme.xaml", files);
        return files.Select(f => (f, XDocument.Load(Path.Combine(root, f), LoadOptions.SetLineInfo).Root!)).ToList();
    }
}
