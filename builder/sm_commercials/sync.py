"""Make a folder of .strm files match a desired set, touching nothing else."""

from __future__ import annotations

import os
import tempfile
from collections.abc import Mapping
from dataclasses import dataclass
from pathlib import Path


class SyncError(Exception):
    """The folder cannot be synchronised."""


@dataclass(frozen=True)
class SyncReport:
    """What a sync changed."""

    written: int
    unchanged: int
    removed: int


def _check_name(name: str) -> None:
    if (
        not name.endswith(".strm")
        or name == ".strm"
        or "/" in name
        or "\\" in name
        or name.startswith(".")
    ):
        raise SyncError(f"invalid .strm file name: {name!r}")


def _write_atomic(path: Path, content: str) -> None:
    fd, tmp = tempfile.mkstemp(dir=path.parent, prefix=".sm-", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(content)
        os.chmod(tmp, 0o644)
        os.replace(tmp, path)
    except BaseException:
        Path(tmp).unlink(missing_ok=True)
        raise


def sync_folder(target: Path, desired: Mapping[str, str]) -> SyncReport:
    """Write, update and delete top-level .strm files so the folder matches ``desired``.

    Files whose content already matches are not rewritten. Only ``*.strm`` files
    directly inside ``target`` are ever removed; other files and subfolders are left
    alone.

    Args:
        target: Existing folder dedicated to the commercials library.
        desired: ``{file name: file content}``.

    Returns:
        Counts of written, unchanged and removed files.

    Raises:
        SyncError: The folder is missing or a name is unsafe.

    """
    if not target.is_dir():
        raise SyncError(f"output folder {target} does not exist")
    for name in desired:
        _check_name(name)

    written = unchanged = removed = 0
    for name, content in sorted(desired.items()):
        path = target / name
        if path.is_file() and path.read_text(encoding="utf-8") == content:
            unchanged += 1
            continue
        _write_atomic(path, content)
        written += 1

    for path in sorted(target.glob("*.strm")):
        if path.is_file() and path.name not in desired:
            path.unlink()
            removed += 1

    return SyncReport(written=written, unchanged=unchanged, removed=removed)
