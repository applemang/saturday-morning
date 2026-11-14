"""End-to-end checks against a real Jellyfin 12.1 server.

Tests run in file order and share one server: the first test runs the task while
the plugin is still unconfigured, later tests configure it.
"""

from __future__ import annotations

import urllib.request
from typing import Any

from conftest import (
    AIR_FROM,
    AIR_TO,
    ARCHIVE_SOURCE,
    ARCHIVE_STRM,
    LOCAL_ADS,
    PLUGIN_ID,
    ROOT,
    SERIES,
    Server,
    _wait,
)

TASK_KEY = "SaturdayMorningBuildPlaylist"
SYNC_KEY = "SaturdayMorningSyncCommercials"
PLAYLIST = "Saturday Morning"


def _version() -> str:
    props = (ROOT / "Directory.Build.props").read_text()
    return props.split("<Version>", 1)[1].split("</Version>", 1)[0]


def _configure(srv: Server, **overrides: Any) -> None:
    config = {
        "SourceLibraryIds": [srv.tv_library_id],
        "AirDateFrom": AIR_FROM,
        "AirDateTo": AIR_TO,
        "MissingDateFallback": "Exclude",
        "OnePerSeries": True,
        "MaxEpisodes": 0,
        "CommercialsLibraryId": srv.ads_library_id,
        "CommercialsMin": 2,
        "CommercialsMax": 3,
        "AvoidRepeatingCommercials": True,
        "PlaylistName": PLAYLIST,
        "PlaylistOwnerUserId": srv.api.user_id,
    }
    config.update(overrides)
    srv.api.call("POST", f"/Plugins/{PLUGIN_ID}/Configuration", body=config)


def _playlists(srv: Server) -> list[dict[str, Any]]:
    items = srv.api.call(
        "GET",
        "/Items",
        query={"IncludeItemTypes": "Playlist", "Recursive": "true", "userId": srv.api.user_id},
    )["Items"]
    return [p for p in items if p["Name"] == PLAYLIST]


def _items(srv: Server, playlist_id: str) -> list[dict[str, Any]]:
    result = srv.api.call(
        "GET",
        f"/Playlists/{playlist_id}/Items",
        query={"userId": srv.api.user_id, "Fields": "PremiereDate,Path,MediaSources"},
    )
    items: list[dict[str, Any]] = result["Items"]
    return items


def _eligible_series(fallback: bool) -> set[str]:
    eligible = set()
    for series, (premiered, episodes) in SERIES.items():
        for _, aired in episodes:
            if aired is not None and AIR_FROM <= aired <= AIR_TO:
                eligible.add(series)
            if aired is None and fallback and AIR_FROM[:4] <= premiered[:4] <= AIR_TO[:4]:
                eligible.add(series)
    return eligible


def _check_shape(items: list[dict[str, Any]], lo: int, hi: int, ads_pool: int) -> None:
    kinds = ["E" if i["Type"] == "Episode" else "C" for i in items]
    assert kinds[0] == "E" and kinds[-1] == "E", kinds
    breaks = "".join(kinds).strip("C").split("E")[1:-1]
    assert all(lo <= len(b) <= hi for b in breaks), kinds
    ads = [i["Id"] for i in items if i["Type"] != "Episode"]
    if len(ads) <= ads_pool:
        assert len(ads) == len(set(ads)), "commercial repeated although the pool sufficed"
    for episode in (i for i in items if i["Type"] == "Episode"):
        date = (episode.get("PremiereDate") or "")[:10]
        if date:
            assert AIR_FROM <= date <= AIR_TO, episode["Name"]


def _ads_pool(srv: Server) -> int:
    return LOCAL_ADS


def test_plugin_installed_from_catalog_with_default_trigger(server: Server) -> None:
    plugins = server.api.call("GET", "/Plugins")
    ours = [p for p in plugins if p["Id"].replace("-", "") == PLUGIN_ID.replace("-", "")]
    assert ours and ours[0]["Status"] == "Active" and ours[0]["Version"] == _version()

    tasks = {t["Key"]: t for t in server.api.call("GET", "/ScheduledTasks")}
    build, sync = tasks[TASK_KEY], tasks[SYNC_KEY]
    assert build["Name"] == "Build Saturday Morning playlist"
    assert build["Triggers"] == [
        {"Type": "WeeklyTrigger", "TimeOfDayTicks": 6 * 3600 * 10**7, "DayOfWeek": "Saturday"}
    ]
    assert sync["Name"] == "Sync commercials from the Internet Archive"
    assert sync["Triggers"] == [
        {"Type": "WeeklyTrigger", "TimeOfDayTicks": 5 * 3600 * 10**7, "DayOfWeek": "Saturday"}
    ]


