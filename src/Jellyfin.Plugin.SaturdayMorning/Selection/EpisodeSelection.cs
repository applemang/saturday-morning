using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.SaturdayMorning.Selection;

/// <summary>
/// Pure episode selection rules: air-date filter, one per series, shuffle, cap.
/// </summary>
public static class EpisodeSelection
{
    /// <summary>
    /// Keeps episodes whose series is in the allowed set.
    /// </summary>
    /// <param name="episodes">Candidate episodes.</param>
    /// <param name="seriesIds">Allowed series, or null for every series.</param>
    /// <returns>The episodes of allowed series, in input order.</returns>
    public static IReadOnlyList<EpisodeCandidate> FilterBySeries(IReadOnlyList<EpisodeCandidate> episodes, IReadOnlySet<Guid>? seriesIds)
        => seriesIds is null ? episodes : episodes.Where(e => seriesIds.Contains(e.SeriesId)).ToList();

    /// <summary>
    /// Keeps episodes whose air date is within [from, to], inclusive.
    /// </summary>
    /// <remarks>
    /// Under <see cref="MissingDateFallback.UseSeriesPremiereYear"/> an undated episode is kept
    /// when its series premiere year lies between from.Year and to.Year inclusive.
    /// </remarks>
    /// <param name="episodes">Candidate episodes.</param>
    /// <param name="from">First air date to include.</param>
    /// <param name="to">Last air date to include.</param>
    /// <param name="fallback">Handling of undated episodes.</param>
    /// <returns>The kept episodes and the count skipped for lack of a usable date.</returns>
    public static AirDateFilterResult FilterByAirDate(
        IEnumerable<EpisodeCandidate> episodes,
        DateOnly from,
        DateOnly to,
        MissingDateFallback fallback)
    {
        var kept = new List<EpisodeCandidate>();
        var skipped = 0;
        foreach (var episode in episodes)
        {
            if (episode.PremiereDate is { } aired)
            {
                if (aired >= from && aired <= to)
                {
                    kept.Add(episode);
                }
            }
            else if (fallback == MissingDateFallback.UseSeriesPremiereYear && episode.SeriesPremiereYear is { } year)
            {
                if (year >= from.Year && year <= to.Year)
                {
                    kept.Add(episode);
                }
            }
            else
            {
                skipped++;
            }
        }

        return new AirDateFilterResult(kept, skipped);
    }

    /// <summary>
    /// Picks one episode per series uniformly at random. Series keep first-seen order.
    /// </summary>
    /// <param name="episodes">Candidate episodes.</param>
    /// <param name="random">Random source.</param>
    /// <returns>One episode for each series present in the input.</returns>
    public static IReadOnlyList<EpisodeCandidate> PickOnePerSeries(IEnumerable<EpisodeCandidate> episodes, Random random)
        => episodes
            .GroupBy(e => e.SeriesId)
            .Select(g =>
            {
                var group = g.ToList();
                return group[random.Next(group.Count)];
            })
            .ToList();

    /// <summary>
    /// Returns a Fisher-Yates shuffled copy of the input.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="items">Items to shuffle; not modified.</param>
    /// <param name="random">Random source.</param>
    /// <returns>A new list in random order.</returns>
    public static IReadOnlyList<T> Shuffle<T>(IEnumerable<T> items, Random random)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    /// <summary>
    /// Takes at most <paramref name="max"/> items; 0 means no cap.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="items">Items in final order.</param>
    /// <param name="max">Cap, or 0 for none.</param>
    /// <returns>The capped list.</returns>
    public static IReadOnlyList<T> ApplyCap<T>(IReadOnlyList<T> items, int max)
        => max > 0 ? items.Take(max).ToList() : items;
}
