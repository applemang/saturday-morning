# Jellyfin 12.1 API notes

Signatures this plugin and its scripts rely on, read from the Jellyfin source at
tag `v12.1` (commit `ee91c75e`, `AssemblyVersion 12.1.0`, 2026-09-14) and
confirmed by compiling against the `Jellyfin.Controller` / `Jellyfin.Model`
12.1.0 NuGet packages. Recheck this file before moving to a newer Jellyfin.

## Build targets

- Target framework `net10.0`; host packages referenced with
  `<ExcludeAssets>runtime</ExcludeAssets>`.
- `targetAbi` `12.1.0.0`. The official plugin repository (checked 2026-11-14)
  lists 12.x plugins with `12.0.0.0`, `12.1.0.0` and `12.2.0.0`.
  `InstallationManager.GetPackages` hides versions whose `targetAbi` is higher
  than the server version, so `12.1.0.0` installs on 12.1 and later.
- Repository manifest `checksum` is an MD5 hex digest
  (`InstallationManager` compares `MD5.HashDataAsync` output, case-insensitive).
- The plugin template's 12.x branch (`renovate/major-jellyfin-12.4.0`) keeps the
  same `BasePlugin<T>(IApplicationPaths, IXmlSerializer)` constructor and
  `IHasWebPages` / `PluginPageInfo` shape as 10.11.

## MediaBrowser.Model.Tasks.IScheduledTask

```csharp
string Name { get; }
string Key { get; }
string Description { get; }
string Category { get; }
Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken);
IEnumerable<TaskTriggerInfo> GetDefaultTriggers();
```

`TaskTriggerInfo { TaskTriggerInfoType Type; long? TimeOfDayTicks; long? IntervalTicks;
DayOfWeek? DayOfWeek; long? MaxRuntimeTicks; }`. `TaskTriggerInfoType` is an enum
(`DailyTrigger`, `WeeklyTrigger`, `IntervalTrigger`, `StartupTrigger`). It was a string
constant in 10.x. Plugin `IScheduledTask` implementations are discovered automatically.

## MediaBrowser.Controller.Playlists.IPlaylistManager

```csharp
Playlist GetPlaylistForUser(Guid playlistId, Guid userId);
Task<PlaylistCreationResult> CreatePlaylist(PlaylistCreationRequest request);
Task UpdatePlaylist(PlaylistUpdateRequest request);
IEnumerable<Playlist> GetPlaylists(Guid userId);
Task AddItemToPlaylistAsync(Guid playlistId, IReadOnlyCollection<Guid> itemIds, int? position, Guid userId);
Task RemoveItemFromPlaylistAsync(string playlistId, IEnumerable<string> entryIds);
```

- `PlaylistCreationRequest { string? Name; IReadOnlyList<Guid> ItemIdList; MediaType? MediaType;
  Guid UserId; IReadOnlyList<PlaylistUserPermissions> Users; bool? Public }`.
- `PlaylistUpdateRequest { Guid Id; Guid UserId; string? Name; IReadOnlyList<Guid>? Ids; ... }`.
  When `Ids` is set, `UpdatePlaylist` empties `LinkedChildren`, saves, then
  appends `Ids` in order. This is how the task replaces the contents.
- Duplicates are kept. `AddToPlaylistInternal` does no de-duplication, so a
  commercial can appear more than once.
- Items not visible to the owner (`IsVisible(user)`) or with
  `SupportsAddingToPlaylist == false` are dropped silently. `Video` (which
  covers `.strm` items in a Home Videos library) returns true.
- `GetPlaylists(userId)` returns every playlist the user can see (admins see
  all), so the task also filters on `OwnerUserId` and `Name`.
- The clear-then-append in `UpdatePlaylist` is not atomic. The task computes
  and validates the whole plan before calling it, so a run that fails for lack
  of data never touches the playlist.

## MediaBrowser.Controller.Library.ILibraryManager

```csharp
BaseItem? GetItemById(Guid id);
IReadOnlyList<BaseItem> GetItemList(InternalItemsQuery query);
List<VirtualFolderInfo> GetVirtualFolders();
```

With `Recursive = true` and `ParentId` set to a library (collection folder),
`GetItemList` rewrites the query to that library's top parent ids, so one query
per library is used.

## MediaBrowser.Controller.Entities.InternalItemsQuery (fields used)

`ParentId (Guid)`, `Recursive (bool)`, `IncludeItemTypes (BaseItemKind[])`,
`MediaTypes (MediaType[])`, `IsFolder (bool?)`, `IsVirtualItem (bool?)`,
`DtoOptions`.

## Entities

- `Episode`: `SeriesId (Guid)`, `Series` (looked up through the library manager),
  `PremiereDate (DateTime?)` inherited from `BaseItem`.
- `Series`: `PremiereDate (DateTime?)`, `ProductionYear (int?)`.
- `VirtualFolderInfo.ItemId` is the library id (string, 32 hex digits).

## HTTP API (scripts and integration tests)

- Auth: `Authorization: MediaBrowser Token="<key>"`. Migration
  `20260531160000_DisableLegacyAuthorization` sets
  `EnableLegacyAuthorization = false`, so `X-Emby-*` headers are rejected.
- `GET /Library/VirtualFolders`; `POST /Library/VirtualFolders?name=&collectionType=&paths=&refreshLibrary=`
  with body `{"LibraryOptions": {...}}` (policy `FirstTimeSetupOrElevated`, so
  the key needs admin rights, which API keys have).
- `POST /Items/{itemId}/Refresh?Recursive=true&MetadataRefreshMode=Default&ImageRefreshMode=Default`
  scans one library.
- `GET /Repositories`, `POST /Repositories` (replaces the whole list, so GET,
  append, POST), `GET /Packages`, `POST /Packages/Installed/{name}?assemblyGuid=&version=&repositoryUrl=`.
  Package GUIDs come back without dashes. The install takes effect after
  `POST /System/Restart` (or a container restart).
- `GET/POST /Plugins/{id}/Configuration`, `GET /ScheduledTasks`,
  `POST /ScheduledTasks/Running/{taskId}`, `GET /Playlists/{id}/Items`,
  `POST /Items/{id}/PlaybackInfo`.

## Behaviour observed on 12.1 (integration test)

- Media under a dot-folder (any path segment starting with `.`) is ignored by
  the scanner. The `.strm` folder must not live under a hidden directory.
- A `.strm` file in a Home Videos library becomes a `Video` item. Its own
  `Path` is the `.strm` file, while `PlaybackInfo` returns
  `MediaSources[0].Path` = the URL, `Protocol = Http`, `IsRemote = true`, so
  clients stream straight from the URL.
- Plugin configuration enums serialise as strings over the API
  (`"MissingDateFallback": "Exclude"`).
- Serilog renders string arguments in quotes, so the run summary logs each
  count as its own structured value.
