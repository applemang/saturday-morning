"""Download URLs and .strm file names for archive.org videos."""

from __future__ import annotations

import re
import urllib.parse
from collections.abc import Iterable
from pathlib import PurePosixPath

from sm_commercials.archive import VideoFile

DOWNLOAD_BASE = "https://archive.org/download/"
MAX_NAME_BYTES = 200
_UNSAFE = re.compile(r"[^A-Za-z0-9 ._()\-]+")
_SPACES = re.compile(r"\s+")
# Jellyfin treats files whose names contain "sample" as sample clips and hides them.
_RESERVED = re.compile(r"sample", re.IGNORECASE)


def download_url(identifier: str, filename: str) -> str:
    """Build the direct download URL for a file in an item.

    Args:
        identifier: Item identifier.
        filename: File name inside the item; may contain ``/`` for subfolders.

    Returns:
        ``https://archive.org/download/{identifier}/{url-encoded filename}``.

    """
    return DOWNLOAD_BASE + identifier + "/" + urllib.parse.quote(filename, safe="/")


def strm_content(url: str) -> str:
    """Return the .strm file body: exactly one line holding the URL.

    Args:
        url: Stream URL.

    Returns:
        The URL followed by a single newline.

    """
    return url + "\n"


def _clean(text: str) -> str:
    text = _SPACES.sub(" ", _UNSAFE.sub(" ", text)).strip(" .-")
    while _RESERVED.search(text):
        text = _RESERVED.sub("smpl", text)
    return text


def strm_name(identifier: str, filename: str) -> str:
    """Derive a safe, readable .strm file name.

    Args:
        identifier: Item identifier.
        filename: File name inside the item.

    Returns:
        ``"{identifier} - {stem}.strm"``, sanitised, never containing ``sample``.

    """
    path = PurePosixPath(filename)
    stem = " ".join([*path.parent.parts, path.stem]) if path.parent.parts else path.stem
    stem = _clean(stem) or "video"
    name = f"{_clean(identifier)} - {stem}"
    encoded = name.encode()[: MAX_NAME_BYTES - len(".strm")]
    return _clean(encoded.decode(errors="ignore")) + ".strm"


def assign_names(files: Iterable[VideoFile]) -> dict[str, str]:
    """Map unique .strm names to download URLs, numbering any collisions.

    Args:
        files: Chosen video files, in a stable order.

    Returns:
        ``{strm name: download URL}``.

    """
    result: dict[str, str] = {}
    for video in files:
        base = strm_name(video.identifier, video.name)
        name, n = base, 1
        while name in result:
            n += 1
            name = f"{base[: -len('.strm')]} ({n}).strm"
        result[name] = download_url(video.identifier, video.name)
    return result
