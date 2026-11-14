"""Choose playable video files from Internet Archive item metadata."""

from __future__ import annotations

import fnmatch
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from typing import Any

METADATA_BASE = "https://archive.org/metadata/"

# Lower rank wins. MP4 / h.264 derivatives first because every Jellyfin client can
# direct-play them; other video formats are a fallback. Anything not listed here
# (thumbnails, torrents, metadata, text, GIF previews) is never selected.
FORMAT_RANK: dict[str, int] = {
    "h.264": 0,
    "h.264 IA": 1,
    "MPEG4": 2,
    "512Kb MPEG4": 3,
    "HiRes MPEG4": 4,
    "WebM": 10,
    "Matroska": 11,
    "QuickTime": 12,
    "MPEG2": 13,
    "MPEG1": 14,
    "Ogg Video": 15,
    "Windows Media": 16,
    "Cinepack": 17,
    "AVI": 18,
    "DivX": 19,
}


class ArchiveError(Exception):
    """The metadata does not describe a usable item."""


@dataclass(frozen=True, order=True)
class VideoFile:
    """One video file inside an Internet Archive item."""

    identifier: str
    name: str


def metadata_url(identifier: str) -> str:
    """Return the Metadata API URL for an item.

    Args:
        identifier: Internet Archive item identifier (already validated).

    Returns:
        The metadata URL.

    """
    return METADATA_BASE + identifier


def _matches(names: Sequence[str], patterns: Sequence[str]) -> bool:
    return any(fnmatch.fnmatch(n.lower(), p.lower()) for n in names for p in patterns)


def select_video_files(
    identifier: str,
    metadata: Mapping[str, Any],
    include: Sequence[str],
    exclude: Sequence[str],
) -> list[VideoFile]:
    """Pick one best video file per source video in an item.

    Files are grouped by their ``original`` (derivatives point at the file they were
    made from), and the best-ranked format in each group is kept. Include patterns,
    when given, must match the chosen file name or its original name; exclude
    patterns remove a group when they match either.

    Args:
        identifier: Item identifier.
        metadata: Parsed Metadata API response.
        include: Case-insensitive glob patterns; empty means everything.
        exclude: Case-insensitive glob patterns.

    Returns:
        Chosen files, sorted by name.

    Raises:
        ArchiveError: The item does not exist or the response is malformed.

    """
    if "files" not in metadata:
        raise ArchiveError(f"archive.org item '{identifier}' not found (no files listed)")
    files = metadata["files"]
    if not isinstance(files, list):
        raise ArchiveError(f"archive.org item '{identifier}' has malformed metadata")

    best: dict[str, tuple[int, str]] = {}
    for entry in files:
        if not isinstance(entry, dict):
            continue
        name, fmt = entry.get("name"), entry.get("format")
        if not isinstance(name, str) or fmt not in FORMAT_RANK:
            continue
        original = entry.get("original")
        group = original if isinstance(original, str) else name
        candidate = (FORMAT_RANK[fmt], name)
        if group not in best or candidate < best[group]:
            best[group] = candidate

    chosen: list[VideoFile] = []
    for group, (_, name) in best.items():
        names = (name, group)
        if include and not _matches(names, include):
            continue
        if exclude and _matches(names, exclude):
            continue
        chosen.append(VideoFile(identifier, name))
    return sorted(chosen)
