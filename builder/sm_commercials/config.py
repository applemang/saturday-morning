"""Load and validate the commercials list (commercials.toml)."""

from __future__ import annotations

import re
import tomllib
from dataclasses import dataclass
from pathlib import Path
from typing import Any

_IDENTIFIER = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*$")
PLACEHOLDER_PREFIX = "REPLACE_ME"
_TOP_KEYS = {"item", "delay_seconds"}
_ITEM_KEYS = {"identifier", "include", "exclude"}


class ConfigError(Exception):
    """The configuration file is missing or invalid."""


@dataclass(frozen=True)
class ItemConfig:
    """One Internet Archive item and its optional file name filters."""

    identifier: str
    include: tuple[str, ...]
    exclude: tuple[str, ...]


@dataclass(frozen=True)
class BuilderConfig:
    """The whole commercials list."""

    items: tuple[ItemConfig, ...]
    delay_seconds: float = 1.0


def _patterns(raw: dict[str, Any], key: str, identifier: str) -> tuple[str, ...]:
    value = raw.get(key, [])
    if not isinstance(value, list) or not all(isinstance(p, str) for p in value):
        raise ConfigError(f"item '{identifier}': {key} must be a list of strings")
    return tuple(value)


def _item(raw: object) -> ItemConfig:
    if not isinstance(raw, dict):
        raise ConfigError("each [[item]] must be a table")
    identifier = raw.get("identifier")
    if not isinstance(identifier, str) or not _IDENTIFIER.match(identifier):
        raise ConfigError(f"invalid identifier {identifier!r}")
    if identifier.startswith(PLACEHOLDER_PREFIX):
        raise ConfigError(
            f"identifier '{identifier}' is a placeholder; edit commercials.toml with real items"
        )
    unknown = set(raw) - _ITEM_KEYS
    if unknown:
        raise ConfigError(f"item '{identifier}': unknown keys {sorted(unknown)}")
    return ItemConfig(
        identifier, _patterns(raw, "include", identifier), _patterns(raw, "exclude", identifier)
    )


def load_config(path: Path) -> BuilderConfig:
    """Read and validate the configuration.

    Args:
        path: Path to the TOML file.

    Returns:
        The parsed configuration.

    Raises:
        ConfigError: The file is unreadable or invalid.

    """
    try:
        raw = tomllib.loads(path.read_text(encoding="utf-8"))
    except OSError as exc:
        raise ConfigError(f"cannot read {path}: {exc.strerror}") from exc
    except tomllib.TOMLDecodeError as exc:
        raise ConfigError(f"{path} is not valid TOML: {exc}") from exc

    unknown = set(raw) - _TOP_KEYS
    if unknown:
        raise ConfigError(f"unknown top-level keys {sorted(unknown)}")

    raw_items = raw.get("item")
    if not isinstance(raw_items, list) or not raw_items:
        raise ConfigError("the config must list at least one [[item]]")
    items = tuple(_item(i) for i in raw_items)

    seen: set[str] = set()
    for item in items:
        if item.identifier in seen:
            raise ConfigError(f"duplicate identifier '{item.identifier}'")
        seen.add(item.identifier)

    delay = raw.get("delay_seconds", 1.0)
    if isinstance(delay, bool) or not isinstance(delay, int | float) or delay < 0:
        raise ConfigError("delay_seconds must be a non-negative number")
    return BuilderConfig(items=items, delay_seconds=float(delay))
