using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;

namespace Jellyfin.Plugin.SaturdayMorning.Adapter;

/// <summary>
/// Scans one library now and waits for the scan to finish.
/// </summary>
/// <remarks>
/// Jellyfin's per-library refresh (<c>QueueRefresh</c>, the dashboard's "Scan library") only
/// queues work, and it scans the library's cached folder items, which still point at the old
/// folder after the library's folder is changed. This does what "Scan Media Library" does for
/// one library: refresh the library folders, then validate each of the library's folders with
/// a fresh <see cref="DirectoryService"/>, and wait for it.
/// </remarks>
public sealed class LibraryScanner
{
    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryScanner"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="fileSystem">File system.</param>
    public LibraryScanner(ILibraryManager libraryManager, IFileSystem fileSystem)
    {
        _libraryManager = libraryManager;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Scans the library and returns when Jellyfin has added, updated and removed its items.
    /// </summary>
    /// <param name="libraryId">Library (collection folder) id.</param>
    /// <param name="progress">Scan progress, 0 to 100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the scan is done.</returns>
    public async Task ScanAsync(Guid libraryId, IProgress<double> progress, CancellationToken cancellationToken)
    {
        // First refresh each library's folder list. Until then a library whose folder was
        // changed still points at its old folder (CollectionFolder.PhysicalFolderIds).
        await _libraryManager.ValidateTopLibraryFolders(cancellationToken).ConfigureAwait(false);

        if (_libraryManager.GetItemById(libraryId) is not CollectionFolder library)
        {
            throw new InvalidOperationException("The commercials library no longer exists.");
        }

        // A library item's own ValidateChildren does nothing; its folders hold the files.
        var folders = library.GetPhysicalFolders().ToList();
        for (var i = 0; i < folders.Count; i++)
        {
            var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
            {
                MetadataRefreshMode = MetadataRefreshMode.Default,
                ImageRefreshMode = MetadataRefreshMode.Default,
            };
            var done = i;
            var count = folders.Count;
            await folders[i]
                .ValidateChildren(new Progress<double>(p => progress.Report(((done * 100) + p) / count)), options, recursive: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        progress.Report(100);
    }
}
