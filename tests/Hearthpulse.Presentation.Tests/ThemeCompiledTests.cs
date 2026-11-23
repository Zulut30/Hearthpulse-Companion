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
            var parsed = ThemeEngine.Build(theme);
            var compiled = ThemeEngine.Compiled(theme);

            var keys = parsed.Keys.Cast<object>().Select(k => k.ToString()!).Order(StringComparer.Ordinal).ToList();
            Assert.Equal(keys, compiled.Keys.Cast<object>().Select(k => k.ToString()!).Order(StringComparer.Ordinal));
            var different = keys.Where(k => Print(parsed[k]) != Print(compiled[k])).ToList();
            Assert.True(different.Count == 0, string.Join('\n', different.Take(2).Select(k => k + "\n P " + Print(parsed[k]) + "\n C " + Print(compiled[k]))));
        });
    }

    /// <summary>A style's template draws with the theme's colours too: each templated control is built from both dictionaries and its tree's brushes compared.</summary>
    [Fact]
    public void Templates_of_the_compiled_theme_draw_with_the_colours_of_the_theme_parsed_from_text()
    {
        var theme = ThemePalette.Resolve("Frost", 0, 0, 1);
        WpfThread.Run(() =>
        {
            var parsed = ThemeEngine.Build(theme);
            var compiled = ThemeEngine.Compiled(theme);
            var templated = 0;
            foreach (var key in parsed.Keys.Cast<object>().Select(k => k.ToString()!).Order(StringComparer.Ordinal))
            {
                if (parsed[key] is not Style { TargetType: { IsAbstract: false } type } style || !typeof(System.Windows.Controls.Control).IsAssignableFrom(type)
                    || typeof(Window).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
                {
                    continue;
                }

                var drawn = Tree(type, style);
                if (drawn.Length > 0)
                {
                    templated++;
                    Assert.True(drawn == Tree(type, (Style)compiled[key]), key + "\n P " + drawn + "\n C " + Tree(type, (Style)compiled[key]));
                }
            }

            Assert.True(templated > 10, $"templated controls compared: {templated}");
        });

        static string Tree(Type type, Style style)
        {
            var control = (System.Windows.Controls.Control)Activator.CreateInstance(type)!;
            control.Style = style;
            if (!control.ApplyTemplate())
            {
                return "";
            }

            var text = new StringBuilder();
            void Walk(DependencyObject node)
            {
                foreach (var brush in new[] { "Background", "BorderBrush", "Foreground", "Fill", "Stroke" }
                    .Select(p => node.GetType().GetProperty(p)?.GetValue(node)).OfType<Brush>())
                {
                    text.Append(node.GetType().Name).Append(' ');
                    Print(brush, text);
                    text.Append(';');
                }

                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                {
                    Walk(VisualTreeHelper.GetChild(node, i));
                }
            }

            Walk(control);
            return text.ToString();
        }
    }

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
                foreach (var key in style.Resources.Keys.Cast<object>().Select(k => k.ToString()!).Order(StringComparer.Ordinal))
                {
                    text.Append(" res ").Append(key).Append('=');
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
