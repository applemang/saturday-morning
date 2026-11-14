import re

import pytest

from sm_commercials.archive import VideoFile
from sm_commercials.naming import assign_names, download_url, strm_content, strm_name


def test_download_url_simple() -> None:
    assert (
        download_url("classic_tv_ads", "Cereal Ad 1983.mp4")
        == "https://archive.org/download/classic_tv_ads/Cereal%20Ad%201983.mp4"
    )


def test_download_url_keeps_subdirectory_slashes_and_encodes_the_rest() -> None:
    url = download_url("ads", "1980s/Toy #1 & Friends?.mp4")
    assert url == "https://archive.org/download/ads/1980s/Toy%20%231%20%26%20Friends%3F.mp4"


def test_download_url_encodes_non_ascii() -> None:
    assert download_url("ads", "caf\u00e9.mp4").endswith("/caf%C3%A9.mp4")


def test_strm_content_is_exactly_one_line() -> None:
    content = strm_content("https://archive.org/download/a/b.mp4")
    assert content == "https://archive.org/download/a/b.mp4\n"
    assert content.count("\n") == 1


def test_strm_name_combines_identifier_and_stem() -> None:
    name = strm_name("classic_tv_ads", "Cereal Ad 1983.mp4")
    assert name == "classic_tv_ads - Cereal Ad 1983.strm"


def test_strm_name_drops_subdirectories() -> None:
    assert strm_name("ads", "1980s/toys/robot.mp4") == "ads - 1980s toys robot.strm"


@pytest.mark.parametrize(
    "filename",
    [
        "sample.mp4",
        "Sample Reel.mp4",
        "SAMPLE.mp4",
        "my_sampler.mp4",
        "samsampleple.mp4",
        "s a m p l e/sample.mp4",
    ],
)
def test_strm_name_never_contains_sample(filename: str) -> None:
    name = strm_name("ads", filename)
    assert "sample" not in name.lower()
    assert name.endswith(".strm")


def test_strm_name_identifier_containing_sample_is_also_cleaned() -> None:
    assert "sample" not in strm_name("sample_ads_collection", "a.mp4").lower()


def test_strm_name_removes_unsafe_characters() -> None:
    name = strm_name("ads", 'bad:<>|*?"\\name\x00.mp4')
    assert re.fullmatch(r"[A-Za-z0-9 ._()\-]+\.strm", name)


def test_strm_name_never_starts_with_dot_or_is_empty() -> None:
    assert not strm_name("ads", "....mp4").startswith(".")
    assert strm_name("ads", "!!!.mp4") == "ads - video.strm"


def test_strm_name_is_length_capped() -> None:
    name = strm_name("ads", "x" * 400 + ".mp4")
    assert len(name.encode()) <= 200


def test_assign_names_maps_names_to_urls() -> None:
    files = [VideoFile("ads", "a.mp4"), VideoFile("ads", "b.mp4")]
    assert assign_names(files) == {
        "ads - a.strm": "https://archive.org/download/ads/a.mp4",
        "ads - b.strm": "https://archive.org/download/ads/b.mp4",
    }


def test_assign_names_disambiguates_collisions() -> None:
    files = [VideoFile("ads", "a?.mp4"), VideoFile("ads", "a*.mp4"), VideoFile("ads", "a|.mp4")]
    names = assign_names(files)
    assert set(names) == {"ads - a.strm", "ads - a (2).strm", "ads - a (3).strm"}
    assert len(set(names.values())) == 3
