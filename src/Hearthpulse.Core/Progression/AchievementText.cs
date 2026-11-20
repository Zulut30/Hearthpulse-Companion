using System.Globalization;
using System.Text.RegularExpressions;
using Hearthpulse.Core.Localization;

namespace Hearthpulse.Core.Progression;

/// <summary>
/// An achievement's description as the player reads it. The client's text carries markup: <c>$q</c> is the quota,
/// <c>|4(one,few,many)</c> picks the Russian plural form by it and <c>|4(one,other)</c> the English one, <c>&lt;i&gt;</c>
/// marks italics. Other markup stays as it is (none was seen in the client's texts).
/// </summary>
public static partial class AchievementText
{
    /// <summary>The text with the quota written in and its markup resolved.</summary>
    public static string Format(string text, int quota)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var written = text.Replace("$q", quota.ToString("N0", CultureInfo.CurrentCulture), StringComparison.Ordinal);
        written = PluralForms().Replace(written, match =>
        {
            var forms = match.Groups[1].Value.Split(',');
            return forms.Length switch
            {
                3 => forms[Loc.PluralIndex(Loc.Russian, quota)],
                2 => forms[Loc.PluralIndex(Loc.English, quota)],
                _ => match.Value,
            };
        });
        return written.Replace("<i>", string.Empty, StringComparison.Ordinal).Replace("</i>", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>The achievement's description with its own quota (<see cref="Format"/>).</summary>
    public static string Describe(AchievementDef achievement)
    {
        ArgumentNullException.ThrowIfNull(achievement);
        return Format(achievement.Description, achievement.Quota);
    }

    [GeneratedRegex(@"\|4\(([^()]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex PluralForms();
}
