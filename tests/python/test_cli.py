import json
import logging
from pathlib import Path
from typing import Any

import pytest

from sm_commercials import cli
from sm_commercials.http import HttpError


class FakeHttp:
    def __init__(self, routes: dict[str, list[bytes | Exception]]) -> None:
        self.routes = routes
        self.calls: list[tuple[str, str]] = []

    def request(
        self,
        method: str,
        url: str,
        *,
        headers: dict[str, str] | None = None,
        body: bytes | None = None,
    ) -> bytes:
        self.calls.append((method, url))
        for prefix, outcomes in self.routes.items():
            if url.startswith(prefix):
                outcome = outcomes.pop(0)
                if isinstance(outcome, Exception):
                    raise outcome
                return outcome
        raise AssertionError(f"unexpected request {method} {url}")

    def get_json(self, url: str, headers: dict[str, str] | None = None) -> Any:
        return json.loads(self.request("GET", url, headers=headers))


def _meta(*names: str) -> bytes:
    return json.dumps({"files": [{"name": n, "format": "h.264"} for n in names]}).encode()


@pytest.fixture
def setup(tmp_path: Path) -> tuple[Path, Path]:
    config = tmp_path / "commercials.toml"
    config.write_text(
        'delay_seconds = 0\n[[item]]\nidentifier = "ads1"\n'
        '[[item]]\nidentifier = "ads2"\ninclude = ["keep*"]\n'
    )
    out = tmp_path / "out"
    out.mkdir()
    return config, out


def test_builds_strm_files_from_all_items(setup: tuple[Path, Path]) -> None:
    config, out = setup
    http = FakeHttp(
        {
            "https://archive.org/metadata/ads1": [_meta("a.mp4")],
            "https://archive.org/metadata/ads2": [_meta("keep me.mp4", "drop.mp4")],
        }
    )

    rc = cli.main(["--config", str(config), "--output", str(out)], http=http)

    assert rc == 0
    assert sorted(p.name for p in out.iterdir()) == ["ads1 - a.strm", "ads2 - keep me.strm"]
    assert (out / "ads2 - keep me.strm").read_text() == (
        "https://archive.org/download/ads2/keep%20me.mp4\n"
    )


def test_fetch_failure_aborts_without_touching_folder(setup: tuple[Path, Path]) -> None:
    config, out = setup
    (out / "old.strm").write_text("https://archive.org/download/x/y.mp4\n")
    http = FakeHttp(
        {
            "https://archive.org/metadata/ads1": [_meta("a.mp4")],
            "https://archive.org/metadata/ads2": [HttpError("boom")],
        }
    )

    rc = cli.main(["--config", str(config), "--output", str(out)], http=http)

    assert rc == 1
    assert sorted(p.name for p in out.iterdir()) == ["old.strm"]


def test_item_with_no_videos_aborts(setup: tuple[Path, Path]) -> None:
    config, out = setup
    http = FakeHttp(
        {
            "https://archive.org/metadata/ads1": [_meta("a.mp4")],
            "https://archive.org/metadata/ads2": [_meta("drop.mp4")],
        }
    )

    assert cli.main(["--config", str(config), "--output", str(out)], http=http) == 1
    assert list(out.iterdir()) == []


def test_bad_config_returns_2(tmp_path: Path) -> None:
    out = tmp_path / "out"
    out.mkdir()
    args = ["--config", str(tmp_path / "none.toml"), "--output", str(out)]
    assert cli.main(args, http=FakeHttp({})) == 2


def test_missing_output_folder_returns_1(setup: tuple[Path, Path], tmp_path: Path) -> None:
    config, _ = setup
    http = FakeHttp(
        {
            "https://archive.org/metadata/ads1": [_meta("a.mp4")],
            "https://archive.org/metadata/ads2": [_meta("keep.mp4")],
        }
    )
    rc = cli.main(["--config", str(config), "--output", str(tmp_path / "nope")], http=http)
    assert rc == 1


