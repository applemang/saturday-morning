using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.SaturdayMorning.Selection;

/// <summary>
/// Composes the selection rules into a full playlist plan.
/// </summary>
public static class PlaylistPlanner
{
    /// <summary>
    /// Filters, picks, shuffles, caps and interleaves.
    /// </summary>
    /// <param name="episodes">All episodes from the source libraries.</param>
    /// <param name="commercials">All items in the commercials library.</param>
    /// <param name="settings">Validated selection settings.</param>
    /// <param name="random">Random source.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="SelectionException">Nothing to build a playlist from.</exception>
    public static PlaylistPlan Plan(
        IEnumerable<EpisodeCandidate> episodes,
        IReadOnlyList<Guid> commercials,
        SelectionSettings settings,
        Random random)
    {
        if (settings.CommercialsMax > 0 && commercials.Count == 0)
        {
            throw new SelectionException("The commercials library returned no items, but commercial breaks are configured.");
        }

        var filtered = EpisodeSelection.FilterByAirDate(episodes, settings.AirDateFrom, settings.AirDateTo, settings.Fallback);
        if (filtered.Kept.Count == 0)
        {
            throw new SelectionException(FormattableString.Invariant(
                $"No episodes in the source libraries aired between {settings.AirDateFrom:yyyy-MM-dd} and {settings.AirDateTo:yyyy-MM-dd} ({filtered.SkippedMissingDate} skipped for missing dates)."));
        }

        var picked = settings.OnePerSeries
            ? EpisodeSelection.PickOnePerSeries(filtered.Kept, random)
            : filtered.Kept;
        var chosen = EpisodeSelection.ApplyCap(EpisodeSelection.Shuffle(picked, random), settings.MaxEpisodes);

        var interleaved = CommercialInterleaver.Interleave(
            chosen.Select(e => e.Id).ToList(),
            commercials,
            settings.CommercialsMin,
            settings.CommercialsMax,
            settings.AvoidRepeatingCommercials,
            random);

        return new PlaylistPlan(
            interleaved.Items,
            chosen.Count,
            chosen.Select(e => e.SeriesId).Distinct().Count(),
            interleaved.CommercialsInserted,
            filtered.SkippedMissingDate,
            interleaved.PoolExhausted);
    }
}
