from pathlib import Path

import pytest

from sm_commercials.config import BuilderConfig, ConfigError, ItemConfig, load_config


def _write(tmp_path: Path, text: str) -> Path:
    path = tmp_path / "commercials.toml"
    path.write_text(text)
    return path


def test_loads_items_with_optional_filters(tmp_path: Path) -> None:
    path = _write(
        tmp_path,
        """
delay_seconds = 2.5

[[item]]
identifier = "classic_tv_ads"

[[item]]
identifier = "toy-ads.1980s"
include = ["*1983*"]
exclude = ["*promo*"]
""",
    )

    assert load_config(path) == BuilderConfig(
        items=(
            ItemConfig("classic_tv_ads", (), ()),
            ItemConfig("toy-ads.1980s", ("*1983*",), ("*promo*",)),
        ),
        delay_seconds=2.5,
    )


def test_default_delay(tmp_path: Path) -> None:
    config = load_config(_write(tmp_path, '[[item]]\nidentifier = "a"\n'))
    assert config.delay_seconds == 1.0


def test_shipped_example_config_is_valid_toml_but_flagged_as_placeholder() -> None:
    shipped = Path(__file__).resolve().parents[2] / "commercials.toml"
    with pytest.raises(ConfigError, match="placeholder"):
        load_config(shipped)


@pytest.mark.parametrize(
    ("text", "message"),
    [
        ("", "at least one"),
        ("item = 3", "at least one"),
        ("item = [1]", "table"),
        ("[[item]]\nidentifier = 5\n", "identifier"),
        ('[[item]]\nidentifier = "../etc"\n', "identifier"),
        ('[[item]]\nidentifier = "a b"\n', "identifier"),
        ('[[item]]\nidentifier = "a"\n[[item]]\nidentifier = "a"\n', "duplicate"),
        ('[[item]]\nidentifier = "a"\ninclude = "x"\n', "include"),
        ('[[item]]\nidentifier = "a"\nexclude = [1]\n', "exclude"),
        ('[[item]]\nidentifier = "REPLACE_ME_1"\n', "placeholder"),
        ('delay_seconds = -1\n[[item]]\nidentifier = "a"\n', "delay_seconds"),
        ('delay_seconds = "x"\n[[item]]\nidentifier = "a"\n', "delay_seconds"),
        ('[[item]]\nidentifier = "a"\nextra = 1\n', "unknown"),
        ('surprise = 1\n[[item]]\nidentifier = "a"\n', "unknown"),
        ("not toml [", "TOML"),
    ],
)
def test_rejects_bad_config(tmp_path: Path, text: str, message: str) -> None:
    with pytest.raises(ConfigError, match=message):
        load_config(_write(tmp_path, text))


def test_missing_file(tmp_path: Path) -> None:
    with pytest.raises(ConfigError, match="cannot read"):
        load_config(tmp_path / "missing.toml")