def test_unconfigured_run_fails_and_creates_nothing(server: Server) -> None:
    result = server.run_task(TASK_KEY)

    assert result["Status"] == "Failed"
    assert "not configured" in result["ErrorMessage"]
    assert _playlists(server) == []


def test_unconfigured_sync_fails_and_leaves_folder_alone(server: Server) -> None:
    folder = server.extra_paths["commercials"]
    before = sorted(p.name for p in folder.iterdir())

    result = server.run_task(SYNC_KEY)

    assert result["Status"] == "Failed"
    assert "not configured" in result["ErrorMessage"]
    assert sorted(p.name for p in folder.iterdir()) == before


def test_run_builds_playlist_following_every_rule(server: Server) -> None:
    _configure(server)

    result = server.run_task(TASK_KEY)

    assert result["Status"] == "Completed", result
    playlists = _playlists(server)
    assert len(playlists) == 1
    items = _items(server, playlists[0]["Id"])
    episodes = [i for i in items if i["Type"] == "Episode"]
    assert {e["SeriesName"] for e in episodes} == _eligible_series(fallback=False)
    assert len(episodes) == len({e["SeriesId"] for e in episodes})
    _check_shape(items, 2, 3, _ads_pool(server))
    assert "Saturday Morning: episodes=3 series=3" in server.log_text()
    assert "skipped_missing_date=2" in server.log_text()


def test_rerun_replaces_contents_of_the_same_playlist(server: Server) -> None:
    first = _playlists(server)[0]["Id"]
    orders = set()
    for _ in range(3):
        assert server.run_task(TASK_KEY)["Status"] == "Completed"
        playlists = _playlists(server)
        assert [p["Id"] for p in playlists] == [first]
        items = _items(server, first)
        _check_shape(items, 2, 3, _ads_pool(server))
        orders.add(tuple(i["Id"] for i in items))
    assert len(orders) > 1, "three runs produced identical playlists; shuffle not applied"


def test_failed_run_leaves_existing_playlist_untouched(server: Server) -> None:
    playlist = _playlists(server)[0]["Id"]
    before = [i["PlaylistItemId"] for i in _items(server, playlist)]
    _configure(server, AirDateFrom="2001-01-01", AirDateTo="2002-12-31")

    result = server.run_task(TASK_KEY)

    assert result["Status"] == "Failed"
    assert "No episodes" in result["ErrorMessage"]
    assert [i["PlaylistItemId"] for i in _items(server, playlist)] == before


def test_series_year_fallback_all_episodes_and_cap(server: Server) -> None:
    _configure(server, MissingDateFallback="UseSeriesPremiereYear", OnePerSeries=False)
    assert server.run_task(TASK_KEY)["Status"] == "Completed"
    items = _items(server, _playlists(server)[0]["Id"])
    episodes = [i for i in items if i["Type"] == "Episode"]
    assert {e["SeriesName"] for e in episodes} == _eligible_series(fallback=True)
    assert len(episodes) == 8

    _configure(server, OnePerSeries=False, MaxEpisodes=2, CommercialsMin=1, CommercialsMax=1)
    assert server.run_task(TASK_KEY)["Status"] == "Completed"
    items = _items(server, _playlists(server)[0]["Id"])
    assert [i["Type"] == "Episode" for i in items] == [True, False, True]


