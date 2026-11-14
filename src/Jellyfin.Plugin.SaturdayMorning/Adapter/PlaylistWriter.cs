using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;

namespace Jellyfin.Plugin.SaturdayMorning.Adapter;

/// <summary>
/// Replaces the contents of the owner's named playlist, creating it on first use.
/// </summary>
public sealed class PlaylistWriter
{
    private readonly IPlaylistManager _playlistManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistWriter"/> class.
    /// </summary>
    /// <param name="playlistManager">Playlist manager.</param>
    public PlaylistWriter(IPlaylistManager playlistManager)
    {
        _playlistManager = playlistManager;
    }

    /// <summary>
    /// Writes the items to the playlist, in order. Duplicates are kept.
    /// </summary>
    /// <param name="ownerUserId">Playlist owner.</param>
    /// <param name="name">Playlist name.</param>
    /// <param name="itemIds">Ordered item ids.</param>
    /// <returns>The playlist id and whether it was created by this call.</returns>
    public async Task<(Guid PlaylistId, bool Created)> ReplaceAsync(Guid ownerUserId, string name, IReadOnlyList<Guid> itemIds)
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

        await _playlistManager.UpdatePlaylist(new PlaylistUpdateRequest
        {
            Id = existing.Id,
            UserId = ownerUserId,
            Ids = itemIds,
        }).ConfigureAwait(false);
        return (existing.Id, false);
    }
}
