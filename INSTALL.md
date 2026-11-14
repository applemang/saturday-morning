# Installing Saturday Morning

This guide takes you from nothing to a working "Saturday Morning" playlist on
your own Jellyfin server. Everything after the install is done in the Jellyfin
web interface; there are no files to edit.

## Before you start

- **Jellyfin 12.1 or later.** The plugin targets .NET 10 / ABI 12.1.0.0 and will
  not load on Jellyfin 10.x. Check your version under Dashboard > Dashboard.
- **An administrator account** on the server.
- **A TV library** with episode air dates. Air dates come from your normal
  metadata (NFO files or online providers). Episodes without one can be
  excluded or matched by their series' premiere year.
- **An empty folder for commercials** that the Jellyfin server can **write**
  to (step 2). It must not be inside a hidden folder (any path segment starting
  with `.`), because Jellyfin skips those.
- **Privacy:** commercials stream straight from archive.org to each client, so
  archive.org sees your viewers' IP addresses and requests. The server itself
  only fetches item listings (metadata) from archive.org. Nothing is downloaded
  to or re-hosted on your server.

The build is architecture-independent (managed .NET only, no native code), so
the same zip works on x86-64 and ARM64, in Docker or on bare metal.

## Step 1: install the plugin

Pick either A or B.

### A. From the plugin repository (recommended: you get update notices)

1. Dashboard > Plugins > **Repositories** (or Catalog > gear icon) > **+**.
2. Name: `Saturday Morning`. URL:
   ```
   https://raw.githubusercontent.com/applemang/saturday-morning/main/manifest.json
   ```
3. Save, open **Catalog**, find **Saturday Morning** (category General), and
   click **Install**.
4. Restart Jellyfin (Dashboard > Dashboard > Restart, or restart the container).

### B. Manually from the zip

