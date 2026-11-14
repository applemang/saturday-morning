using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SaturdayMorning.Adapter;
using Jellyfin.Plugin.SaturdayMorning.Configuration;
using Jellyfin.Plugin.SaturdayMorning.Selection;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SaturdayMorning.Tasks;

/// <summary>
/// Scheduled task that rebuilds the Saturday Morning playlist.
/// </summary>
public sealed partial class BuildSaturdayMorningTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<BuildSaturdayMorningTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BuildSaturdayMorningTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="playlistManager">Playlist manager.</param>
    /// <param name="userManager">User manager.</param>
    /// <param name="logger">Logger.</param>
    public BuildSaturdayMorningTask(
        ILibraryManager libraryManager,
        IPlaylistManager playlistManager,
        IUserManager userManager,
        ILogger<BuildSaturdayMorningTask> logger)
    {
        _libraryManager = libraryManager;
        _playlistManager = playlistManager;
        _userManager = userManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Build Saturday Morning playlist";

    /// <inheritdoc />
    public string Key => "SaturdayMorningBuildPlaylist";

    /// <inheritdoc />
    public string Description => "Picks shuffled episodes from the configured air-date range and places vintage commercials between them.";

    /// <inheritdoc />
    public string Category => "Saturday Morning";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.WeeklyTrigger,
            DayOfWeek = DayOfWeek.Saturday,
            TimeOfDayTicks = TimeSpan.FromHours(6).Ticks,
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Saturday Morning plugin is not loaded.");
        var validation = ConfigValidator.Validate(plugin.Configuration);
        if (validation.Config is not { } config)
        {
            throw new InvalidOperationException(
                "Saturday Morning is not configured; the playlist was left untouched: " + string.Join(" ", validation.Errors));
        }

        if (_userManager.GetUserById(config.OwnerUserId) is null)
        {
            throw new InvalidOperationException("Saturday Morning playlist owner no longer exists; the playlist was left untouched.");
        }

        var source = new JellyfinLibrarySource(_libraryManager);
        var missing = source.FindMissingLibraries(config.SourceLibraryIds.Append(config.CommercialsLibraryId));
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Saturday Morning libraries not found (" + string.Join(", ", missing) + "); the playlist was left untouched.");
        }

        progress.Report(5);
        var episodes = source.GetEpisodes(config.SourceLibraryIds);
        var commercials = source.GetCommercials(config.CommercialsLibraryId);
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(40);

        PlaylistPlan plan;
        try
        {
            plan = PlaylistPlanner.Plan(episodes, commercials, config.Selection, Random.Shared);
        }
        catch (SelectionException ex)
        {
            throw new InvalidOperationException(ex.Message + " The playlist was left untouched.", ex);
        }

        if (plan.CommercialPoolExhausted)
        {
            LogPoolExhausted(_logger, commercials.Count);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(60);

        var writer = new PlaylistWriter(_playlistManager);
        var (playlistId, created) = await writer
            .ReplaceAsync(config.OwnerUserId, config.PlaylistName, plan.Items.Select(i => i.Id).ToList())
            .ConfigureAwait(false);

        progress.Report(100);
        LogSummary(_logger, plan.EpisodeCount, plan.SeriesCount, plan.CommercialCount, plan.SkippedMissingDate, playlistId, created);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saturday Morning: commercials library has only {Count} items, fewer than this run needed; commercials repeat")]
    private static partial void LogPoolExhausted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saturday Morning: episodes={Episodes} series={Series} commercials={Commercials} skipped_missing_date={Skipped} playlist={PlaylistId} created={Created}")]
    private static partial void LogSummary(ILogger logger, int episodes, int series, int commercials, int skipped, Guid playlistId, bool created);
}
