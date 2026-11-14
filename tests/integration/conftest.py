"""Disposable Jellyfin 12.1 for integration tests.

Runs the portable Jellyfin 12.1 build (no container runtime on the build VM) bound
to 127.0.0.1 only, against a generated fake TV library and a few .strm commercials,
then installs the plugin from a local plugin repository the same way a server
would install it from the published manifest.
"""

from __future__ import annotations

import functools
import http.server
import json
import os
import secrets
import shutil
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from collections.abc import Iterator
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import pytest

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / ".integration"
JELLYFIN = WORK / "jellyfin" / "jellyfin"
FFMPEG = WORK / "ffmpeg" / "ffmpeg"
PORT = int(os.environ.get("SM_IT_PORT", "18096"))
PLUGIN_ID = "3108dfcc-d642-4726-9ea3-d4b10ab9461b"
CLIENT = 'MediaBrowser Client="sm-it", Device="it", DeviceId="sm-it-1", Version="1.0"'

# series name -> (series premiere date, [(episode number, aired date or None)])
SERIES: dict[str, tuple[str, list[tuple[int, str | None]]]] = {
    "Laser Cats": ("1985-09-09", [(1, "1985-09-09"), (2, "1985-09-16"), (3, "1986-01-04")]),
    "Robo Pals": ("1987-02-01", [(1, "1987-02-01"), (2, "1987-02-08"), (3, "1992-05-05")]),
    "Edge Case Show": ("1989-06-01", [(1, "1989-12-31"), (2, "1990-01-01")]),
    "Space Cadets": ("1990-03-03", [(1, "1990-03-03"), (2, "1990-03-10")]),
    "Undated Toons": ("1986-04-04", [(1, None), (2, None)]),
}
AIR_FROM, AIR_TO = "1985-01-01", "1989-12-31"
LOCAL_ADS = 5
ARCHIVE_ADS = [
    # Public domain (archive.org item ctvc, licence publicdomain); 512Kb MPEG4 derivatives.
    "https://archive.org/download/ctvc/ctvc_BAYER_512kb.mp4",
    "https://archive.org/download/ctvc/ctvc_CHESTER_512kb.mp4",
]


def _ffmpeg_clip(path: Path, seconds: int, colour: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        [
            str(FFMPEG),
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-f",
            "lavfi",
            "-i",
            f"color=c={colour}:size=160x120:rate=10",
            "-f",
            "lavfi",
            "-i",
            "sine=frequency=440",
            "-t",
            str(seconds),
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            "-c:a",
            "aac",
            "-movflags",
            "+faststart",
            str(path),
        ],
        check=True,
    )


def _write_library(base: Path, ads_url: str, with_archive: bool) -> tuple[Path, Path]:
    tv = base / "tv"
    for series, (premiered, episodes) in SERIES.items():
        show = tv / series
        show.mkdir(parents=True, exist_ok=True)
        (show / "tvshow.nfo").write_text(
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
            f"<tvshow><title>{series}</title><premiered>{premiered}</premiered>"
            f"<year>{premiered[:4]}</year></tvshow>\n"
        )
        for number, aired in episodes:
            stem = show / "Season 01" / f"{series} - S01E{number:02d}"
            _ffmpeg_clip(stem.with_suffix(".mp4"), 3, "blue")
            aired_xml = f"<aired>{aired}</aired>" if aired else ""
            stem.with_suffix(".nfo").write_text(
                '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
                f"<episodedetails><title>Episode {number}</title><season>1</season>"
                f"<episode>{number}</episode>{aired_xml}</episodedetails>\n"
            )
    ads = base / "commercials"
    ads.mkdir(parents=True, exist_ok=True)
    www = base / "www"
    for i in range(1, LOCAL_ADS + 1):
        _ffmpeg_clip(www / f"ad{i}.mp4", 2, "red")
        (ads / f"local - ad{i}.strm").write_text(f"{ads_url}/ad{i}.mp4\n")
    if with_archive:
        for url in ARCHIVE_ADS:
            name = url.rsplit("/", 1)[1].removesuffix(".mp4")
            (ads / f"ctvc - {name}.strm").write_text(url + "\n")
    return tv, ads


