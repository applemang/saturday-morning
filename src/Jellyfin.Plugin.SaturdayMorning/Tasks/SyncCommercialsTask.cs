using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SaturdayMorning.Commercials;
using Jellyfin.Plugin.SaturdayMorning.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SaturdayMorning.Tasks;

/// <summary>
/// Scheduled task that writes one .strm file per configured Internet Archive video into the
/// commercials library's folder and rescans that library.
/// </summary>
public sealed partial class SyncCommercialsTask : IScheduledTask
{
    private const string UserAgent = "Jellyfin-SaturdayMorning/1.0 (+https://github.com/applemang/saturday-morning)";

    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SyncCommercialsTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncCommercialsTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="providerManager">Provider manager (library refresh).</param>
    /// <param name="fileSystem">File system.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public SyncCommercialsTask(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        IHttpClientFactory httpClientFactory,
        ILogger<SyncCommercialsTask> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Sync commercials from the Internet Archive";

    /// <inheritdoc />
    public string Key => "SaturdayMorningSyncCommercials";

    /// <inheritdoc />
    public string Description => "Writes a .strm file for each video of the Internet Archive items listed in the Saturday Morning settings, then rescans the commercials library.";

    /// <inheritdoc />
    public string Category => "Saturday Morning";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.WeeklyTrigger,
            DayOfWeek = DayOfWeek.Saturday,
            TimeOfDayTicks = TimeSpan.FromHours(5).Ticks,
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Saturday Morning plugin is not loaded.");
        var validation = ConfigValidator.ValidateSources(plugin.Configuration);
        if (validation.Errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Commercials are not configured; nothing was changed: " + string.Join(" ", validation.Errors));
        }

        var folder = ResolveFolder(validation.CommercialsLibraryId);
        var http = _httpClientFactory.CreateClient(NamedClient.Default);
        var client = new ArchiveClient(
            async (uri, token) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd(UserAgent);
                return await http.SendAsync(request, token).ConfigureAwait(false);
            },
            Task.Delay,
            retries: 4,
            backoff: TimeSpan.FromSeconds(2),
            pause: TimeSpan.FromSeconds(1));

        progress.Report(5);
        CommercialsPlan plan;
        SyncReport report;
        try
        {
            plan = await CommercialsPlanner.BuildAsync(validation.Sources, client, cancellationToken).ConfigureAwait(false);
            progress.Report(80);
            report = StrmFolderSync.Sync(folder, plan.Files);
        }
        catch (CommercialsException ex)
        {
            throw new InvalidOperationException(ex.Message + " The commercials folder was left as it was.", ex);
        }

        foreach (var source in plan.PerSource)
        {
            LogSource(_logger, source.Identifier, source.Videos);
        }

        if (report.Written + report.Removed > 0)
        {
            _providerManager.QueueRefresh(
                validation.CommercialsLibraryId,
                new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                {
                    MetadataRefreshMode = MetadataRefreshMode.Default,
                    ImageRefreshMode = MetadataRefreshMode.Default,
                },
                RefreshPriority.High);
        }

        progress.Report(100);
        LogSummary(_logger, report.Written, report.Unchanged, report.Removed, folder);
    }

    private string ResolveFolder(Guid libraryId)
    {
        var library = _libraryManager.GetVirtualFolders()
            .FirstOrDefault(f => Guid.TryParse(f.ItemId, out var id) && id == libraryId)
            ?? throw new InvalidOperationException("The commercials library no longer exists; nothing was changed.");
        if (library.Locations.Length != 1)
        {
            throw new InvalidOperationException(
                $"The commercials library '{library.Name}' must have exactly one folder (it has {library.Locations.Length}); nothing was changed.");
        }

        return library.Locations[0];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Saturday Morning: archive.org item {Identifier}: {Videos} video(s)")]
    private static partial void LogSource(ILogger logger, string identifier, int videos);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saturday Morning: commercials sync written={Written} unchanged={Unchanged} removed={Removed} folder={Folder}")]
    private static partial void LogSummary(ILogger logger, int written, int unchanged, int removed, string folder);
}