1. Download the zip from [`dist/`](dist/) in this repository or from the
   [latest GitHub release](https://github.com/applemang/saturday-morning/releases/latest).
2. Check it against the `.sha256` (or `.md5`) file next to it:
   ```
   sha256sum saturday-morning_<version>.zip
   ```
3. Find your server's **data path**: Dashboard > Dashboard, "Paths" section.
   The plugins folder is `<data path>/plugins`. Typical locations:

   | Install | Plugins folder |
   |---|---|
   | Official Docker image (`jellyfin/jellyfin`) | `/config/plugins` inside the container (on the host: whatever you mount at `/config`, then `plugins`) |
   | linuxserver.io image | `/config/data/plugins` inside the container |
   | Debian / Ubuntu package | `/var/lib/jellyfin/plugins` |
   | Windows | `C:\ProgramData\Jellyfin\Server\plugins` |

4. Create a folder named `Saturday Morning_<version>` (for example
   `Saturday Morning_1.1.0.0`) inside the plugins folder and extract the zip
   into it. It should contain exactly `Jellyfin.Plugin.SaturdayMorning.dll`
   and `meta.json`. Make sure the Jellyfin user can read the files.
5. Restart Jellyfin.

### Check it loaded

Dashboard > Plugins > **My Plugins** should list **Saturday Morning** as
Active. Dashboard > Scheduled Tasks should show a "Saturday Morning" section
with two tasks: **Sync commercials from the Internet Archive** and **Build
Saturday Morning playlist**. If the plugin shows "Not supported" or doesn't
appear, check that the server is 12.1+ and look in the server log for
`SaturdayMorning`.

## Step 2: create the commercials library

The plugin writes commercials into a library of their own: one small `.strm`
file per Internet Archive video, each holding that video's archive.org
address.

1. Create an empty folder the server can **write** to. On Docker, mount it
   **read-write** into the container, for example:
   ```
   -v /srv/jellyfin-commercials:/commercials      # docker run
   - /srv/jellyfin-commercials:/commercials       # compose "volumes:"
   ```
   Recreate the container after adding a mount.
2. Dashboard > Libraries > **Add Media Library**:
   - Content type: **Home Videos and Photos**
   - Display name: `Commercials`
   - Folders: just that one folder (`/commercials` in the example)
   - In the library options, turn **off** "Extract chapter images" and
     trickplay image extraction. Otherwise scans download every commercial from
     archive.org.
3. Save. The library stays empty until the sync task runs (step 4).

You can also put your own local video files in this library instead of, or as
well as, archive.org commercials. The sync task only ever adds, updates or
removes the `.strm` files at the top level of the folder, and leaves
everything else alone.

## Step 3: configure the plugin

Dashboard > Plugins > Saturday Morning (or My Plugins > Saturday Morning >
Settings).

**Episodes**

| Setting | What to set |
|---|---|
| Source libraries | Tick your cartoon / TV libraries |
| Every series in these libraries | Leave on to include every show (new shows join automatically), or untick it to get a checklist of the series in the ticked libraries. The list has a search box and "tick / untick all shown" buttons. |
| Air date from / to | For example 1984-01-01 to 1989-12-31 (inclusive) |
| When an episode has no premiere date | Exclude it, or use the series premiere year |
| One episode per series per run | On for the classic block feel |
| Maximum episodes per run | 0 for no limit, or e.g. 8 for a morning-sized block |

**Commercials**

| Setting | What to set |
|---|---|
| Commercials library | The library from step 2 |
| Internet Archive items | One row per item. **Identifier** is the part after `/details/` in the item's address, e.g. `archive.org/details/`**`classic_tv_ads`**. **Include** / **Exclude** take comma-separated file name patterns (`*1983*, *cereal*`). Leave them empty to use every video in the item. Click **Add item** for more rows. |
| Commercials between episodes | Minimum / maximum per break (default 2-3) |
| Avoid repeating a commercial within a run | On |

To find items, search archive.org for something like "television commercials"
in the Movies section. Check each item's licence. Where an item has several
versions of a video, the plugin picks the MP4 (h.264) one, which every client
can play directly.

**Playlist**

| Setting | What to set |
|---|---|
| Playlist name | Default `Saturday Morning` |
| Playlist owner | The user whose playlist this is. They need access to the source libraries and the commercials library. |

Click **Save**.

## Step 4: run it

At the bottom of the settings page:

1. **Sync commercials now** fetches the item listings from archive.org, writes
   the `.strm` files and rescans the commercials library. Give the scan a
   minute, then check that the library has items.
2. **Build playlist now** builds the playlist. Open it as the owner user and
   press play.

Both are ordinary scheduled tasks (Dashboard > Scheduled Tasks > Saturday
Morning), where you can see results and change the schedule. Defaults: sync
every Saturday at 05:00, build every Saturday at 06:00 (server time). Every
build replaces the contents of the same playlist; it never creates a second
one.

The server log gets one line per run, for example:

```
Saturday Morning: commercials sync written=37 unchanged=0 removed=0 folder=/commercials
Saturday Morning: episodes=8 series=8 commercials=18 skipped_missing_date=2 playlist=... created=False
```

## Changing things later

- **Commercials:** add, edit or remove rows on the settings page, save, and
  click **Sync commercials now**. Removed items' `.strm` files are deleted.
- **Series:** untick "Every series" and tick or untick shows, then save. The
  next build uses the new set.
- **Plugin updates:** with option A, Jellyfin shows them in the catalog. With
  option B, replace the `Saturday Morning_<version>` folder with the new
  version's and restart.

## Uninstalling

Dashboard > Plugins > My Plugins > Saturday Morning > Uninstall, then restart.
The playlist, the commercials library and its `.strm` files stay until you
delete them.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Build fails: "not configured" | A required setting is empty. The message lists each one. |
| Build fails: "No episodes in the selected series aired between" | Nothing in the chosen series has an air date in range. Check the range, the series checklist, or use the series-year fallback. |
| Build fails: "commercials library returned no items" | Run the sync first and let the scan finish, or check that the library folder isn't under a hidden (`.`) directory. |
| Sync fails: "Commercials are not configured" | Choose the commercials library and add at least one Internet Archive item. |
| Sync fails: "not found (no files listed)" | The identifier is wrong. Copy it from the item's archive.org address. |
| Sync fails: "has no matching video files" | Your include/exclude patterns removed every video of that item. |
| Sync fails: "must have exactly one folder" | Give the commercials library a single folder. |
| Sync fails with an access error | Jellyfin can't write to the commercials folder (read-only mount or permissions). |
| Fewer episodes than expected | One per series is on (by design), the series checklist excludes them, or the playlist owner can't see some libraries. |
| Warning "commercials repeat" | More commercials needed than the library holds. Add items or lower the per-break counts. |
| A commercial won't play on a client | It streams directly from archive.org. Check that the client can reach archive.org. A few archive items have no MP4 version, and those need server transcoding. |
| Plugin not in the catalog | The repository URL is wrong, or the server is older than 12.1 (versions with a newer target ABI are hidden). |

When a run fails, the playlist and the commercials folder are left exactly as
they were.

## Security notes

The plugin adds no web endpoints. Its settings page and "run now" buttons use
Jellyfin's existing admin APIs, so nothing new is exposed even on an
internet-facing server. The only outbound requests are to
`https://archive.org/metadata/<identifier>` for the items you list. They are
sent one at a time, one second apart, with a descriptive User-Agent, and
retried with backoff.
