using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The theme the app applies with the default look (ThemeEngine.Compiled: the compiled Theme.xaml, only its palette
/// recoloured and parsed again) holds the same colours, in its resources and in every style's setters and triggers, as
/// the theme parsed whole from the recoloured text (ThemeEngine.Build), for every preset; it draws the preset's motif
/// and hands out every brush, geometry and drawing frozen.
/// </summary>
public sealed partial class ThemeCompiledTests
{
    // ThemeEngine keeps a pack:// URI of the built-in theme; without an Application the scheme needs registering.
    static ThemeCompiledTests() => _ = System.IO.Packaging.PackUriHelper.UriSchemePack;

    public static TheoryData<string> Presets() => new(ThemePalette.Presets.Select(p => p.Name).Append("Custom"));

    [Theory]
    [MemberData(nameof(Presets))]
    public void Compiled_theme_has_the_colours_of_the_theme_parsed_from_text(string name)
    {
        var theme = ThemePalette.Resolve(name, 330, 300, 0.9);
        WpfThread.Run(() =>
        {
            var different = Differences(ThemeEngine.Build(theme), ThemeEngine.Compiled(theme));
            Assert.True(different.Count == 0, string.Join('\n', different.Take(2)));
        });
    }

    /// <summary>
    /// The comparison sees an implicit style, keyed by its type rather than by a name: a setter added to the ScrollBar
    /// style of the text alone is a difference (review of TASK-2026-11-23-perf-arch-round2 P2, M1: looked up by the
    /// text of its key, the style was null on both sides and passed).
    /// </summary>
    [Fact]
    public void A_difference_in_an_implicit_style_is_found()
    {
        var theme = ThemePalette.Resolve("Frost", 0, 0, 1);
        WpfThread.Run(() =>
        {
            const string scrollBar = "<Style TargetType=\"ScrollBar\">";
            var source = ThemeEngine.Source(theme);
            Assert.Contains(scrollBar, source, StringComparison.Ordinal);
            var wrong = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(
                source.Replace(scrollBar, scrollBar + "<Setter Property=\"Opacity\" Value=\"0.625\" />", StringComparison.Ordinal));

            var different = Differences(wrong, ThemeEngine.Compiled(theme));
            Assert.Equal(new[] { "{x:Type System.Windows.Controls.Primitives.ScrollBar}" }, different.Select(d => d.Split('\n')[0]));
            Assert.Contains("Opacity=Double:0.625", different[0], StringComparison.Ordinal);
            Assert.Contains("ScrollBar", Templated(wrong, ThemeEngine.Compiled(theme)).Different);
        });
    }

    /// <summary>A style's template draws with the theme's colours too: each templated control is built from both dictionaries and its tree compared, node by node.</summary>
    [Fact]
    public void Templates_of_the_compiled_theme_draw_with_the_colours_of_the_theme_parsed_from_text()
    {
        var theme = ThemePalette.Resolve("Frost", 0, 0, 1);
        WpfThread.Run(() =>
        {
            var (templated, implicitStyles, different) = Templated(ThemeEngine.Build(theme), ThemeEngine.Compiled(theme));
            Assert.True(different.Length == 0, different);
            Assert.True(templated > 10, $"templated controls compared: {templated}");
            Assert.True(implicitStyles > 0, "implicit styles (keyed by their type) are compared too");
        });
    }

    /// <summary>
    /// The scrollbar thumb under the mouse keeps the colour it had before the theme came compiled: the accent with alpha
    /// 0x80 at full opacity, not the opaque accent at opacity 0.5 (review of P2, m1).
    /// </summary>
    [Fact]
    public void The_scroll_thumb_under_the_mouse_keeps_its_colour()
    {
        var tavern = ThemePalette.Resolve(ThemePalette.DefaultName, 0, 0, 1);
        WpfThread.Run(() =>
        {
            var parsed = ThemeEngine.Build(tavern);
            var template = (System.Windows.Controls.ControlTemplate)((Style)parsed["ScrollThumb"]).Setters.OfType<Setter>()
                .Single(s => s.Property == System.Windows.Controls.Control.TemplateProperty).Value;
            var hover = template.Triggers.OfType<Trigger>().Single(t => t.Property == UIElement.IsMouseOverProperty);
            var brush = Assert.IsType<SolidColorBrush>(hover.Setters.OfType<Setter>().Single(s => s.Property.Name == "Background").Value);
            Assert.Equal((Color.FromArgb(0x80, 0xE9, 0xB9, 0x49), 1d), (brush.Color, brush.Opacity));
            var compiled = (SolidColorBrush)ThemeEngine.Compiled(tavern)["ScrollThumbHover"];
            Assert.Equal((brush.Color, brush.Opacity), (compiled.Color, compiled.Opacity));
        });
    }

    /// <summary>
    /// The keys whose values differ between the two dictionaries, each looked up by the key itself (an implicit style is
    /// keyed by its type), with both values printed; the two must hold the same keys.
    /// </summary>
    private static List<string> Differences(ResourceDictionary parsed, ResourceDictionary compiled)
    {
        var keys = parsed.Keys.Cast<object>().OrderBy(Name, StringComparer.Ordinal).ToList();
        Assert.Equal(keys.Select(Name), compiled.Keys.Cast<object>().Select(Name).Order(StringComparer.Ordinal));
        Assert.All(keys, k => Assert.True(compiled.Contains(k), Name(k)));
        return keys.Select(k => (Key: k, P: Print(parsed[k]), C: Print(compiled[k])))
            .Where(d => d.P != d.C)
            .Select(d => Name(d.Key) + "\n P " + d.P + "\n C " + d.C)
            .ToList();
    }

    /// <summary>Every templated control of the styles built from both dictionaries: how many, how many of them by an implicit style, and the first trees that differ.</summary>
    private static (int Templated, int Implicit, string Different) Templated(ResourceDictionary parsed, ResourceDictionary compiled)
    {
        var (templated, implicitStyles, different) = (0, 0, new StringBuilder());
        foreach (var key in parsed.Keys.Cast<object>().OrderBy(Name, StringComparer.Ordinal))
        {
            if (parsed[key] is not Style { TargetType: { IsAbstract: false } type } style || !typeof(System.Windows.Controls.Control).IsAssignableFrom(type)
                || typeof(Window).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
            {
                continue;
            }

            var drawn = Tree(type, style);
            if (drawn.Length == 0)
            {
                continue;
            }

            templated++;
            implicitStyles += key is Type ? 1 : 0;
            var other = Tree(type, (Style)compiled[key]);
            if (drawn != other && different.Length < 4000)
            {
                different.Append(Name(key)).Append("\n P ").Append(drawn).Append("\n C ").Append(other).Append('\n');
            }
        }

        return (templated, implicitStyles, different.ToString());

        static string Tree(Type type, Style style)
        {
            var control = (System.Windows.Controls.Control)Activator.CreateInstance(type)!;
            control.Style = style;
            if (!control.ApplyTemplate())
            {
                return "";
            }

            // Every node with its name and the values a theme or a setter can change, brushes or not.
            var text = new StringBuilder();
            void Walk(DependencyObject node, int depth)
            {
                text.Append(depth).Append(' ').Append(node.GetType().Name);
                if (node is FrameworkElement { Name.Length: > 0 } named)
                {
                    text.Append('#').Append(named.Name);
                }

                if (node is UIElement element)
                {
                    text.Append(" opacity ").Append(Number(element.Opacity)).Append(' ').Append(element.Visibility);
                }

                foreach (var property in new[] { "Background", "BorderBrush", "Foreground", "Fill", "Stroke" })
                {
                    if (node.GetType().GetProperty(property)?.GetValue(node) is Brush brush)
                    {
                        text.Append(' ').Append(property).Append('=');
                        Print(brush, text);
                    }
                }

                text.Append(';');
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                {
                    Walk(VisualTreeHelper.GetChild(node, i), depth + 1);
                }
            }

            Walk(control, 0);
            return text.ToString();
        }
    }

    /// <summary>A key as the report names it: an implicit style's type as XAML writes it.</summary>
    private static string Name(object key) => key is Type type ? "{x:Type " + type.FullName + "}" : key.ToString()!;

    [Theory]
    [MemberData(nameof(Presets))]
    public void Compiled_theme_draws_its_own_motif_and_hands_out_frozen_values(string name)
    {
        var theme = ThemePalette.Resolve(name, 330, 300, 0.9);
        WpfThread.Run(() =>
        {
            var dictionary = ThemeEngine.Compiled(theme);
            var motif = (Geometry)dictionary["Motif" + theme.Motif];
            var badge = (DrawingGroup)((DrawingBrush)dictionary["MotifBrush"]).Drawing;
            Assert.Same(motif, ((GeometryDrawing)badge.Children[0]).Geometry);
            var carved = (DrawingGroup)((DrawingGroup)((DrawingBrush)dictionary["ContentFill"]).Drawing).Children[^1];
            Assert.Same(motif, ((GeometryDrawing)carved.Children[0]).Geometry);
            Assert.Same(dictionary["Motif" + theme.Motif + "Alt"], ((GeometryDrawing)carved.Children[1]).Geometry);

            var unfrozen = dictionary.Keys.Cast<object>().Where(k => dictionary[k] is Freezable { IsFrozen: false }).ToList();
            Assert.Empty(unfrozen);
        });
    }

    /// <summary>
    /// Below the palette (styles and their templates, which come compiled whatever the theme) nothing names a colour
    /// or a motif: it would keep the default theme's. A template's own tree cannot be compared once built, so the
    /// source is read for it.
    /// </summary>
    [Fact]
    public void Nothing_below_the_palette_names_a_colour_or_a_motif()
    {
        var source = ThemeEngine.BuiltInSource;
        var end = source.IndexOf(ThemeEngine.PaletteEnd, StringComparison.Ordinal);
        Assert.True(end > 0, "Theme.xaml marks the end of its palette");
        var styles = source[end..];
        Assert.DoesNotMatch(HexColor(), styles);
        Assert.DoesNotContain("Motif", styles, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Accent2\"", source[..end], StringComparison.Ordinal);
    }

    [GeneratedRegex("#[0-9A-Fa-f]{6}")]
    private static partial Regex HexColor();

    /// <summary>What a resource holds that a theme can change, as text: colours of brushes, drawings, effects, and of every setter and trigger of a style.</summary>
    private static string Print(object? value)
    {
        var text = new StringBuilder();
        Print(value, text);
        return text.ToString();
    }

    private static void Print(object? value, StringBuilder text)
    {
        switch (value)
        {
            case null:
                text.Append("null");
                break;
            case Color color:
                text.Append(color);
                break;
            case SolidColorBrush solid:
                text.Append("Solid(").Append(solid.Color).Append(' ').Append(Number(solid.Opacity)).Append(')');
                break;
            case GradientBrush gradient:
                text.Append(gradient.GetType().Name).Append('(').Append(Number(gradient.Opacity));
                foreach (var stop in gradient.GradientStops)
                {
                    text.Append(' ').Append(stop.Color).Append('@').Append(Number(stop.Offset));
                }

                text.Append(')');
                break;
            case DrawingBrush drawing:
                text.Append("Drawing(");
                Print(drawing.Drawing, text);
                text.Append(')');
                break;
            case DrawingGroup group:
                text.Append("Group[");
                foreach (var child in group.Children)
                {
                    Print(child, text);
                    text.Append(';');
                }

                text.Append(']');
                break;
            case GeometryDrawing shape:
                text.Append("Shape(");
                Print(shape.Brush, text);
                text.Append(',');
                Print(shape.Pen, text);
                text.Append(',').Append(shape.Geometry?.Bounds.ToString(CultureInfo.InvariantCulture)).Append(')');
                break;
            case Pen pen:
                text.Append("Pen(");
                Print(pen.Brush, text);
                text.Append(' ').Append(Number(pen.Thickness)).Append(')');
                break;
            case DropShadowEffect shadow:
                text.Append("Shadow(").Append(shadow.Color).Append(' ').Append(Number(shadow.Opacity)).Append(')');
                break;
            case Style style:
                text.Append("Style<").Append(style.TargetType?.Name).Append(">(");
                Print(style.BasedOn, text);
                PrintSetters(style.Setters, text);
                PrintTriggers(style.Triggers, text);
                // Looked up by the keys themselves: a nested implicit style is keyed by its type.
                foreach (var key in style.Resources.Keys.Cast<object>().OrderBy(Name, StringComparer.Ordinal))
                {
                    text.Append(" res ").Append(Name(key)).Append('=');
                    Print(style.Resources[key], text);
                }

                text.Append(')');
                break;
            case FrameworkTemplate template:
                text.Append(template.GetType().Name).Append('(');
                PrintTriggers(template is System.Windows.Controls.ControlTemplate control ? control.Triggers : template is DataTemplate data ? data.Triggers : null, text);
                text.Append(')');
                break;
            case DynamicResourceExtension dynamic:
                text.Append("Dynamic(").Append(dynamic.ResourceKey).Append(')');
                break;
            default:
                text.Append(value.GetType().Name).Append(':').Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static void PrintSetters(SetterBaseCollection setters, StringBuilder text)
    {
        foreach (var setter in setters.OfType<Setter>())
        {
            text.Append(" set ").Append(setter.TargetName).Append('.').Append(setter.Property?.Name).Append('=');
            Print(setter.Value, text);
        }
    }

    private static void PrintTriggers(TriggerCollection? triggers, StringBuilder text)
    {
        foreach (var trigger in triggers ?? Enumerable.Empty<TriggerBase>())
        {
            switch (trigger)
            {
                case Trigger single:
                    text.Append(" when ").Append(single.Property?.Name).Append('=').Append(single.Value);
                    PrintSetters(single.Setters, text);
                    break;
                case MultiTrigger multi:
                    text.Append(" when ").Append(string.Join('&', multi.Conditions.Select(c => c.Property?.Name + "=" + c.Value)));
                    PrintSetters(multi.Setters, text);
                    break;
                case DataTrigger data:
                    text.Append(" when data ").Append(data.Value);
                    PrintSetters(data.Setters, text);
                    break;
                case MultiDataTrigger multiData:
                    text.Append(" when data ").Append(string.Join('&', multiData.Conditions.Select(c => c.Value)));
                    PrintSetters(multiData.Setters, text);
                    break;
                default:
                    text.Append(" when ").Append(trigger.GetType().Name);
                    break;
            }
        }
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
