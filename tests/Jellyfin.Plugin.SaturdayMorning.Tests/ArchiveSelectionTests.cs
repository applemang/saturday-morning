using System;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.SaturdayMorning.Commercials;
using Xunit;

namespace Jellyfin.Plugin.SaturdayMorning.Tests;

public class ArchiveSelectionTests
{
    private static JsonElement Meta(params (string Name, string Format, string? Original)[] files)
    {
        var list = files.Select(f => f.Original is null
            ? (object)new { name = f.Name, format = f.Format, source = "original" }
            : new { name = f.Name, format = f.Format, source = "derivative", original = f.Original });
        return JsonSerializer.SerializeToElement(new { metadata = new { identifier = "ads" }, files = list });
    }

    private static string[] Names(JsonElement meta, string[]? include = null, string[]? exclude = null)
        => ArchiveSelection.SelectVideoFiles("ads", meta, include ?? [], exclude ?? []).Select(v => v.Name).ToArray();

    [Fact]
    public void MetadataUri_PointsAtTheMetadataApi()
    {
        Assert.Equal(new Uri("https://archive.org/metadata/classic_tv_ads"), ArchiveSelection.MetadataUri("classic_tv_ads"));
    }

    [Fact]
    public void PrefersH264DerivativeOverOriginalAndOtherDerivatives()
    {
        var meta = Meta(
            ("spot.avi", "Cinepack", null),
            ("spot.ogv", "Ogg Video", "spot.avi"),
            ("spot.512kb.mp4", "512Kb MPEG4", "spot.avi"),
            ("spot.mp4", "h.264", "spot.avi"));

        Assert.Equal(["spot.mp4"], Names(meta));
        Assert.Equal(new VideoFile("ads", "spot.mp4"), ArchiveSelection.SelectVideoFiles("ads", meta, [], []).Single());
    }

    [Fact]
    public void FallsBackThroughMpeg4Ranks()
    {
        var meta = Meta(
            ("a.mpeg", "MPEG2", null),
            ("a.512kb.mp4", "512Kb MPEG4", "a.mpeg"),
            ("b.mp4", "MPEG4", null),
            ("b.512kb.mp4", "512Kb MPEG4", "b.mp4"));

        Assert.Equal(["a.512kb.mp4", "b.mp4"], Names(meta));
    }

    [Fact]
    public void UsesOtherVideoFormatWhenNoMp4Exists()
    {
        Assert.Equal(["c.mkv"], Names(Meta(("c.mkv", "Matroska", null), ("c.ogv", "Ogg Video", "c.mkv"))));
    }

    [Fact]
    public void SkipsThumbnailsTorrentsMetadataAndUnknownFormats()
    {
        var meta = Meta(
            ("ads_thumb.jpg", "Thumbnail", null),
            ("__ia_thumb.jpg", "Item Tile", null),
            ("ads_archive.torrent", "Archive BitTorrent", null),
            ("ads_meta.xml", "Metadata", null),
            ("notes.txt", "Text", null),
            ("x.gif", "Animated GIF", "x.mp4"),
            ("x.mp4", "h.264", null));

        Assert.Equal(["x.mp4"], Names(meta));
    }

    [Fact]
    public void IncludeMatchesChosenOrOriginalNameCaseInsensitively()
    {
        var meta = Meta(
            ("Toys-1983.avi", "Cinepack", null),
            ("Toys-1983.mp4", "h.264", "Toys-1983.avi"),
            ("Cereal-1984.mp4", "h.264", null),
            ("News.avi", "Cinepack", null),
            ("News.mp4", "h.264", "News.avi"));

        Assert.Equal(["Cereal-1984.mp4", "Toys-1983.mp4"], Names(meta, ["*toys*.avi", "cereal*"]));
    }

    [Fact]
    public void ExcludeWinsOverInclude()
    {
        var meta = Meta(("Toys-1983.mp4", "h.264", null), ("Toys-1990.mp4", "h.264", null));

        Assert.Equal(["Toys-1983.mp4"], Names(meta, ["toys*"], ["*1990*"]));
    }

    [Fact]
    public void OutputIsSortedByName()
    {
        Assert.Equal(["a.mp4", "b.mp4"], Names(Meta(("b.mp4", "h.264", null), ("a.mp4", "h.264", null))));
    }

    [Fact]
    public void MissingFilesMeansItemNotFound()
    {
        var ex = Assert.Throws<CommercialsException>(
            () => ArchiveSelection.SelectVideoFiles("ads", JsonSerializer.SerializeToElement(new { }), [], []));
        Assert.Contains("not found", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonObjectResponseIsMalformed()
    {
        Assert.Throws<CommercialsException>(
            () => ArchiveSelection.SelectVideoFiles("ads", JsonSerializer.SerializeToElement(new[] { 1 }), [], []));
    }

    [Fact]
    public void FilesMustBeAnArray()
    {
        var ex = Assert.Throws<CommercialsException>(
            () => ArchiveSelection.SelectVideoFiles("ads", JsonSerializer.SerializeToElement(new { files = "nope" }), [], []));
        Assert.Contains("malformed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedFileEntriesAreIgnored()
    {
        var meta = JsonSerializer.SerializeToElement(new
        {
            files = new object[] { new { format = "h.264" }, "junk", new { name = 3, format = "h.264" }, new { name = "x.mp4", format = 5 } },
        });

        Assert.Empty(ArchiveSelection.SelectVideoFiles("ads", meta, [], []));
    }
}
