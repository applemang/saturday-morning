"""Minimal Jellyfin 12 API client for registering and scanning the commercials library."""

from __future__ import annotations

import json
import urllib.parse
from typing import Any, Protocol


class JellyfinError(Exception):
    """A Jellyfin operation failed or the server state is unexpected."""


class Requester(Protocol):
    """The part of HttpClient this module needs."""

    def request(
        self,
        method: str,
        url: str,
        *,
        headers: dict[str, str] | None = None,
        body: bytes | None = None,
    ) -> bytes:
        """Send a request and return the body."""
        ...  # pragma: no cover


# Remote .strm files must never be read in full during a scan: chapter and trickplay
# extraction would download every commercial from archive.org.
LIBRARY_OPTIONS: dict[str, Any] = {
    "Enabled": True,
    "EnableRealtimeMonitor": False,
    "EnableChapterImageExtraction": False,
    "ExtractChapterImagesDuringLibraryScan": False,
    "EnableTrickplayImageExtraction": False,
    "ExtractTrickplayImagesDuringLibraryScan": False,
    "SaveLocalMetadata": False,
    "EnableInternetProviders": False,
    "EnableEmbeddedTitles": False,
}


def auth_header(api_key: str) -> dict[str, str]:
    """Build the Jellyfin 12 Authorization header (legacy X-Emby-* is disabled).

    Args:
        api_key: Jellyfin API key.

    Returns:
        Header mapping.

    """
    return {"Authorization": f'MediaBrowser Token="{api_key}"'}


class JellyfinClient:
    """Talks to the Jellyfin admin API with a dedicated API key."""

    def __init__(self, base_url: str, api_key: str, http: Requester) -> None:
        """Create a client.

        Args:
            base_url: Server URL, e.g. ``http://127.0.0.1:8096``.
            api_key: Dedicated API key (from the environment).
            http: Request sender.

        Raises:
            JellyfinError: Missing key or non-http URL.

        """
        if not api_key:
            raise JellyfinError("a Jellyfin API key is required")
        if urllib.parse.urlsplit(base_url).scheme not in ("http", "https"):
            raise JellyfinError("Jellyfin URL must start with http:// or https://")
        self._base = base_url.rstrip("/")
        self._headers = auth_header(api_key)
        self._http = http

    def _folders(self) -> list[dict[str, Any]]:
        body = self._http.request(
            "GET", f"{self._base}/Library/VirtualFolders", headers=self._headers
        )
        data = json.loads(body)
        if not isinstance(data, list) or not all(isinstance(f, dict) for f in data):
            raise JellyfinError("unexpected response from /Library/VirtualFolders")
        return data

    @staticmethod
    def _find(folders: list[dict[str, Any]], name: str, path: str) -> str | None:
        for folder in folders:
            locations = folder.get("Locations") or []
            if folder.get("Name") == name:
                if path in locations:
                    return str(folder["ItemId"])
                raise JellyfinError(f"a library named '{name}' already exists with other paths")
            if path in locations:
                other = folder.get("Name")
                raise JellyfinError(f"path {path} is already used by library '{other}'")
        return None

    def ensure_library(self, name: str, path: str) -> tuple[str, bool]:
        """Create a Home Videos library for ``path`` unless it already exists.

        Args:
            name: Library name.
            path: Folder path as seen by the Jellyfin server (container path).

        Returns:
            ``(library item id, created)``.

        Raises:
            JellyfinError: Name/path conflicts or the new library cannot be found.

        """
        existing = self._find(self._folders(), name, path)
        if existing is not None:
            return existing, False

        query = urllib.parse.urlencode(
            {
                "name": name,
                "collectionType": "homevideos",
                "paths": path,
                "refreshLibrary": "false",
            }
        )
        body = json.dumps({"LibraryOptions": LIBRARY_OPTIONS}).encode()
        self._http.request(
            "POST", f"{self._base}/Library/VirtualFolders?{query}", headers=self._headers, body=body
        )
        created = self._find(self._folders(), name, path)
        if created is None:
            raise JellyfinError(f"library '{name}' was created but is not visible")
        return created, True

    def refresh_library(self, library_id: str) -> None:
        """Scan only the given library.

        Args:
            library_id: Library item id.

        """
        query = "Recursive=true&MetadataRefreshMode=Default&ImageRefreshMode=Default"
        self._http.request(
            "POST",
            f"{self._base}/Items/{urllib.parse.quote(library_id)}/Refresh?{query}",
            headers=self._headers,
        )
