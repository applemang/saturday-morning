using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Playlists;

namespace Jellyfin.Plugin.SaturdayMorning.Adapter;

/// <summary>
/// Replaces the contents of the owner's named playlist, creating it on first use.
/// </summary>
public sealed class PlaylistWriter
{
    private readonly IPlaylistManager _playlistManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistWriter"/> class.
    /// </summary>
    /// <param name="playlistManager">Playlist manager.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="userManager">User manager.</param>
    /// <param name="providerManager">Provider manager.</param>
    /// <param name="fileSystem">File system.</param>
    public PlaylistWriter(
        IPlaylistManager playlistManager,
        ILibraryManager libraryManager,
        IUserManager userManager,
        IProviderManager providerManager,
        IFileSystem fileSystem)
    {
        _playlistManager = playlistManager;
        _libraryManager = libraryManager;
        _userManager = userManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Writes the items to the playlist, in order. Duplicates are kept.
    /// </summary>
    /// <param name="ownerUserId">Playlist owner.</param>
    /// <param name="name">Playlist name.</param>
    /// <param name="itemIds">Ordered item ids.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The playlist id and whether it was created by this call.</returns>
    public async Task<(Guid PlaylistId, bool Created)> ReplaceAsync(
        Guid ownerUserId,
        string name,
        IReadOnlyList<Guid> itemIds,
        CancellationToken cancellationToken)
    {
        var existing = _playlistManager.GetPlaylists(ownerUserId)
            .Where(p => p.OwnerUserId.Equals(ownerUserId) && string.Equals(p.Name, name, StringComparison.Ordinal))
            .OrderBy(p => p.DateCreated)
            .FirstOrDefault();

        if (existing is null)
        {
            var result = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = name,
                UserId = ownerUserId,
                MediaType = MediaType.Video,
                ItemIdList = itemIds,
                Public = false,
            }).ConfigureAwait(false);
            return (Guid.Parse(result.Id), true);
        }

        // Not IPlaylistManager.UpdatePlaylist: with Ids set it saves playlist.xml, queues a
        // refresh that saves it again, then saves it a third time, and the last two overlap
        // ("file is being used by another process"). Same filtering as the playlist manager,
        // one save, then the usual refresh, which can only start after the save.
        var owner = _userManager.GetUserById(ownerUserId)
            ?? throw new InvalidOperationException("The playlist owner no longer exists.");
        existing.LinkedChildren = itemIds
            .Select(_libraryManager.GetItemById)
            .OfType<BaseItem>()
            .Where(i => i.IsVisible(owner) && i.SupportsAddingToPlaylist)
            .Select(LinkedChild.Create)
            .ToArray();
        existing.DateLastMediaAdded = DateTime.UtcNow;
        await existing.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);

        _providerManager.QueueRefresh(
            existing.Id,
            new MetadataRefreshOptions(new DirectoryService(_fileSystem)) { ForceSave = true },
            RefreshPriority.High);
        return (existing.Id, false);
    }
}
