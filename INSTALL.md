# Installing Saturday Morning

This guide takes you from nothing to a working "Saturday Morning" playlist on
your own Jellyfin server.

## Before you start

- **Jellyfin 12.1 or later.** The plugin targets .NET 10 / ABI 12.1.0.0 and will
  not load on Jellyfin 10.x. Check your version under Dashboard > Dashboard.
- **An administrator account** on the server.
- **A TV library** with episode air dates. Air dates come from your normal
  metadata (NFO files or online providers). Episodes without one can be
  excluded or matched by their series' premiere year.
- **Python 3.11+** on any machine that can write to a folder your Jellyfin
  server reads. It's only needed for the commercials builder (step 2), and the
  builder uses only the standard library.
- **Privacy:** commercials stream straight from archive.org to each client, so
  archive.org sees your viewers' IP addresses and requests. Nothing is
  downloaded to or re-hosted on your server.

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

1. Download `saturday-morning_1.0.0.0.zip` from
   [`dist/`](dist/) in this repository or from the
   [GitHub release](https://github.com/applemang/saturday-morning/releases/tag/v1.0.0.0).
2. Check it. Either checksum is enough:
   ```
   sha256sum saturday-morning_1.0.0.0.zip   # compare with dist/saturday-morning_1.0.0.0.zip.sha256
   md5sum saturday-morning_1.0.0.0.zip      # ea3bc8a2cc59568aae7644c791b344f4
   ```
3. Find your server's **data path**: Dashboard > Dashboard, "Paths" section.
   The plugins folder is `<data path>/plugins`. Typical locations:

   | Install | Plugins folder |
   |---|---|
   | Official Docker image (`jellyfin/jellyfin`) | `/config/plugins` inside the container (on the host: whatever you mount at `/config`, then `plugins`) |
   | linuxserver.io image | `/config/data/plugins` inside the container |
   | Debian / Ubuntu package | `/var/lib/jellyfin/plugins` |
   | Windows | `C:\ProgramData\Jellyfin\Server\plugins` |

4. Create a folder named `Saturday Morning_1.0.0.0` inside the plugins folder
   and extract the zip into it. It should contain exactly
   `Jellyfin.Plugin.SaturdayMorning.dll` and `meta.json`. Make sure the
   Jellyfin user can read the files.
5. Restart Jellyfin.

### Check it loaded

Dashboard > Plugins > **My Plugins** should list **Saturday Morning 1.0.0.0**
as Active, and Dashboard > Scheduled Tasks should show a "Saturday Morning"
section with **Build Saturday Morning playlist**. If it shows "Not supported"
or doesn't appear, check that the server is 12.1+ and look in the server log
for `SaturdayMorning`.

## Step 2: build the commercials library

The plugin draws commercials from any Jellyfin library you choose. Normally
that's a folder of `.strm` files, each pointing at one Internet Archive video.
The builder in this repository creates and maintains that folder.

1. Get the code:
   ```
   git clone https://github.com/applemang/saturday-morning.git
   cd saturday-morning
   ```
2. Choose your commercials. Open `commercials.toml` and replace each
   `REPLACE_ME_...` identifier with a real Internet Archive item: the part
   after `/details/` in a URL such as
   `https://archive.org/details/<identifier>`. Searching archive.org for
   "television commercials" in the Movies section is a good start. Check each
   item's licence. Optional `include` / `exclude` patterns narrow which files of
   an item are used:
   ```toml
   [[item]]
   identifier = "some_item"
   include = ["*1983*"]
   exclude = ["*promo*"]
   ```
   The builder refuses to run while any placeholder remains.
3. Create an empty folder the Jellyfin server can read. It must **not** be
   inside a hidden folder (any path segment starting with `.`), because
   Jellyfin skips those. For Docker, mount it into the container. Read-only is
   fine:
   ```
   -v /srv/jellyfin-commercials:/commercials:ro      # docker run
   - /srv/jellyfin-commercials:/commercials:ro       # compose "volumes:"
   ```
4. Run the builder (on the host, writing to the host-side folder):
   ```
   PYTHONPATH=builder python3 -m sm_commercials.cli --config commercials.toml --output /srv/jellyfin-commercials
   ```
   You get one `.strm` file per video, each holding a single line like
   `https://archive.org/download/<identifier>/<file>`. Requests are sequential
   and rate-limited. If archive.org fails, the folder is left unchanged.
5. Add the library to Jellyfin. Do either a or b.

   **a. In the dashboard.** Dashboard > Libraries > Add Media Library. Content
   type: **Home Videos and Photos**. Name: `Commercials`. Folder: the path as
   Jellyfin sees it (`/commercials` in the Docker example). Under the library
   options, turn **off** chapter image extraction and trickplay image
   extraction, otherwise scans download every video from archive.org. Save.

   **b. Let the builder do it.** Create a dedicated API key (Dashboard > API
   Keys > +), then:
   ```
   export JELLYFIN_API_KEY='<your key>'
   PYTHONPATH=builder python3 -m sm_commercials.cli --config commercials.toml --output /srv/jellyfin-commercials --register-library --jellyfin-url https://jellyfin.example.com --library-path /commercials
   ```
   This creates the "Commercials" library (Home Videos, extraction off) if it
   doesn't exist and scans only that library. The key goes in the standard
   Jellyfin 12 header (`Authorization: MediaBrowser Token=...`). It is never
   logged or written to disk.

## Step 3: configure the plugin

Dashboard > Plugins > Saturday Morning (or My Plugins > Saturday Morning >
Settings):

| Setting | What to set |
|---|---|
| Source libraries | Tick your cartoon / TV libraries |
| Air date from / to | For example 1984-01-01 to 1989-12-31 (inclusive) |
| When an episode has no premiere date | Exclude it, or use the series premiere year |
| One episode per series per run | On for the classic block feel |
| Maximum episodes per run | 0 for no limit, or e.g. 8 for a morning-sized block |
| Commercials library | The library from step 2 |
| Commercials between episodes | Minimum / maximum per break (default 2-3) |
| Avoid repeating a commercial within a run | On |
| Playlist name | Default `Saturday Morning` |
| Playlist owner | The user whose playlist this is. They need access to both libraries. |

Save.

## Step 4: run it

Dashboard > Scheduled Tasks > Saturday Morning > **Build Saturday Morning
playlist** > run (play icon). Then open the playlist as the owner user and
press play.

The default schedule is every Saturday at 06:00 server time. Change it on the
same task page with the usual trigger editor. Every run replaces the contents
of the same playlist; it never creates a second one.

The server log gets one line per run:

```
Saturday Morning: episodes=8 series=8 commercials=18 skipped_missing_date=2 playlist=... created=False
```

## Updating

- **Plugin:** with option A, Jellyfin shows updates in the catalog. With
  option B, replace the folder with the new version's
  `Saturday Morning_<version>` and restart.
- **Commercials:** edit `commercials.toml` and rerun the builder. New videos are
  added, `.strm` files for items you removed are deleted, and other files in
  the folder are never touched. Then rescan the library (or pass
  `--register-library ...` again).

## Uninstalling

Dashboard > Plugins > My Plugins > Saturday Morning > Uninstall, then restart.
The playlist and the commercials library stay until you delete them.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Task fails: "not configured" | A required setting is empty. The message lists each one. |
| Task fails: "No episodes ... aired between" | Nothing in the selected libraries has an air date in range. Check episode metadata, or use the series-year fallback. |
| Task fails: "commercials library returned no items" | The library is empty or not scanned yet, or the folder sits under a hidden (`.`) directory. |
| Fewer episodes than expected | One per series is on (by design), or the playlist owner can't see some libraries. |
| Warning "commercials repeat" | More commercials needed than the library holds. Add items or lower the per-break counts. |
| A commercial won't play on a client | It streams directly from archive.org. Check that the client can reach archive.org. A few archive items have no MP4 version, and those need server transcoding. |
| Plugin not in the catalog | The repository URL is wrong, or the server is older than 12.1 (versions with a newer target ABI are hidden). |

## Security notes

The plugin adds no web endpoints. Its settings page uses Jellyfin's existing
admin APIs, so nothing new is exposed even on an internet-facing server. Use a
dedicated API key for the builder, pass it through the environment, and delete
it from Dashboard > API Keys if you stop using `--register-library`.
