using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.SaturdayMorning.Selection;

/// <summary>
/// What to do with an episode that has no premiere date.
/// </summary>
public enum MissingDateFallback
{
    /// <summary>Leave the episode out.</summary>
    Exclude = 0,

    /// <summary>Keep the episode when its series premiere year falls within the range years.</summary>
    UseSeriesPremiereYear = 1,
}

/// <summary>
/// Kind of an entry in the planned playlist.
/// </summary>
public enum PlanItemKind
{
    /// <summary>An episode from a source library.</summary>
    Episode = 0,

    /// <summary>A commercial from the commercials library.</summary>
    Commercial = 1,
}

/// <summary>
/// An episode reduced to the fields the selection rules need.
/// </summary>
/// <param name="Id">Jellyfin item id.</param>
/// <param name="SeriesId">Id of the episode's series.</param>
/// <param name="PremiereDate">Episode air date, if known.</param>
/// <param name="SeriesPremiereYear">Premiere year of the series, if known.</param>
public sealed record EpisodeCandidate(Guid Id, Guid SeriesId, DateOnly? PremiereDate, int? SeriesPremiereYear);

/// <summary>
/// One entry in the planned playlist.
/// </summary>
/// <param name="Id">Jellyfin item id.</param>
/// <param name="Kind">Whether it is an episode or a commercial.</param>
public sealed record PlanItem(Guid Id, PlanItemKind Kind);

/// <summary>
/// Result of the air-date filter.
/// </summary>
/// <param name="Kept">Episodes inside the range, in input order.</param>
/// <param name="SkippedMissingDate">Episodes dropped because no usable date was available.</param>
public sealed record AirDateFilterResult(IReadOnlyList<EpisodeCandidate> Kept, int SkippedMissingDate);

/// <summary>
/// Result of interleaving commercials between episodes.
/// </summary>
/// <param name="Items">Episodes with commercial breaks between them.</param>
/// <param name="CommercialsInserted">Number of commercial entries added.</param>
/// <param name="PoolExhausted">True when no-repeat was requested but the pool ran out and repeats were used.</param>
public sealed record InterleaveResult(IReadOnlyList<PlanItem> Items, int CommercialsInserted, bool PoolExhausted);

/// <summary>
/// Selection rules, already validated.
/// </summary>
/// <param name="AirDateFrom">First air date to include.</param>
/// <param name="AirDateTo">Last air date to include.</param>
/// <param name="Fallback">Handling of episodes without a premiere date.</param>
/// <param name="OnePerSeries">Keep at most one episode per series.</param>
/// <param name="MaxEpisodes">Episode cap; 0 means no cap.</param>
/// <param name="CommercialsMin">Minimum commercials per break.</param>
/// <param name="CommercialsMax">Maximum commercials per break.</param>
/// <param name="AvoidRepeatingCommercials">Avoid using a commercial twice in one run.</param>
/// <param name="SeriesIds">Series allowed in the rotation; null means every series.</param>
public sealed record SelectionSettings(
    DateOnly AirDateFrom,
    DateOnly AirDateTo,
    MissingDateFallback Fallback,
    bool OnePerSeries,
    int MaxEpisodes,
    int CommercialsMin,
    int CommercialsMax,
    bool AvoidRepeatingCommercials,
    IReadOnlySet<Guid>? SeriesIds = null);

/// <summary>
/// The finished plan for one run.
/// </summary>
/// <param name="Items">Ordered playlist entries.</param>
/// <param name="EpisodeCount">Episodes chosen.</param>
/// <param name="SeriesCount">Distinct series among the chosen episodes.</param>
/// <param name="CommercialCount">Commercial entries inserted.</param>
/// <param name="SkippedMissingDate">Episodes skipped for missing dates.</param>
/// <param name="CommercialPoolExhausted">True when repeats had to be used despite no-repeat.</param>
public sealed record PlaylistPlan(
    IReadOnlyList<PlanItem> Items,
    int EpisodeCount,
    int SeriesCount,
    int CommercialCount,
    int SkippedMissingDate,
    bool CommercialPoolExhausted);