@dataclass
class Api:
    """Minimal Jellyfin API client using the 12.x Authorization header."""

    base: str
    token: str | None = None
    user_id: str = ""
    extra: dict[str, Any] = field(default_factory=dict)

    def header(self) -> str:
        return CLIENT + (f', Token="{self.token}"' if self.token else "")

    def raw(
        self,
        method: str,
        path: str,
        query: dict[str, Any] | None = None,
        body: object = None,
        timeout: float = 60,
        headers: dict[str, str] | None = None,
    ) -> tuple[int, bytes, dict[str, str]]:
        url = self.base + path
        if query:
            url += "?" + urllib.parse.urlencode(query, doseq=True)
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(url, data=data, method=method)
        req.add_header("Authorization", self.header())
        if data is not None:
            req.add_header("Content-Type", "application/json")
        for k, v in (headers or {}).items():
            req.add_header(k, v)
        try:
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                return resp.status, resp.read(), dict(resp.headers)
        except urllib.error.HTTPError as exc:
            return exc.code, exc.read(), dict(exc.headers or {})

    def call(self, method: str, path: str, **kwargs: Any) -> Any:
        status, body, _ = self.raw(method, path, **kwargs)
        if status >= 400:
            raise AssertionError(f"{method} {path} -> {status}: {body[:300]!r}")
        return json.loads(body) if body else None


def _wait(predicate: Any, timeout: float, what: str, interval: float = 1.0) -> Any:
    deadline = time.monotonic() + timeout
    last: Any = None
    while time.monotonic() < deadline:
        try:
            last = predicate()
            if last:
                return last
        except (OSError, AssertionError, ValueError) as exc:
            last = exc
        time.sleep(interval)
    raise AssertionError(f"timed out waiting for {what}: {last!r}")


@dataclass
class Server:
    api: Api
    proc: subprocess.Popen[bytes]
    run_dir: Path
    www_url: str
    tv_library_id: str
    ads_library_id: str
    with_archive: bool

    def wait_up(self, timeout: float = 240) -> None:
        def up() -> bool:
            status, body, _ = self.api.raw("GET", "/System/Info/Public", timeout=5)
            return status == 200 and b"12.1" in body

        _wait(up, timeout, "Jellyfin to answer")

    def run_task(self, key: str, timeout: float = 120) -> dict[str, Any]:
        task = next(t for t in self.api.call("GET", "/ScheduledTasks") if t["Key"] == key)
        before = (task.get("LastExecutionResult") or {}).get("EndTimeUtc")
        self.api.call("POST", f"/ScheduledTasks/Running/{task['Id']}")

        def done() -> dict[str, Any] | None:
            t = self.api.call("GET", f"/ScheduledTasks/{task['Id']}")
            result = t.get("LastExecutionResult") or {}
            if t["State"] == "Idle" and result.get("EndTimeUtc") not in (None, before):
                return dict(result)
            return None

        result: dict[str, Any] = _wait(done, timeout, f"task {key}")
        return result

    def log_text(self) -> str:
        return "\n".join(
            p.read_text(errors="replace") for p in (self.run_dir / "log").glob("*.log")
        )


class _QuietHandler(http.server.SimpleHTTPRequestHandler):
    def log_message(self, format: str, *args: Any) -> None:
        return


def _serve(directory: Path) -> tuple[http.server.ThreadingHTTPServer, str]:
    handler = functools.partial(_QuietHandler, directory=str(directory))
    httpd = http.server.ThreadingHTTPServer(("127.0.0.1", 0), handler)
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    return httpd, f"http://127.0.0.1:{httpd.server_address[1]}"


def _package_plugin(www: Path, www_url: str) -> None:
    subprocess.run([str(ROOT / "scripts" / "package.sh"), "Integration build."], check=True)
    zip_path = next((ROOT / "artifacts").glob("saturday-morning_*.zip"))
    version = zip_path.stem.split("_", 1)[1]
    shutil.copy(zip_path, www / zip_path.name)
    subprocess.run(
        [
            str(ROOT / ".venv" / "bin" / "python"),
            "-m",
            "sm_release.manifest",
            "manifest",
            "--build",
            str(ROOT / "build.json"),
            "--version",
            version,
            "--target-abi",
            "12.1.0.0",
            "--changelog",
            "Integration build.",
            "--zip",
            str(www / zip_path.name),
            "--source-url",
            f"{www_url}/{zip_path.name}",
            "--manifest",
            str(www / "manifest.json"),
        ],
        check=True,
        env={**os.environ, "PYTHONPATH": str(ROOT / "builder")},
    )


