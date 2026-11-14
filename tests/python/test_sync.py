from pathlib import Path

import pytest

from sm_commercials.sync import SyncError, SyncReport, sync_folder


@pytest.fixture
def folder(tmp_path: Path) -> Path:
    target = tmp_path / "commercials"
    target.mkdir()
    return target


def test_writes_new_files(folder: Path) -> None:
    report = sync_folder(folder, {"a.strm": "https://x/a\n", "b.strm": "https://x/b\n"})

    assert report == SyncReport(written=2, unchanged=0, removed=0)
    assert (folder / "a.strm").read_text() == "https://x/a\n"
    assert (folder / "b.strm").read_text() == "https://x/b\n"


def test_rerun_with_same_input_changes_nothing(folder: Path) -> None:
    desired = {"a.strm": "https://x/a\n"}
    sync_folder(folder, desired)
    before = (folder / "a.strm").stat().st_mtime_ns

    report = sync_folder(folder, desired)

    assert report == SyncReport(written=0, unchanged=1, removed=0)
    assert (folder / "a.strm").stat().st_mtime_ns == before


def test_updates_changed_content(folder: Path) -> None:
    sync_folder(folder, {"a.strm": "https://x/old\n"})

    report = sync_folder(folder, {"a.strm": "https://x/new\n"})

    assert report.written == 1
    assert (folder / "a.strm").read_text() == "https://x/new\n"


def test_removes_strm_files_no_longer_listed(folder: Path) -> None:
    sync_folder(folder, {"a.strm": "https://x/a\n", "b.strm": "https://x/b\n"})

    report = sync_folder(folder, {"a.strm": "https://x/a\n"})

    assert report == SyncReport(written=0, unchanged=1, removed=1)
    assert not (folder / "b.strm").exists()


def test_touches_nothing_else(folder: Path) -> None:
    (folder / "notes.txt").write_text("keep")
    (folder / "a.nfo").write_text("keep")
    sub = folder / "sub"
    sub.mkdir()
    (sub / "nested.strm").write_text("keep")

    sync_folder(folder, {})

    assert (folder / "notes.txt").read_text() == "keep"
    assert (folder / "a.nfo").read_text() == "keep"
    assert (sub / "nested.strm").read_text() == "keep"


def test_leaves_no_temporary_files(folder: Path) -> None:
    sync_folder(folder, {"a.strm": "https://x/a\n"})

    assert sorted(p.name for p in folder.iterdir()) == ["a.strm"]


def test_failed_write_cleans_up_and_keeps_old_file(
    folder: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    sync_folder(folder, {"a.strm": "https://x/old\n"})

    def boom(src: str, dst: object) -> None:
        raise OSError("disk full")

    monkeypatch.setattr("sm_commercials.sync.os.replace", boom)
    with pytest.raises(OSError, match="disk full"):
        sync_folder(folder, {"a.strm": "https://x/new\n"})

    assert sorted(p.name for p in folder.iterdir()) == ["a.strm"]
    assert (folder / "a.strm").read_text() == "https://x/old\n"


def test_missing_folder_is_an_error(tmp_path: Path) -> None:
    with pytest.raises(SyncError, match="does not exist"):
        sync_folder(tmp_path / "nope", {})


@pytest.mark.parametrize("bad", ["../escape.strm", "sub/a.strm", "a.txt", ".strm"])
def test_rejects_unsafe_or_non_strm_names(folder: Path, bad: str) -> None:
    with pytest.raises(SyncError, match="invalid"):
        sync_folder(folder, {bad: "https://x\n"})