def test_register_library_requires_api_key_env(
    setup: tuple[Path, Path], monkeypatch: pytest.MonkeyPatch
) -> None:
    config, out = setup
    monkeypatch.delenv("JELLYFIN_API_KEY", raising=False)
    args = ["--config", str(config), "--output", str(out), "--register-library"]
    args += ["--jellyfin-url", "https://jf", "--library-path", "/commercials"]

    assert cli.main(args, http=FakeHttp({})) == 2


def test_register_library_needs_url_and_path(
    setup: tuple[Path, Path], monkeypatch: pytest.MonkeyPatch
) -> None:
    config, out = setup
    monkeypatch.setenv("JELLYFIN_API_KEY", "k")
    args = ["--config", str(config), "--output", str(out), "--register-library"]

    assert cli.main(args, http=FakeHttp({})) == 2


def test_register_library_creates_and_scans(
    setup: tuple[Path, Path], monkeypatch: pytest.MonkeyPatch, caplog: pytest.LogCaptureFixture
) -> None:
    config, out = setup
    monkeypatch.setenv("JELLYFIN_API_KEY", "supersecretvalue")
    folders = json.dumps([{"Name": "Commercials", "ItemId": "lib1", "Locations": ["/c"]}])
    http = FakeHttp(
        {
            "https://archive.org/metadata/ads1": [_meta("a.mp4")],
            "https://archive.org/metadata/ads2": [_meta("keep.mp4")],
            "https://jf/Library/VirtualFolders": [b"[]", b"", folders.encode()],
            "https://jf/Items/lib1/Refresh": [b""],
        }
    )
    args = ["--config", str(config), "--output", str(out), "--register-library"]
    args += ["--jellyfin-url", "https://jf", "--library-path", "/c"]

    with caplog.at_level(logging.DEBUG):
        assert cli.main(args, http=http) == 0

    assert (
        "POST",
        "https://jf/Items/lib1/Refresh?Recursive=true&MetadataRefreshMode=Default"
        "&ImageRefreshMode=Default",
    ) in http.calls
    assert "supersecretvalue" not in caplog.text


def test_jellyfin_failure_returns_1(
    setup: tuple[Path, Path], monkeypatch: pytest.MonkeyPatch
) -> None:
    config, out = setup
    monkeypatch.setenv("JELLYFIN_API_KEY", "k")
    http = FakeHttp(
        {
            "https://archive.org/metadata/ads1": [_meta("a.mp4")],
            "https://archive.org/metadata/ads2": [_meta("keep.mp4")],
            "https://jf/Library/VirtualFolders": [HttpError("401")],
        }
    )
    args = ["--config", str(config), "--output", str(out), "--register-library"]
    args += ["--jellyfin-url", "https://jf", "--library-path", "/c"]

    assert cli.main(args, http=http) == 1
    assert len(list(out.iterdir())) == 2


def test_non_object_metadata_aborts(setup: tuple[Path, Path]) -> None:
    config, out = setup
    http = FakeHttp({"https://archive.org/metadata/ads1": [b"[]"]})

    assert cli.main(["--config", str(config), "--output", str(out)], http=http) == 1


def test_default_http_client_is_built(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    built: list[Any] = []

    def fake_client(user_agent: str, **kwargs: Any) -> FakeHttp:
        built.append((user_agent, kwargs))
        return FakeHttp({"https://archive.org/metadata/a": [_meta("a.mp4")]})

    monkeypatch.setattr(cli, "HttpClient", fake_client)
    out = tmp_path / "out"
    out.mkdir()
    cli.main(["--config", str(tmp_path / "missing.toml"), "--output", str(out)])
    assert built == []

    config = tmp_path / "c.toml"
    config.write_text('delay_seconds = 3\n[[item]]\nidentifier = "a"\n')
    assert cli.main(["--config", str(config), "--output", str(out)]) == 0
    assert built[0][0].startswith("sm-commercials/")
    assert built[0][1]["delay_seconds"] == 3
