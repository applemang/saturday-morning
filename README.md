# Saturday Morning for Jellyfin

Saturday morning cartoons, rebuilt every week inside Jellyfin. A scheduled task
builds a normal Jellyfin playlist that:

1. draws episodes only from the libraries you choose,
2. keeps only episodes that aired in a date range you set,
3. takes at most one episode per series (optional),
4. shuffles them, and
5. puts a random handful of vintage commercials between episodes. Commercials
   never interrupt an episode and never come before the first or after the last.

The commercials are videos on the Internet Archive, exposed to Jellyfin as
`.strm` files. Clients stream them straight from archive.org, so archive.org
sees the client's network details. Nothing is downloaded or re-hosted.

The project has two parts:

| Part | Where | What |
|---|---|---|
| Plugin | `src/Jellyfin.Plugin.SaturdayMorning` | Scheduled task, admin page |
| Commercials builder | `builder/sm_commercials`, `commercials.toml` | Writes the `.strm` folder and can register the library |

Requires **Jellyfin 12.1 or later** (`net10.0`, `targetAbi 12.1.0.0`). It does not
load on 10.x.

## Install

**See [INSTALL.md](INSTALL.md)** for the full walkthrough: plugin install
(from the repository manifest or manually from the zip), building the
commercials library, configuration and troubleshooting.

- Repository manifest:
  `https://raw.githubusercontent.com/applemang/saturday-morning/main/manifest.json`
