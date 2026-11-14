# Saturday Morning for Jellyfin

Saturday morning cartoons, rebuilt every week inside Jellyfin. A scheduled task
builds a normal Jellyfin playlist that:

1. draws episodes only from the libraries (and, optionally, the series) you choose,
2. keeps only episodes that aired in a date range you set,
3. takes at most one episode per series (optional),
4. shuffles them, and
5. puts a random handful of vintage commercials between episodes. Commercials
   never interrupt an episode and never come before the first or after the last.

The commercials are videos on the Internet Archive. You list the archive.org
items on the plugin's settings page. A second scheduled task turns each video
into a `.strm` file in a commercials library, and clients stream them straight
from archive.org, so archive.org sees the client's network details. Nothing is
downloaded or re-hosted.

**All configuration happens in the Jellyfin web interface.** There are no
config files to edit and no scripts to run.

Requires **Jellyfin 12.1 or later** (`net10.0`, `targetAbi 12.1.0.0`). It does not
load on 10.x.

## Install

**See [INSTALL.md](INSTALL.md)** for the full walkthrough: plugin install
(from the repository manifest or manually from the zip), creating the
commercials library, configuration and troubleshooting.

- Repository manifest:
  `https://raw.githubusercontent.com/applemang/saturday-morning/main/manifest.json`
- Prebuilt plugin: [`dist/`](dist/) (zip + md5 + sha256), also attached to each
  [GitHub release](https://github.com/applemang/saturday-morning/releases).

## Settings at a glance

Dashboard > Plugins > Saturday Morning:

| Setting | Default | Notes |
|---|---|---|
| Source libraries | none (required) | Episodes come only from these |
| Every series in these libraries | on | Untick for a searchable checklist of the libraries' series |
| Episodes aired from / to | none (required) | Inclusive. Episodes only: commercials are chosen by the Internet Archive items |
| When an episode has no premiere date | Exclude | Or "use the series premiere year": kept when that year is within the range's years |
| One episode per series per run | on | Picked uniformly at random |
| Maximum episodes per run | 0 | 0 = no cap; applied after shuffling |
| Commercials library | none (required) | A Home Videos library with one writable folder |
| Internet Archive items | none | Identifier plus optional include/exclude file name patterns |
| Commercials between episodes | 2 to 3 | Random count per break |
| Avoid repeating a commercial within a run | on | If the library runs out, repeats are used and a warning is logged |
| Playlist name | Saturday Morning | Contents are replaced each run, never a new playlist |
| Playlist owner | none (required) | Must be able to see the source and commercials libraries |

Scheduled tasks (Dashboard > Scheduled Tasks > Saturday Morning, also
startable from the settings page):

| Task | Default schedule | What it does |
|---|---|---|
| Sync commercials from the Internet Archive | Saturday 05:00 | Reads each item from `https://archive.org/metadata/<identifier>` and picks one file per source video, preferring MP4/h.264. Writes one `.strm` per video into the commercials library's folder, deletes `.strm` files for items you removed, then scans that library and waits for the scan to finish. It touches nothing else in the folder, and changes nothing if any request fails. |
| Build Saturday Morning playlist | Saturday 06:00 | Scans the commercials library, then builds the playlist from the rules above |

Each `.strm` file holds exactly one line,
`https://archive.org/download/<identifier>/<url-encoded file name>`. File
names never contain "sample", because Jellyfin hides files with that word as
sample clips. A failed run leaves the playlist and the commercials folder
unchanged.

## Development

Uses the [jeltz](https://github.com/fortmort/jeltz) conventions: TDD, `make verify`.
Needs the .NET 10 SDK and Python 3.11+ (only for release tooling and the
integration harness).

```
python3 -m venv .venv && .venv/bin/pip install -r requirements-dev.txt
make verify        # ruff, mypy, dotnet format, Release build (warnings as errors), tests
make package       # artifacts/saturday-morning_<version>.zip + .md5
make integration   # real Jellyfin 12.1 in .integration/ (see below)
```

The selection rules (`Selection/`), config validation (`Configuration/`) and
the archive.org / `.strm` logic (`Commercials/`) are plain code over simple
records, with an injected `Random`, HTTP sender and delay. They have 100% unit
coverage. The thin Jellyfin layer (`Adapter/`, `Tasks/`, `Plugin.cs`) is
covered by `make integration`, which:

- starts a disposable Jellyfin 12.1 bound to 127.0.0.1;
- generates a small fake TV library with NFO air dates and some local `.strm`
  commercials;
- installs the plugin from a local plugin repository;
- checks every playlist rule, the series picker and failure handling;
- runs the commercials sync against a public-domain archive.org item
  (`SM_IT_OFFLINE=1` skips that part).

Unpack the portable builds first:

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
tests so you can try the settings page and play the playlist in Jellyfin Web
(tunnel port 18096).

### Releasing

Bump `<Version>` in `Directory.Build.props`, commit to `main`, then push an
annotated tag `v<version>` whose message is the changelog. The release workflow
verifies, packages, publishes the GitHub release (zip, md5, sha256), copies
the build into `dist/` and adds the version to `manifest.json` on `main`.

## Licence

GPL-3.0-or-later, the same as the Jellyfin plugin template. See `LICENSE`.
