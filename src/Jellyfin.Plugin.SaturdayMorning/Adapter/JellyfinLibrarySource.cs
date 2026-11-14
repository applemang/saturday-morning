using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SaturdayMorning.Selection;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.SaturdayMorning.Adapter;

/// <summary>
/// Reads episodes and commercials from Jellyfin and reduces them to selection records.
/// </summary>
public sealed class JellyfinLibrarySource
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinLibrarySource"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    public JellyfinLibrarySource(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Returns ids of the libraries that do not exist on this server.
    /// </summary>
    /// <param name="libraryIds">Configured library ids.</param>
    /// <returns>The unknown ids.</returns>
    public IReadOnlyList<Guid> FindMissingLibraries(IEnumerable<Guid> libraryIds)
        => libraryIds.Where(id => _libraryManager.GetItemById(id) is not Folder).ToList();

    /// <summary>
    /// Loads every real (non-virtual) episode in the given libraries.
    /// </summary>
    /// <param name="libraryIds">Source library ids.</param>
    /// <returns>Episode records.</returns>
    public IReadOnlyList<EpisodeCandidate> GetEpisodes(IEnumerable<Guid> libraryIds)
    {
        var seriesYears = new Dictionary<Guid, int?>();
        return libraryIds
            .SelectMany(id => _libraryManager.GetItemList(new InternalItemsQuery
            {
                ParentId = id,
                Recursive = true,
                IncludeItemTypes = [BaseItemKind.Episode],
                IsVirtualItem = false,
                DtoOptions = new DtoOptions(false),
            }))
            .OfType<Episode>()
            .DistinctBy(e => e.Id)
            .Select(e => new EpisodeCandidate(
                e.Id,
                e.SeriesId,
                e.PremiereDate is { } aired ? DateOnly.FromDateTime(aired) : null,
                SeriesYear(e, seriesYears)))
            .ToList();
    }

    /// <summary>
    /// Loads every playable video in the commercials library.
    /// </summary>
    /// <param name="libraryId">Commercials library id.</param>
    /// <returns>Item ids.</returns>
    public IReadOnlyList<Guid> GetCommercials(Guid libraryId)
        => _libraryManager.GetItemList(new InternalItemsQuery
        {
            ParentId = libraryId,
            Recursive = true,
            IsFolder = false,
            IsVirtualItem = false,
            MediaTypes = [MediaType.Video],
            DtoOptions = new DtoOptions(false),
        })
            .Select(i => i.Id)
            .Distinct()
            .ToList();

    private static int? SeriesYear(Episode episode, Dictionary<Guid, int?> cache)
    {
        if (!cache.TryGetValue(episode.SeriesId, out var year))
        {
            var series = episode.Series;
            year = series?.PremiereDate?.Year ?? series?.ProductionYear;
            cache[episode.SeriesId] = year;
        }

        return year;
    }
}
