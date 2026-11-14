"""Generate the plugin's meta.json and update the Jellyfin repository manifest.json."""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import logging
import re
import sys
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

logger = logging.getLogger("sm_release")

ASSEMBLY = "Jellyfin.Plugin.SaturdayMorning.dll"
_VERSION = re.compile(r"^\d+\.\d+\.\d+\.\d+$")
_MD5 = re.compile(r"^[0-9a-f]{32}$")


class ReleaseError(Exception):
    """Release metadata is invalid."""


@dataclass(frozen=True)
class PluginVersion:
    """One published plugin version."""

    version: str
    changelog: str
    target_abi: str
    source_url: str
    checksum: str
    timestamp: str


def md5_of(path: Path) -> str:
    """Return the MD5 hex digest Jellyfin uses to verify plugin downloads.

    Args:
        path: File to hash.

    Returns:
        Lowercase hex digest.

    """
    return hashlib.md5(path.read_bytes(), usedforsecurity=False).hexdigest()


def _check(version: PluginVersion) -> None:
    for value, label in ((version.version, "version"), (version.target_abi, "targetAbi")):
        if not _VERSION.match(value):
            raise ReleaseError(f"{label} must have four numeric parts, got {value!r}")
    if not _MD5.match(version.checksum):
        raise ReleaseError("checksum must be a lowercase MD5 hex digest")


def add_version(
    manifest: Sequence[Mapping[str, Any]], meta: Mapping[str, str], version: PluginVersion
) -> list[dict[str, Any]]:
    """Return a copy of the manifest with ``version`` listed first for this plugin.

    Args:
        manifest: Existing repository manifest (list of plugins).
        meta: Plugin identity from build.json.
        version: The version to add or replace.

    Returns:
        The updated manifest.

    Raises:
        ReleaseError: Version, targetAbi or checksum is malformed.

    """
    _check(version)
    result = [dict(copy.deepcopy(p)) for p in manifest]
    entry = next((p for p in result if p.get("guid") == meta["guid"]), None)
    if entry is None:
        entry = {"versions": []}
        result.append(entry)
    entry.update({k: meta[k] for k in ("guid", "name", "description", "overview", "owner")})
    entry["category"] = meta["category"]
    others = [v for v in entry["versions"] if v.get("version") != version.version]
    entry["versions"] = [
        {
            "version": version.version,
            "changelog": version.changelog,
            "targetAbi": version.target_abi,
            "sourceUrl": version.source_url,
            "checksum": version.checksum,
            "timestamp": version.timestamp,
        },
        *others,
    ]
    return result


def meta_json(meta: Mapping[str, str], version: PluginVersion) -> dict[str, Any]:
    """Build the meta.json shipped inside the plugin zip.

    Args:
        meta: Plugin identity from build.json.
        version: The version being packaged.

    Returns:
        A dict matching Jellyfin's PluginManifest.

    """
    return {
        "category": meta["category"],
        "changelog": version.changelog,
        "description": meta["description"],
        "guid": meta["guid"],
        "name": meta["name"],
        "overview": meta["overview"],
        "owner": meta["owner"],
        "targetAbi": version.target_abi,
        "timestamp": version.timestamp,
        "version": version.version,
        "status": "Active",
        "autoUpdate": True,
        "assemblies": [ASSEMBLY],
    }


def _write_json(path: Path, data: object) -> None:
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def main(argv: Sequence[str] | None = None) -> int:
    """Command line entry point (``meta`` or ``manifest``).

    Args:
        argv: Arguments.

    Returns:
        Process exit code.

    """
    parser = argparse.ArgumentParser(prog="sm-release")
    parser.add_argument("command", choices=["meta", "manifest"])
    parser.add_argument("--build", type=Path, required=True, help="build.json")
    parser.add_argument("--version", required=True)
    parser.add_argument("--target-abi", required=True)
    parser.add_argument("--changelog", required=True)
    parser.add_argument("--timestamp", default=datetime.now(UTC).strftime("%Y-%m-%dT%H:%M:%SZ"))
    parser.add_argument("--out", type=Path, help="meta: output meta.json")
    parser.add_argument("--zip", type=Path, help="manifest: packaged plugin zip")
    parser.add_argument("--source-url", help="manifest: public download URL of the zip")
    parser.add_argument("--manifest", type=Path, help="manifest: manifest.json to update")
    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(message)s", stream=sys.stderr)

    meta = json.loads(args.build.read_text(encoding="utf-8"))
    checksum = md5_of(args.zip) if args.command == "manifest" else "0" * 32
    version = PluginVersion(
        version=args.version,
        changelog=args.changelog,
        target_abi=args.target_abi,
        source_url=args.source_url or "",
        checksum=checksum,
        timestamp=args.timestamp,
    )
    try:
        if args.command == "meta":
            _check(version)
            _write_json(args.out, meta_json(meta, version))
        else:
            current = (
                json.loads(args.manifest.read_text(encoding="utf-8"))
                if args.manifest.exists()
                else []
            )
            _write_json(args.manifest, add_version(current, meta, version))
    except ReleaseError as exc:
        logger.error("%s", exc)
        return 1
    return 0


if __name__ == "__main__":  # pragma: no cover
    sys.exit(main())
