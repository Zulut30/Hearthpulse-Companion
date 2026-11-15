using System.Globalization;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;

namespace Hearthpulse.App.Main;

public sealed partial class MainViewModel
{
    private int CountHistory(IReadOnlyList<GameRecord> recent) => _history.GameCount();

    partial void RestoreRatingFromHistory(string mode, IReadOnlyList<RatingPoint> rating)
    {
        // Before Hearthstone reports a rating this session, show the last one from history.
        if (rating.Count > 0)
        {
            var last = rating[^1].Rating.ToString("N0", CultureInfo.CurrentCulture);
            if (mode == "Duos" && DuosRating == "—")
            {
                DuosRating = last;
            }
            else if (mode != "Duos" && SoloRating == "—")
            {
                SoloRating = last;
            }
        }
    }
}
