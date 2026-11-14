from typing import Any

import pytest

from sm_commercials.archive import ArchiveError, VideoFile, metadata_url, select_video_files


def _f(name: str, fmt: str, original: str | None = None) -> dict[str, str]:
    entry = {"name": name, "format": fmt, "source": "derivative" if original else "original"}
    if original:
        entry["original"] = original
    return entry


def _meta(*files: dict[str, str]) -> dict[str, Any]:
    return {"metadata": {"identifier": "ads"}, "files": list(files)}


def test_metadata_url() -> None:
    assert metadata_url("classic_tv_ads") == "https://archive.org/metadata/classic_tv_ads"


def test_prefers_h264_derivative_over_original_and_other_derivatives() -> None:
    meta = _meta(
        _f("spot.avi", "Cinepack"),
        _f("spot.ogv", "Ogg Video", "spot.avi"),
        _f("spot.512kb.mp4", "512Kb MPEG4", "spot.avi"),
        _f("spot.mp4", "h.264", "spot.avi"),
    )
    assert select_video_files("ads", meta, (), ()) == [VideoFile("ads", "spot.mp4")]


def test_falls_back_through_mpeg4_ranks() -> None:
    meta = _meta(
        _f("a.mpeg", "MPEG2"),
        _f("a.512kb.mp4", "512Kb MPEG4", "a.mpeg"),
        _f("b.mp4", "MPEG4"),
        _f("b.512kb.mp4", "512Kb MPEG4", "b.mp4"),
    )
    assert select_video_files("ads", meta, (), ()) == [
        VideoFile("ads", "a.512kb.mp4"),
        VideoFile("ads", "b.mp4"),
    ]


def test_uses_other_video_format_when_no_mp4_exists() -> None:
    meta = _meta(_f("c.mkv", "Matroska"), _f("c.ogv", "Ogg Video", "c.mkv"))
    assert select_video_files("ads", meta, (), ()) == [VideoFile("ads", "c.mkv")]


def test_skips_thumbnails_torrents_metadata_and_unknown_formats() -> None:
    meta = _meta(
        _f("ads_thumb.jpg", "Thumbnail"),
        _f("__ia_thumb.jpg", "Item Tile"),
        _f("ads_archive.torrent", "Archive BitTorrent"),
        _f("ads_meta.xml", "Metadata"),
        _f("ads_files.xml", "Metadata"),
        _f("notes.txt", "Text"),
        _f("x.gif", "Animated GIF", "x.mp4"),
        _f("x.mp4", "h.264"),
    )
    assert select_video_files("ads", meta, (), ()) == [VideoFile("ads", "x.mp4")]


def test_include_filters_match_chosen_or_original_name_case_insensitively() -> None:
    meta = _meta(
        _f("Toys-1983.avi", "Cinepack"),
        _f("Toys-1983.mp4", "h.264", "Toys-1983.avi"),
        _f("Cereal-1984.mp4", "h.264"),
        _f("News.avi", "Cinepack"),
        _f("News.mp4", "h.264", "News.avi"),
    )
    chosen = select_video_files("ads", meta, ("*toys*.avi", "cereal*"), ())
    assert chosen == [VideoFile("ads", "Cereal-1984.mp4"), VideoFile("ads", "Toys-1983.mp4")]


def test_exclude_filters_win_over_include() -> None:
    meta = _meta(_f("Toys-1983.mp4", "h.264"), _f("Toys-1990.mp4", "h.264"))
    chosen = select_video_files("ads", meta, ("toys*",), ("*1990*",))
    assert chosen == [VideoFile("ads", "Toys-1983.mp4")]


def test_output_is_sorted_by_name() -> None:
    meta = _meta(_f("b.mp4", "h.264"), _f("a.mp4", "h.264"))
    assert [v.name for v in select_video_files("ads", meta, (), ())] == ["a.mp4", "b.mp4"]


def test_missing_files_key_means_item_not_found() -> None:
    with pytest.raises(ArchiveError, match="not found"):
        select_video_files("ads", {}, (), ())


def test_malformed_file_entries_are_ignored() -> None:
    meta: dict[str, Any] = {"files": [{"format": "h.264"}, "junk", {"name": 3, "format": "h.264"}]}
    assert select_video_files("ads", meta, (), ()) == []


def test_files_must_be_a_list() -> None:
    with pytest.raises(ArchiveError, match="malformed"):
        select_video_files("ads", {"files": "nope"}, (), ())
