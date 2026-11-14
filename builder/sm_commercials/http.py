"""Small, polite HTTP client: sequential, delayed, retried with backoff."""

from __future__ import annotations

import json
import logging
import time
import urllib.error
import urllib.parse
import urllib.request
from collections.abc import Callable
from typing import Any

logger = logging.getLogger(__name__)

Opener = Callable[..., Any]


class HttpError(Exception):
    """A request failed permanently."""


def _retry_after(exc: urllib.error.HTTPError) -> float | None:
    value = exc.headers.get("Retry-After") if exc.headers else None
    try:
        return float(value) if value is not None else None
    except ValueError:
        return None


class HttpClient:
    """Sequential HTTP client with a fixed delay between requests and retry with backoff."""

    def __init__(
        self,
        user_agent: str,
        *,
        retries: int = 4,
        backoff_seconds: float = 2.0,
        delay_seconds: float = 1.0,
        timeout: float = 30.0,
        sleep: Callable[[float], None] = time.sleep,
        opener: Opener | None = None,
    ) -> None:
        """Create a client.

        Args:
            user_agent: Descriptive User-Agent sent with every request.
            retries: Extra attempts after the first for transient failures.
            backoff_seconds: First retry wait; doubles on each further retry.
            delay_seconds: Pause before every request after the first.
            timeout: Socket timeout per attempt.
            sleep: Sleep function (injectable for tests).
            opener: ``urllib.request.urlopen`` compatible callable.

        """
        self.user_agent = user_agent
        self.retries = retries
        self.backoff_seconds = backoff_seconds
        self.delay_seconds = delay_seconds
        self.timeout = timeout
        self.sleep = sleep
        self.opener: Opener = opener or urllib.request.urlopen
        self._used = False

    def request(
        self,
        method: str,
        url: str,
        *,
        headers: dict[str, str] | None = None,
        body: bytes | None = None,
    ) -> bytes:
        """Send a request and return the response body.

        Args:
            method: HTTP method.
            url: Absolute http(s) URL.
            headers: Extra headers. Never logged or included in errors.
            body: JSON request body, if any.

        Returns:
            Response body bytes.

        Raises:
            HttpError: Non-retryable status, retries exhausted, or bad URL scheme.

        """
        if urllib.parse.urlsplit(url).scheme not in ("http", "https"):
            raise HttpError(f"refusing URL with unsupported scheme: {url}")
        if self._used and self.delay_seconds > 0:
            self.sleep(self.delay_seconds)
        self._used = True

        all_headers = {"User-Agent": self.user_agent, **(headers or {})}
        if body is not None:
            all_headers["Content-Type"] = "application/json"

        attempts = max(self.retries, 0) + 1
        attempt = 0
        while True:
            req = urllib.request.Request(url, data=body, headers=all_headers, method=method)  # noqa: S310 - scheme checked above
            try:
                with self.opener(req, timeout=self.timeout) as response:
                    data: bytes = response.read()
                    return data
            except urllib.error.HTTPError as exc:
                if exc.code != 429 and exc.code < 500:
                    raise HttpError(f"{method} {url} failed with HTTP {exc.code}") from None
                wait = _retry_after(exc) if exc.code == 429 else None
                reason = f"HTTP {exc.code}"
            except (urllib.error.URLError, TimeoutError) as exc:
                wait = None
                reason = type(exc).__name__
            attempt += 1
            if attempt >= attempts:
                raise HttpError(f"{method} {url} failed after {attempts} attempts ({reason})")
            wait = wait if wait is not None else self.backoff_seconds * (2 ** (attempt - 1))
            logger.warning("%s %s: %s, retrying in %.0fs", method, url, reason, wait)
            self.sleep(wait)

    def get_json(self, url: str, headers: dict[str, str] | None = None) -> object:
        """GET a URL and parse the JSON response.

        Args:
            url: Absolute http(s) URL.
            headers: Extra headers.

        Returns:
            Parsed JSON.

        Raises:
            HttpError: The request failed or the body is not JSON.

        """
        body = self.request("GET", url, headers=headers)
        try:
            parsed: object = json.loads(body)
            return parsed
        except ValueError as exc:
            raise HttpError(f"GET {url} did not return JSON") from exc