def _library_options() -> dict[str, Any]:
    none: dict[str, list[str]] = {"MetadataFetchers": [], "ImageFetchers": []}
    return {
        "LibraryOptions": {
            "EnableRealtimeMonitor": False,
            "EnableChapterImageExtraction": False,
            "EnableTrickplayImageExtraction": False,
            "ExtractTrickplayImagesDuringLibraryScan": False,
            "EnableInternetProviders": False,
            "SaveLocalMetadata": False,
            "TypeOptions": [
                {"Type": t, **none} for t in ("Series", "Season", "Episode", "Video", "Movie")
            ],
        }
    }


@pytest.fixture(scope="session")
def server() -> Iterator[Server]:
    if not JELLYFIN.exists() or not FFMPEG.exists():
        pytest.skip("portable Jellyfin 12.1 / ffmpeg not unpacked under .integration/")
    with_archive = os.environ.get("SM_IT_OFFLINE") != "1"
    # Not under .integration: Jellyfin ignores media inside hidden (dot) folders.
    run_dir = ROOT / "it-runs" / f"run-{int(time.time())}"
    for sub in ("data", "config", "cache", "log", "www"):
        (run_dir / sub).mkdir(parents=True)
    # Bind to loopback only and keep discovery off: never expose the test server.
    (run_dir / "config" / "network.xml").write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n<NetworkConfiguration>'
        f"<InternalHttpPort>{PORT}</InternalHttpPort>"
        "<LocalNetworkAddresses><string>127.0.0.1</string></LocalNetworkAddresses>"
        "<AutoDiscovery>false</AutoDiscovery><EnableUPnP>false</EnableUPnP>"
        "<EnableIPv6>false</EnableIPv6></NetworkConfiguration>\n"
    )
    httpd, www_url = _serve(run_dir / "www")
    tv, ads = _write_library(run_dir / "media", www_url, with_archive)
    for f in (run_dir / "media" / "www").iterdir():
        shutil.move(str(f), run_dir / "www" / f.name)
    # SM_IT_INSTALL_ZIP follows the manual install instructions (unzip into
    # <data dir>/plugins/Saturday Morning_<version>/ before start).
    # SM_IT_MANIFEST_URL installs from a published manifest instead of a local build.
    manual_zip = os.environ.get("SM_IT_INSTALL_ZIP")
    if manual_zip:
        version = Path(manual_zip).stem.split("_", 1)[1]
        target = run_dir / "data" / "plugins" / f"Saturday Morning_{version}"
        target.mkdir(parents=True)
        with zipfile.ZipFile(manual_zip) as archive:
            archive.extractall(target)
    elif not os.environ.get("SM_IT_MANIFEST_URL"):
        _package_plugin(run_dir / "www", www_url)

    proc = subprocess.Popen(
        [
            str(JELLYFIN),
            "--datadir",
            str(run_dir / "data"),
            "--configdir",
            str(run_dir / "config"),
            "--cachedir",
            str(run_dir / "cache"),
            "--logdir",
            str(run_dir / "log"),
            "--webdir",
            str(JELLYFIN.parent / "jellyfin-web"),
            "--ffmpeg",
            str(FFMPEG),
        ],
        stdout=(run_dir / "stdout.txt").open("wb"),
        stderr=subprocess.STDOUT,
    )
    api = Api(f"http://127.0.0.1:{PORT}")
    srv = Server(api, proc, run_dir, www_url, "", "", with_archive)
    try:
        srv.wait_up()
        _wait(lambda: api.raw("GET", "/Startup/User")[0] == 200, 60, "startup wizard")
        api.call("POST", "/Startup/Configuration", body={"UICulture": "en-US"})
        # SM_IT_PASSWORD lets the owner log in to a held demo server; never printed.
        password = os.environ.get("SM_IT_PASSWORD") or secrets.token_urlsafe(18)
        api.call("POST", "/Startup/User", body={"Name": "admin", "Password": password})
        api.call("POST", "/Startup/Complete")
        auth = api.call(
            "POST", "/Users/AuthenticateByName", body={"Username": "admin", "Pw": password}
        )
        api.token, api.user_id = auth["AccessToken"], auth["User"]["Id"]

        # A dedicated API key, as on the real server; the builder uses it below.
        api.call("POST", "/Auth/Keys", query={"app": "saturday-morning-it"})
        key = next(
            k["AccessToken"]
            for k in api.call("GET", "/Auth/Keys")["Items"]
            if k["AppName"] == "saturday-morning-it"
        )
        api.extra["api_key"] = key

        api.call(
            "POST",
            "/Library/VirtualFolders",
            query={
                "name": "Cartoons",
                "collectionType": "tvshows",
                "paths": str(tv),
                "refreshLibrary": "true",
            },
            body=_library_options(),
        )
        srv.tv_library_id = next(
            f["ItemId"]
            for f in api.call("GET", "/Library/VirtualFolders")
            if f["Name"] == "Cartoons"
        )
        srv.ads_library_id = _register_commercials(srv, ads)
        expected = sum(len(e) for _, e in SERIES.values())

        def scanned() -> bool:
            eps = api.call(
                "GET",
                "/Items",
                query={"IncludeItemTypes": "Episode", "Recursive": "true", "userId": api.user_id},
            )
            ads_items = api.call(
                "GET",
                "/Items",
                query={
                    "ParentId": srv.ads_library_id,
                    "Recursive": "true",
                    "IsFolder": "false",
                    "MediaTypes": "Video",
                    "userId": api.user_id,
                },
            )
            want_ads = LOCAL_ADS + (len(ARCHIVE_ADS) if with_archive else 0)
            got = (eps["TotalRecordCount"], ads_items["TotalRecordCount"])
            if got != (expected, want_ads):
                raise AssertionError(f"episodes/commercials {got}, want {(expected, want_ads)}")
            return True

        _wait(scanned, 300, "library scan", interval=3)
        if manual_zip:
            _wait(lambda: _plugin_active(api), 120, "plugin active")
        else:
            _install_plugin(srv)
        yield srv
        if os.environ.get("SM_IT_HOLD") == "1":
            _hold(run_dir)
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=60)
        except subprocess.TimeoutExpired:
            proc.kill()
        httpd.shutdown()
        if os.environ.get("SM_IT_KEEP") != "1":
            shutil.rmtree(run_dir, ignore_errors=True)


