using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Hearthpulse.Core;

namespace Hearthpulse.App.Infrastructure;

/// <summary>
/// Builds the app's resource dictionary for a colour theme. The source of Themes/Theme.xaml ships inside the app;
/// every colour in it is recoloured through <see cref="ThemePalette.Map"/>, the backdrop takes the theme's motif and
/// the result replaces the theme. With the default look and motion on, only the palette is parsed again and the rest
/// comes compiled (<see cref="Compiled"/>). Windows resolve their brushes when they are created, so the host recreates
/// them after a change.
/// </summary>
public static partial class ThemeEngine
{
    /// <summary>The compiled Theme.xaml, named with its assembly so that it loads from any host (a test, the bench).</summary>
    private static readonly Uri BuiltIn = new("pack://application:,,,/Hearthpulse;component/Themes/Theme.xaml");
    private static string? _source;

    /// <summary>Brushes whose colour carries meaning never change.</summary>
    [GeneratedRegex("x:Key=\"(Positive|Negative|Info|Rarity|Tier|ManaGem|PoolStat)")]
    private static partial Regex FixedKey();

    [GeneratedRegex("#([0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\\b")]
    private static partial Regex HexColor();

    [GeneratedRegex("clr-namespace:([\\w.]+)\"")]
    private static partial Regex LocalNamespace();

    /// <summary>Animation timings in the theme's control templates (hover fades, switch slide, press shrink).</summary>
    [GeneratedRegex("(Duration|BeginTime)=\"0:0:[0-9.]+\"")]
    private static partial Regex Timing();

    /// <summary>The motion tokens at the top of Theme.xaml (<c>&lt;Duration x:Key="MotionFast"&gt;</c>), see <see cref="Motion"/>.</summary>
    [GeneratedRegex("(<Duration x:Key=\"Motion[A-Za-z]+\">)0:0:[0-9.]+(</Duration>)")]
    private static partial Regex MotionToken();

    /// <summary>Uses of the built-in motif (the backdrop and MotifBrush draw MotifTavern and MotifTavernAlt).</summary>
    [GeneratedRegex("\\{StaticResource Motif" + ThemePalette.DefaultName + "(Alt)?\\}")]
    private static partial Regex MotifUse();

    /// <summary>The shape tokens at the top of Theme.xaml (<c>&lt;CornerRadius x:Key="RadiusSmall"&gt;4&lt;/CornerRadius&gt;</c>, CardPadding).</summary>
    [GeneratedRegex("(<(?:CornerRadius|Thickness) x:Key=\"(Radius(?:Small|Medium|Large)|CardPadding)\">)[0-9.]+(</(?:CornerRadius|Thickness)>)")]
    private static partial Regex ShapeToken();

    /// <summary>A plain corner radius in a control template (buttons, boxes, overlay cards): pills (999) and odd shapes stay.</summary>
    [GeneratedRegex("CornerRadius=\"([3-9]|1[0-6])\"")]
    private static partial Regex PlainRadius();

    public static void Apply(Application app, ThemeSpec theme) => Apply(app, theme, ThemeLook.Default);

    public static void Apply(Application app, ThemeSpec theme, ThemeLook look)
    {
        // With motion off (Windows' animation setting or the app's reduce-motion setting), the theme is rebuilt with every control animation made instant.
        // An isolated run builds the theme from its source too: its text font is the one every Windows has (TextFont).
        ResourceDictionary dictionary;
        if (look.IsDefault && Motion.On && !Isolation.On)
        {
            dictionary = Compiled(theme);
        }
        else
        {
            dictionary = Build(theme, look);
            Freeze(dictionary);
        }

        var merged = app.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(dictionary);
    }

    public static ResourceDictionary Build(ThemeSpec theme) => Build(theme, ThemeLook.Default);

    public static ResourceDictionary Build(ThemeSpec theme, ThemeLook look) => (ResourceDictionary)XamlReader.Parse(Source(theme, look));

    /// <summary>
    /// The theme from the compiled Theme.xaml, as <see cref="Build"/> makes it for the default look with motion on, without
    /// parsing the whole file again: only the palette (the lines above <see cref="PaletteEnd"/>, every colour of the
    /// theme) is recoloured and parsed, with the theme's motif alone among the motifs, and takes the place of the
    /// compiled one, so the styles below it find the new colours when they are made. Values are made and frozen on
    /// first use.
    /// </summary>
    internal static ResourceDictionary Compiled(ThemeSpec theme)
    {
        if (ThemePalette.IsDefault(theme))
        {
            return new FrozenOnUse { Source = BuiltIn };
        }

        var parsed = (ResourceDictionary)XamlReader.Parse(PaletteSource(theme));
        var palette = new Dictionary<object, object>(parsed.Count);
        foreach (var key in parsed.Keys)
        {
            var value = parsed[key];
            if (value is Freezable { IsFrozen: false, CanFreeze: true } freezable)
            {
                freezable.Freeze();
            }

            palette[key] = value;
        }

        return new FrozenOnUse { Palette = palette, Source = BuiltIn };
    }

    /// <summary>The line of Theme.xaml that ends its palette: no colour literal and no motif below it.</summary>
    internal const string PaletteEnd = "<!-- palette:end";

    /// <summary>The source of the built-in Theme.xaml (its controls named with their assembly).</summary>
    internal static string BuiltInSource => _source ??= Load();

