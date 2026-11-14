import io
import json
import urllib.error
import urllib.request
from collections.abc import Callable
from email.message import Message
from typing import Any

import pytest

from sm_commercials.http import HttpClient, HttpError


class FakeResponse(io.BytesIO):
    def __enter__(self) -> "FakeResponse":
        return self

    def __exit__(self, *args: object) -> None:
        self.close()


def _http_error(code: int, headers: dict[str, str] | None = None) -> urllib.error.HTTPError:
    msg = Message()
    for k, v in (headers or {}).items():
        msg[k] = v
    return urllib.error.HTTPError("https://x", code, "err", msg, io.BytesIO(b""))


class Recorder:
    def __init__(self, outcomes: list[bytes | Exception]) -> None:
        self.outcomes = outcomes
        self.requests: list[urllib.request.Request] = []
        self.timeouts: list[float] = []

    def __call__(self, request: urllib.request.Request, timeout: float) -> FakeResponse:
        self.requests.append(request)
        self.timeouts.append(timeout)
        outcome = self.outcomes.pop(0)
        if isinstance(outcome, Exception):
            raise outcome
        return FakeResponse(outcome)


def _client(rec: Recorder, sleeps: list[float], **kwargs: Any) -> HttpClient:
    sleep: Callable[[float], None] = sleeps.append
    return HttpClient("sm-commercials/1.0 (test)", opener=rec, sleep=sleep, **kwargs)


def test_get_json_sends_user_agent_and_parses() -> None:
    rec = Recorder([json.dumps({"ok": 1}).encode()])
    sleeps: list[float] = []

    result = _client(rec, sleeps, timeout=7.0).get_json("https://archive.org/metadata/a")

    assert result == {"ok": 1}
    assert rec.requests[0].get_header("User-agent") == "sm-commercials/1.0 (test)"
    assert rec.requests[0].get_method() == "GET"
    assert rec.timeouts == [7.0]
    assert sleeps == []


def test_polite_delay_between_requests() -> None:
    rec = Recorder([b"{}", b"{}"])
    sleeps: list[float] = []
    client = _client(rec, sleeps, delay_seconds=1.5)

    client.get_json("https://a/1")
    client.get_json("https://a/2")

    assert sleeps == [1.5]


def test_retries_server_errors_with_exponential_backoff() -> None:
    rec = Recorder([_http_error(503), urllib.error.URLError("down"), TimeoutError(), b"{}"])
    sleeps: list[float] = []

    assert _client(rec, sleeps, retries=3, backoff_seconds=2.0).get_json("https://a") == {}
    assert sleeps == [2.0, 4.0, 8.0]


def test_honours_retry_after_on_429() -> None:
    rec = Recorder([_http_error(429, {"Retry-After": "30"}), b"{}"])
    sleeps: list[float] = []

    _client(rec, sleeps, backoff_seconds=2.0).get_json("https://a")

    assert sleeps == [30.0]


def test_ignores_unparseable_retry_after() -> None:
    rec = Recorder([_http_error(429, {"Retry-After": "soon"}), b"{}"])
    sleeps: list[float] = []

    _client(rec, sleeps, backoff_seconds=2.0).get_json("https://a")

    assert sleeps == [2.0]


def test_gives_up_after_retries() -> None:
    rec = Recorder([_http_error(500), _http_error(500)])
    sleeps: list[float] = []

    with pytest.raises(HttpError, match="after 2 attempts"):
        _client(rec, sleeps, retries=1).get_json("https://a")


def test_client_errors_are_not_retried() -> None:
    rec = Recorder([_http_error(404)])
    sleeps: list[float] = []

    with pytest.raises(HttpError, match="404"):
        _client(rec, sleeps).get_json("https://a")
    assert sleeps == []


def test_request_with_body_and_headers() -> None:
    rec = Recorder([b""])
    sleeps: list[float] = []

    body = _client(rec, sleeps).request(
        "POST", "https://jf/x", headers={"Authorization": "secret"}, body=b"{}"
    )

    assert body == b""
    req = rec.requests[0]
    assert req.get_method() == "POST"
    assert req.data == b"{}"
    assert req.get_header("Authorization") == "secret"
    assert req.get_header("Content-type") == "application/json"


def test_invalid_json_is_an_error() -> None:
    rec = Recorder([b"<html>"])
    with pytest.raises(HttpError, match="JSON"):
        _client(rec, []).get_json("https://a")


@pytest.mark.parametrize("url", ["file:///etc/passwd", "ftp://a/b", "a/b"])
def test_rejects_non_http_urls(url: str) -> None:
    with pytest.raises(HttpError, match="scheme"):
        _client(Recorder([]), []).get_json(url)


def test_error_messages_do_not_include_headers() -> None:
    rec = Recorder([_http_error(401)])
    with pytest.raises(HttpError) as info:
        _client(rec, []).request("GET", "https://jf/x", headers={"Authorization": "TOPSECRET"})
    assert "TOPSECRET" not in str(info.value)


def test_default_opener_is_urlopen() -> None:
    client = HttpClient("ua")
    assert client.opener is urllib.request.urlopen