def test_series_picker_limits_the_rotation(server: Server) -> None:
    series = server.api.call(
        "GET",
        "/Items",
        query={
            "ParentId": server.tv_library_id,
            "IncludeItemTypes": "Series",
            "Recursive": "true",
            "userId": server.api.user_id,
        },
    )["Items"]
    chosen = {s["Name"]: s["Id"] for s in series if s["Name"] in ("Laser Cats", "Space Cadets")}
    _configure(server, AllSeries=False, SelectedSeriesIds=list(chosen.values()), OnePerSeries=False)

    assert server.run_task(TASK_KEY)["Status"] == "Completed"

    items = _items(server, _playlists(server)[0]["Id"])
    episodes = [i for i in items if i["Type"] == "Episode"]
    # Space Cadets is picked but never aired in range, so only Laser Cats remains.
    assert {e["SeriesName"] for e in episodes} == {"Laser Cats"}
    assert len(episodes) == 3


def test_commercials_are_remote_strm_items_that_stream(server: Server) -> None:
    _configure(server, CommercialsMin=3, CommercialsMax=3)
    assert server.run_task(TASK_KEY)["Status"] == "Completed"
    items = _items(server, _playlists(server)[0]["Id"])
    ads = [i for i in items if i["Type"] != "Episode"]
    assert len(ads) == 6

    for ad in {a["Id"]: a for a in ads}.values():
        info = server.api.call(
            "POST",
            f"/Items/{ad['Id']}/PlaybackInfo",
            query={"userId": server.api.user_id},
            body={},
        )
        source = info["MediaSources"][0]
        assert source["Protocol"] == "Http", source
        assert source["Path"].startswith(("http://127.0.0.1", "https://archive.org/download/"))
        assert source["IsRemote"] is True

    episode = next(i for i in items if i["Type"] == "Episode")
    status, body, _ = server.api.raw(
        "GET",
        f"/Videos/{episode['Id']}/stream",
        query={"static": "true"},
        headers={"Range": "bytes=0-4095"},
    )
    assert status in (200, 206) and b"ftyp" in body[:64]


def test_sync_task_fills_the_commercials_library_from_archive_org(server: Server) -> None:
    if not server.with_archive:
        return
    folder = server.extra_paths["commercials"]
    (folder / "notes.txt").write_text("not a commercial")
    _configure(server, CommercialSources=[ARCHIVE_SOURCE], CommercialsMin=1, CommercialsMax=1)

    result = server.run_task(SYNC_KEY, timeout=300)

    assert result["Status"] == "Completed", result
    # The local test .strm files are replaced by exactly the configured archive videos;
    # other files are left alone.
    assert sorted(p.name for p in folder.iterdir()) == sorted([*ARCHIVE_STRM, "notes.txt"])
    assert (folder / ARCHIVE_STRM[0]).read_text() == (
        "https://archive.org/download/ctvc/ctvc_BAYER_512kb.mp4\n"
    )
    assert "commercials sync written=2 unchanged=0 removed=5" in server.log_text()

    def rescanned() -> bool:
        ads = server.api.call(
            "GET",
            "/Items",
            query={
                "ParentId": server.ads_library_id,
                "Recursive": "true",
                "IsFolder": "false",
                "MediaTypes": "Video",
                "userId": server.api.user_id,
                "Fields": "Path",
            },
        )["Items"]
        return sorted(a["Path"].rsplit("/", 1)[1] for a in ads) == ARCHIVE_STRM

    _wait(rescanned, 180, "commercials library rescan", interval=3)

    assert server.run_task(SYNC_KEY)["Status"] == "Completed"
    assert "commercials sync written=0 unchanged=2 removed=0" in server.log_text()

    assert server.run_task(TASK_KEY)["Status"] == "Completed"
    items = _items(server, _playlists(server)[0]["Id"])
    urls = []
    for ad in (i for i in items if i["Type"] != "Episode"):
        info = server.api.call(
            "POST",
            f"/Items/{ad['Id']}/PlaybackInfo",
            query={"userId": server.api.user_id},
            body={},
        )
        urls.append(info["MediaSources"][0]["Path"])
    assert urls and all(u.startswith("https://archive.org/download/ctvc/") for u in urls)

    # What a client does with that source: fetch it straight from archive.org.
    req = urllib.request.Request(
        urls[0], headers={"Range": "bytes=0-4095", "User-Agent": "sm-it/1.0"}
    )
    with urllib.request.urlopen(req, timeout=60) as resp:
        assert resp.status in (200, 206)
        assert b"ftyp" in resp.read(64)