    /// <summary>The palette of the theme's XAML (<see cref="Compiled"/>): recoloured as <see cref="Source(ThemeSpec, ThemeLook)"/> does, the other presets' motifs left out.</summary>
    internal static string PaletteSource(ThemeSpec theme)
    {
        var source = BuiltInSource;
        var end = source.LastIndexOf('\n', source.IndexOf(PaletteEnd, StringComparison.Ordinal));
        var motif = "x:Key=\"Motif" + theme.Motif + "\"";
        var motifAlt = "x:Key=\"Motif" + theme.Motif + "Alt\"";
        var lines = source[..end].Split('\n')
            .Where(l => !l.Contains("<Geometry x:Key=\"Motif", StringComparison.Ordinal)
                        || l.Contains(motif, StringComparison.Ordinal) || l.Contains(motifAlt, StringComparison.Ordinal))
            .Append("</ResourceDictionary>");
        return Transform(lines.ToArray(), theme, ThemeLook.Default);
    }

    public static string Source(ThemeSpec theme) => Source(theme, ThemeLook.Default);

    /// <summary>The theme's XAML: Theme.xaml recoloured, with the theme's motif, its look's shape and, if needed, instant animations.</summary>
    public static string Source(ThemeSpec theme, ThemeLook look) => Transform(BuiltInSource.Split('\n'), theme, look);

    private static string Transform(string[] lines, ThemeSpec theme, ThemeLook look)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (!FixedKey().IsMatch(lines[i]))
            {
                lines[i] = HexColor().Replace(lines[i], m => Recolor(m.Value, theme));
            }
        }

        var xaml = LocalNamespace().Replace(string.Join('\n', lines), "clr-namespace:$1;assembly=Hearthpulse\"")
            .Replace(TextFont.Families, TextFont.Name, StringComparison.Ordinal);
        xaml = MotifUse().Replace(xaml, m => "{StaticResource Motif" + theme.Motif + m.Groups[1].Value + "}");
        if (!look.IsDefault)
        {
            xaml = ShapeToken().Replace(xaml, m => look.Token(m.Groups[2].Value) is { } value
                ? m.Groups[1].Value + value.ToString(CultureInfo.InvariantCulture) + m.Groups[3].Value
                : m.Value);
            if (look.SharpCorners)
            {
                xaml = PlainRadius().Replace(xaml, "CornerRadius=\"2\"");
            }
        }

        if (!Motion.On)
        {
            xaml = Timing().Replace(xaml, "$1=\"0:0:0\"");
            xaml = MotionToken().Replace(xaml, "${1}0:0:0$2");
        }

        return xaml;
    }

    /// <summary>One theme colour as a frozen brush, for previews.</summary>
    public static SolidColorBrush Brush(uint argb, ThemeSpec theme)
    {
        var c = ThemePalette.Map(argb, theme);
        var brush = new SolidColorBrush(Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c));
        brush.Freeze();
        return brush;
    }

    /// <summary>A theme's backdrop line art (<c>Motif&lt;Name&gt;</c>), or null outside the app.</summary>
    public static Geometry? Motif(string name) => Application.Current?.TryFindResource("Motif" + name) as Geometry;

    /// <summary>
    /// Freezes every brush, geometry and drawing of the theme: frozen resources are shared without change tracking,
    /// which keeps the overlay (redrawn over the game) cheap.
    /// </summary>
    public static void Freeze(ResourceDictionary dictionary)
    {
        foreach (var key in dictionary.Keys)
        {
            if (dictionary[key] is Freezable { IsFrozen: false, CanFreeze: true } freezable)
            {
                freezable.Freeze();
            }
        }
    }

    /// <summary>
    /// The compiled theme, each value made when first asked for (by a window, a style or a lookup) and every brush,
    /// geometry and drawing frozen as it comes out: <see cref="Freeze"/> on the whole dictionary made its 45 styles
    /// at start-up, most of them before any window used them.
    /// </summary>
    private sealed class FrozenOnUse : ResourceDictionary
    {
        /// <summary>The recoloured palette: what a lookup of its keys gets, from outside or from a style being made.</summary>
        public Dictionary<object, object>? Palette { get; init; }

        protected override void OnGettingValue(object key, ref object value, out bool canCache)
        {
            base.OnGettingValue(key, ref value, out canCache);
            if (Palette is not null && Palette.TryGetValue(key, out var recoloured))
            {
                value = recoloured;
            }

            if (value is Freezable { IsFrozen: false, CanFreeze: true } freezable)
            {
                freezable.Freeze();
            }
        }
    }

    private static string Recolor(string hex, ThemeSpec theme)
    {
        var value = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (hex.Length == 7)
        {
            value |= 0xFF000000;
        }

        var mapped = ThemePalette.Map(value, theme);
        return hex.Length == 7 ? $"#{mapped & 0xFFFFFF:X6}" : $"#{mapped:X8}";
    }

    private static string Load()
    {
        using var stream = typeof(ThemeEngine).Assembly.GetManifestResourceStream("Hearthpulse.Theme.xaml")
            ?? throw new InvalidOperationException("Theme source is missing from the app.");
        using var reader = new StreamReader(stream);
        // The app's own controls (GoldGlyph in the NavItem template) are named without an assembly, as the markup compiler
        // needs; XamlReader needs the assembly.
        return reader.ReadToEnd().Replace(ControlsNamespace + "\"", ControlsNamespace + ";assembly=Hearthpulse\"", StringComparison.Ordinal);
    }

    private const string ControlsNamespace = "clr-namespace:Hearthpulse.App.Controls";
}
