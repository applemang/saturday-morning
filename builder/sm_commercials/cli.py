"""Command line entry point: archive.org items -> .strm folder (-> Jellyfin library)."""

from __future__ import annotations

import argparse
import logging
import os
import sys
from collections.abc import Sequence
from pathlib import Path
from typing import Protocol

from sm_commercials import __version__
from sm_commercials.archive import ArchiveError, VideoFile, metadata_url, select_video_files
from sm_commercials.config import ConfigError, load_config
from sm_commercials.http import HttpClient, HttpError
from sm_commercials.jellyfin import JellyfinClient, JellyfinError
from sm_commercials.naming import assign_names, strm_content
from sm_commercials.sync import SyncError, sync_folder

logger = logging.getLogger("sm_commercials")

USER_AGENT = (
    f"sm-commercials/{__version__} (Saturday Morning Jellyfin plugin commercials builder; "
    "sequential, rate-limited)"
)
API_KEY_ENV = "JELLYFIN_API_KEY"


class Http(Protocol):
    """HTTP operations the CLI uses."""

    def request(
        self,
        method: str,
        url: str,
        *,
        headers: dict[str, str] | None = None,
        body: bytes | None = None,
    ) -> bytes:
        """Send a request."""
        ...  # pragma: no cover

    def get_json(self, url: str, headers: dict[str, str] | None = None) -> object:
        """GET JSON."""
        ...  # pragma: no cover


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="sm-commercials",
        description="Write one .strm file per archive.org commercial listed in the config.",
    )
    parser.add_argument("--config", type=Path, required=True, help="commercials.toml")
    parser.add_argument("--output", type=Path, required=True, help="dedicated .strm folder")
    parser.add_argument(
        "--register-library",
        action="store_true",
        help=f"create the Jellyfin library if missing and scan it (key from ${API_KEY_ENV})",
    )
    parser.add_argument("--jellyfin-url", help="e.g. http://127.0.0.1:8096")
    parser.add_argument("--library-name", default="Commercials")
    parser.add_argument("--library-path", help="the .strm folder as the Jellyfin server sees it")
    parser.add_argument("-v", "--verbose", action="store_true")
    return parser


def main(argv: Sequence[str] | None = None, *, http: Http | None = None) -> int:
    """Run the builder.

    Args:
        argv: Arguments (defaults to ``sys.argv[1:]``).
        http: HTTP client override for tests.

    Returns:
        0 on success, 1 on a runtime failure, 2 on a usage or config error.

    """
    args = _parser().parse_args(argv)
    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(levelname)s %(message)s",
        stream=sys.stderr,
    )

    api_key = ""
    if args.register_library:
        api_key = os.environ.get(API_KEY_ENV, "")
        if not api_key:
            logger.error("--register-library needs the %s environment variable", API_KEY_ENV)
            return 2
        if not args.jellyfin_url or not args.library_path:
            logger.error("--register-library needs --jellyfin-url and --library-path")
            return 2

    try:
        config = load_config(args.config)
    except ConfigError as exc:
        logger.error("config: %s", exc)
        return 2

    client: Http = http or HttpClient(USER_AGENT, delay_seconds=config.delay_seconds)

    videos: list[VideoFile] = []
    try:
        for item in config.items:
            metadata = client.get_json(metadata_url(item.identifier))
            if not isinstance(metadata, dict):
                raise ArchiveError(f"item '{item.identifier}': unexpected metadata response")
            chosen = select_video_files(item.identifier, metadata, item.include, item.exclude)
            if not chosen:
                raise ArchiveError(f"item '{item.identifier}' has no matching video files")
            logger.info("%s: %d video(s)", item.identifier, len(chosen))
            videos.extend(chosen)
    except (HttpError, ArchiveError) as exc:
        logger.error("%s; folder left untouched", exc)
        return 1

    desired = {name: strm_content(url) for name, url in assign_names(videos).items()}
    try:
        report = sync_folder(args.output, desired)
    except SyncError as exc:
        logger.error("%s", exc)
        return 1
    logger.info(
        "sync: %d written, %d unchanged, %d removed",
        report.written,
        report.unchanged,
        report.removed,
    )

    if args.register_library:
        try:
            jellyfin = JellyfinClient(args.jellyfin_url, api_key, client)
            library_id, created = jellyfin.ensure_library(args.library_name, args.library_path)
            jellyfin.refresh_library(library_id)
        except (HttpError, JellyfinError) as exc:
            logger.error("jellyfin: %s", exc)
            return 1
        logger.info(
            "jellyfin: library '%s' %s; scan queued",
            args.library_name,
            "created" if created else "exists",
        )
    return 0


if __name__ == "__main__":  # pragma: no cover
    sys.exit(main())
