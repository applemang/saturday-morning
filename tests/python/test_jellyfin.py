import json
from typing import Any

import pytest

from sm_commercials.jellyfin import JellyfinClient, JellyfinError, auth_header


class FakeHttp:
    def __init__(self, responses: list[bytes]) -> None:
        self.responses = responses
        self.calls: list[tuple[str, str, dict[str, str], bytes | None]] = []

    def request(
        self,
        method: str,
        url: str,
        *,
        headers: dict[str, str] | None = None,
        body: bytes | None = None,
    ) -> bytes:
        self.calls.append((method, url, dict(headers or {}), body))
        return self.responses.pop(0)


def _folders(*folders: dict[str, Any]) -> bytes:
    return json.dumps(list(folders)).encode()


def test_auth_header_uses_modern_scheme() -> None:
    assert auth_header("k") == {"Authorization": 'MediaBrowser Token="k"'}


def test_rejects_empty_key_and_bad_url() -> None:
    with pytest.raises(JellyfinError, match="API key"):
        JellyfinClient("https://jf", "", FakeHttp([]))
    with pytest.raises(JellyfinError, match="URL"):
        JellyfinClient("ftp://jf", "k", FakeHttp([]))


def test_existing_library_with_path_is_reused() -> None:
    http = FakeHttp(
        [_folders({"Name": "Commercials", "ItemId": "abc", "Locations": ["/commercials"]})]
    )
    client = JellyfinClient("https://jf/", "k", http)

    assert client.ensure_library("Commercials", "/commercials") == ("abc", False)
    method, url, headers, _ = http.calls[0]
    assert (method, url) == ("GET", "https://jf/Library/VirtualFolders")
    assert headers == {"Authorization": 'MediaBrowser Token="k"'}
    assert "X-Emby-Token" not in headers


def test_existing_library_name_with_other_path_is_an_error() -> None:
    http = FakeHttp([_folders({"Name": "Commercials", "ItemId": "abc", "Locations": ["/other"]})])

    with pytest.raises(JellyfinError, match="already exists"):
        JellyfinClient("https://jf", "k", http).ensure_library("Commercials", "/commercials")


def test_path_used_by_other_library_is_an_error() -> None:
    http = FakeHttp([_folders({"Name": "Ads", "ItemId": "abc", "Locations": ["/commercials"]})])

    with pytest.raises(JellyfinError, match="already used"):
        JellyfinClient("https://jf", "k", http).ensure_library("Commercials", "/commercials")


def test_creates_library_with_no_heavy_extraction() -> None:
    http = FakeHttp(
        [
            _folders({"Name": "TV", "ItemId": "t", "Locations": ["/media/tv"]}),
            b"",
            _folders(
                {"Name": "TV", "ItemId": "t", "Locations": ["/media/tv"]},
                {"Name": "Commercials", "ItemId": "new", "Locations": ["/commercials"]},
            ),
        ]
    )

    result = JellyfinClient("https://jf", "k", http).ensure_library("Commercials", "/commercials")

    assert result == ("new", True)
    method, url, _, body = http.calls[1]
    assert method == "POST"
    assert url == (
        "https://jf/Library/VirtualFolders?name=Commercials&collectionType=homevideos"
        "&paths=%2Fcommercials&refreshLibrary=false"
    )
    assert body is not None
    options = json.loads(body)["LibraryOptions"]
    assert options["EnableChapterImageExtraction"] is False
    assert options["EnableTrickplayImageExtraction"] is False
    assert options["ExtractTrickplayImagesDuringLibraryScan"] is False
    assert options["EnableRealtimeMonitor"] is False
    assert options["SaveLocalMetadata"] is False


def test_created_library_not_visible_afterwards_is_an_error() -> None:
    http = FakeHttp([_folders(), b"", _folders()])

    with pytest.raises(JellyfinError, match="not visible"):
        JellyfinClient("https://jf", "k", http).ensure_library("Commercials", "/commercials")


def test_refresh_library_targets_only_that_library() -> None:
    http = FakeHttp([b""])

    JellyfinClient("https://jf", "k", http).refresh_library("abc")

    method, url, _, _ = http.calls[0]
    assert method == "POST"
    assert url == (
        "https://jf/Items/abc/Refresh?Recursive=true"
        "&MetadataRefreshMode=Default&ImageRefreshMode=Default"
    )


def test_malformed_folder_listing_is_an_error() -> None:
    with pytest.raises(JellyfinError, match="unexpected"):
        JellyfinClient("https://jf", "k", FakeHttp([b'{"x": 1}'])).ensure_library("C", "/c")