def _hold(run_dir: Path) -> None:
    """Keep the server up for a manual Jellyfin Web check until STOP exists."""
    stop = run_dir / "STOP"
    sys.stderr.write(
        f"\nDemo server on http://127.0.0.1:{PORT} (user 'admin', password from "
        f"SM_IT_PASSWORD). Play the 'Saturday Morning' playlist in Jellyfin Web, then: "
        f"touch {stop}\n"
    )
    try:
        while not stop.exists():
            time.sleep(2)
    except KeyboardInterrupt:
        return


def _register_commercials(srv: Server, ads: Path) -> str:
    """Create the commercials library through the builder's own Jellyfin client."""
    from sm_commercials.http import HttpClient
    from sm_commercials.jellyfin import JellyfinClient

    client = JellyfinClient(
        srv.api.base, srv.api.extra["api_key"], HttpClient("sm-it", delay_seconds=0)
    )
    library_id, created = client.ensure_library("Commercials", str(ads))
    assert created
    assert client.ensure_library("Commercials", str(ads)) == (library_id, False)
    client.refresh_library(library_id)
    return library_id


def _install_plugin(srv: Server) -> None:
    api = srv.api
    manifest_url = os.environ.get("SM_IT_MANIFEST_URL") or f"{srv.www_url}/manifest.json"
    repos = api.call("GET", "/Repositories")
    repos.append({"Name": "Saturday Morning (local)", "Url": manifest_url, "Enabled": True})
    api.call("POST", "/Repositories", body=repos)
    packages = api.call("GET", "/Packages")
    ours = [p for p in packages if p["guid"].replace("-", "") == PLUGIN_ID.replace("-", "")]
    assert ours, "plugin not in catalog"
    assert [v["targetAbi"] for v in ours[0]["versions"]] == ["12.1.0.0"]
    api.call(
        "POST",
        "/Packages/Installed/Saturday%20Morning",
        query={"assemblyGuid": PLUGIN_ID, "repositoryUrl": manifest_url},
    )
    plugin_dirs = srv.run_dir / "data" / "plugins"
    _wait(lambda: list(plugin_dirs.glob("Saturday Morning_*/*.dll")), 60, "plugin download")
    api.call("POST", "/System/Restart")
    time.sleep(5)
    srv.wait_up()
    _wait(lambda: _plugin_active(api), 120, "plugin active")


def _plugin_active(api: Api) -> bool:
    plugins = api.call("GET", "/Plugins")
    want = PLUGIN_ID.replace("-", "")
    return any(p["Id"].replace("-", "") == want and p["Status"] == "Active" for p in plugins)