- Prebuilt plugin: [`dist/`](dist/) (zip + md5 + sha256), also attached to each
  [GitHub release](https://github.com/applemang/saturday-morning/releases).

## Configure

Dashboard > Plugins > Saturday Morning:

| Setting | Default | Notes |
|---|---|---|
| Source libraries | none (required) | Episodes come only from these |
| Air date from / to | none (required) | Inclusive |
| When an episode has no premiere date | Exclude | Or "use the series premiere year": kept when that year is within the range's years |
| One episode per series per run | on | Picked uniformly at random |
| Maximum episodes per run | 0 | 0 = no cap; applied after shuffling |
| Commercials library | none (required) | |
| Commercials between episodes | 2 to 3 | Random count per break |
| Avoid repeating a commercial within a run | on | If the library runs out, repeats are used and a warning is logged |
| Playlist name | Saturday Morning | Contents are replaced each run, never a new playlist |
| Playlist owner | none (required) | Must be able to see the source and commercials libraries |

Schedule: Dashboard > Scheduled Tasks > Saturday Morning > "Build Saturday
Morning playlist". The default is weekly, Saturday 06:00 server time. Run it on
demand from the same page.

Each run logs one line, for example:

```
Saturday Morning: episodes=12 series=12 commercials=29 skipped_missing_date=3 playlist=... created=False
```

If a required setting is missing, the owner or a library no longer exists, or
nothing matches, the task fails with a message and leaves the existing
playlist as it was.

## Commercials builder

Python 3.11+, standard library only.

### Choose the commercials

`commercials.toml` ships with placeholders and the builder refuses to run until
you replace them. Each `[[item]]` is one Internet Archive item: its identifier is
the part after `/details/` in the item's URL.

```toml
delay_seconds = 1.0          # pause between archive.org requests

[[item]]
identifier = "some_item"     # every video in the item

[[item]]
identifier = "another_item"
include = ["*1983*"]         # optional: keep only matching file names
exclude = ["*promo*"]        # optional: drop matching file names
```

For each item the builder reads `https://archive.org/metadata/<identifier>` and
picks one file per source video. It prefers the MP4/h.264 derivatives and falls
back to other video formats. Thumbnails, torrents and metadata files are
skipped. Each chosen file becomes one `.strm` file holding exactly one line:

```
https://archive.org/download/<identifier>/<url-encoded file name>
```

File names look like `<identifier> - <file stem>.strm`. Names never contain
"sample", because Jellyfin hides files with that word as sample clips.

### Run it

```
python3 -m sm_commercials.cli --config commercials.toml --output /path/to/commercials
```

(from a checkout, with `PYTHONPATH=builder`, or after `pip install .` as
`sm-commercials`). Reruns make the folder match the list. New files are
written, changed URLs are updated, and `.strm` files for items you removed are
deleted. Other files and subfolders are never touched. If any archive.org
request fails, nothing is changed. Requests are sequential, delayed, retried
with backoff, and send a descriptive User-Agent.

### Register the library (optional)

```
JELLYFIN_API_KEY=... python3 -m sm_commercials.cli --config commercials.toml \
  --output /host/path/commercials --register-library \
  --jellyfin-url http://127.0.0.1:8096 --library-path /commercials
```

`--library-path` is the folder as the Jellyfin server sees it (the container
path). The builder creates a Home Videos library named "Commercials" if needed,
with chapter and trickplay image extraction off so scans don't download the
videos, then scans just that library. It uses the Jellyfin 12
`Authorization: MediaBrowser Token="..."` header. Use a dedicated API key and
pass it only through the environment.

### Update the commercials list

Edit `commercials.toml`, rerun the builder (with `--register-library` to trigger
a scan), and the next playlist run picks up the change.

Keep the `.strm` folder out of hidden (dot) directories: Jellyfin skips
anything under a path segment that starts with `.`.

## Development

Uses the [jeltz](https://github.com/fortmort/jeltz) conventions: TDD, `make verify`.

```
python3 -m venv .venv && .venv/bin/pip install -r requirements-dev.txt
make verify        # ruff, mypy, dotnet format, Release build (warnings as errors), tests
make package       # artifacts/saturday-morning_<version>.zip + .md5
make integration   # real Jellyfin 12.1 in .integration/ (see below)
```

The selection rules (`Selection/`) and config validation are plain functions
over simple records with an injected `Random`, and have 100% unit coverage. The
thin Jellyfin adapter, scheduled task and plugin entry point are covered by
`make integration`. It starts a disposable Jellyfin 12.1 bound to 127.0.0.1,
generates a small fake TV library with NFO air dates and some `.strm`
commercials, and installs the plugin from a local plugin repository. It then
checks every playlist rule, failure handling, and that the `.strm` items
resolve to remote HTTP streams, including two public-domain archive.org clips
(`SM_IT_OFFLINE=1` skips those). Unpack the portable builds first:

```
mkdir -p .integration && cd .integration
# ARM64 shown; use amd64 / linux64 builds on x86-64
curl -fLO https://repo.jellyfin.org/files/server/linux/stable/v12.1/arm64/jellyfin_12.1-arm64.tar.xz
curl -fLO https://repo.jellyfin.org/files/ffmpeg/linux/latest-7.x/arm64/jellyfin-ffmpeg_7.1.4-3_portable_linuxarm64-gpl.tar.xz
tar xJf jellyfin_12.1-arm64.tar.xz && mkdir ffmpeg && tar xJf jellyfin-ffmpeg_*.tar.xz -C ffmpeg
```

`SM_IT_MANIFEST_URL=<manifest url> make integration` installs from a published
manifest instead of a local build; `SM_IT_INSTALL_ZIP=<path to zip> make integration`
follows the manual install steps in INSTALL.md.
`SM_IT_PASSWORD=<pick one> make demo` keeps that server running after the
tests so you can play the playlist in Jellyfin Web (tunnel port 18096).

### Releasing

Bump `<Version>` in `Directory.Build.props`, commit to `main`, then push an
annotated tag `v<version>` whose message is the changelog. The release workflow
verifies, packages, publishes the GitHub release (zip, md5, sha256), copies
the build into `dist/` and adds the version to `manifest.json` on `main`.

## Licence

GPL-3.0-or-later, the same as the Jellyfin plugin template. See `LICENSE`.
