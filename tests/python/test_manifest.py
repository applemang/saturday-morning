import json
from pathlib import Path

import pytest

from sm_release import manifest
from sm_release.manifest import PluginVersion, ReleaseError, add_version, main, meta_json

META = {
    "guid": "3108dfcc-d642-4726-9ea3-d4b10ab9461b",
    "name": "Saturday Morning",
    "description": "Long description.",
    "overview": "Short overview.",
    "owner": "applemang",
    "category": "General",
}


def _version(v: str = "1.0.0.0") -> PluginVersion:
    return PluginVersion(
        version=v,
        changelog="First release.",
        target_abi="12.1.0.0",
        source_url=f"https://github.com/applemang/saturday-morning/releases/download/v{v}/saturday-morning_{v}.zip",
        checksum="0123456789abcdef0123456789abcdef",
        timestamp="2026-11-14T12:00:00Z",
    )


def test_add_version_to_empty_manifest_creates_plugin_entry() -> None:
    result = add_version([], META, _version())

    assert len(result) == 1
    entry = result[0]
    assert entry["guid"] == META["guid"]
    assert entry["name"] == "Saturday Morning"
    assert entry["versions"] == [
        {
            "version": "1.0.0.0",
            "changelog": "First release.",
            "targetAbi": "12.1.0.0",
            "sourceUrl": _version().source_url,
            "checksum": "0123456789abcdef0123456789abcdef",
            "timestamp": "2026-11-14T12:00:00Z",
        }
    ]


def test_new_version_goes_first_and_replaces_same_version() -> None:
    first = add_version([], META, _version("1.0.0.0"))
    second = add_version(first, META, _version("1.1.0.0"))
    redo = add_version(second, META, _version("1.1.0.0"))

    assert [v["version"] for v in redo[0]["versions"]] == ["1.1.0.0", "1.0.0.0"]


def test_other_plugins_are_left_alone() -> None:
    other = {"guid": "other", "name": "Other", "versions": []}
    result = add_version([other], META, _version())
    assert result[0] == other
    assert result[1]["guid"] == META["guid"]


def test_input_is_not_mutated() -> None:
    original: list[dict[str, object]] = []
    add_version(original, META, _version())
    assert original == []


@pytest.mark.parametrize("bad", ["1.0", "v1.0.0.0", "1.0.0.x"])
def test_rejects_non_four_part_versions(bad: str) -> None:
    with pytest.raises(ReleaseError, match="version"):
        add_version([], META, _version(bad))


def test_rejects_bad_checksum() -> None:
    v = _version()
    bad = PluginVersion(v.version, v.changelog, v.target_abi, v.source_url, "xyz", v.timestamp)
    with pytest.raises(ReleaseError, match="checksum"):
        add_version([], META, bad)


def test_meta_json_matches_jellyfin_plugin_manifest() -> None:
    meta = meta_json(META, _version())
    assert meta["guid"] == META["guid"]
    assert meta["version"] == "1.0.0.0"
    assert meta["targetAbi"] == "12.1.0.0"
    assert meta["status"] == "Active"
    assert meta["autoUpdate"] is True
    assert meta["assemblies"] == ["Jellyfin.Plugin.SaturdayMorning.dll"]


def test_cli_meta_and_manifest(tmp_path: Path) -> None:
    build = tmp_path / "build.json"
    build.write_text(json.dumps(META))
    out_meta = tmp_path / "meta.json"
    mf = tmp_path / "manifest.json"
    common = ["--build", str(build), "--version", "1.0.0.0", "--target-abi", "12.1.0.0"]
    common += ["--changelog", "First.", "--timestamp", "2026-11-14T12:00:00Z"]

    assert main(["meta", *common, "--out", str(out_meta)]) == 0
    assert json.loads(out_meta.read_text())["version"] == "1.0.0.0"

    zip_path = tmp_path / "plugin.zip"
    zip_path.write_bytes(b"zip-bytes")
    args = ["manifest", *common, "--zip", str(zip_path), "--manifest", str(mf)]
    args += ["--source-url", "https://example.invalid/plugin.zip"]
    assert main(args) == 0
    data = json.loads(mf.read_text())
    assert data[0]["versions"][0]["checksum"] == manifest.md5_of(zip_path)
    assert mf.read_text().endswith("\n")


def test_cli_rejects_bad_input(tmp_path: Path) -> None:
    build = tmp_path / "build.json"
    build.write_text(json.dumps(META))
    args = ["meta", "--build", str(build), "--version", "bad", "--target-abi", "12.1.0.0"]
    args += ["--changelog", "x", "--out", str(tmp_path / "m.json")]
    assert main(args) == 1


def test_md5_of(tmp_path: Path) -> None:
    p = tmp_path / "f"
    p.write_bytes(b"abc")
    assert manifest.md5_of(p) == "900150983cd24fb0d6963f7d28e17f72"
