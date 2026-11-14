using System;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SaturdayMorning.Commercials;
using Xunit;

namespace Jellyfin.Plugin.SaturdayMorning.Tests;

public class StrmNamingTests
{
    [Fact]
    public void DownloadUrl_EncodesTheFileName()
    {
        Assert.Equal(
            "https://archive.org/download/classic_tv_ads/Cereal%20Ad%201983.mp4",
            StrmNaming.DownloadUrl("classic_tv_ads", "Cereal Ad 1983.mp4"));
    }

    [Fact]
    public void DownloadUrl_KeepsSubdirectorySlashesAndEncodesTheRest()
    {
        Assert.Equal(
            "https://archive.org/download/ads/1980s/Toy%20%231%20%26%20Friends%3F.mp4",
            StrmNaming.DownloadUrl("ads", "1980s/Toy #1 & Friends?.mp4"));
    }

    [Fact]
    public void DownloadUrl_EncodesNonAscii()
    {
        Assert.EndsWith("/caf%C3%A9.mp4", StrmNaming.DownloadUrl("ads", "caf\u00e9.mp4"), StringComparison.Ordinal);
    }

    [Fact]
    public void StrmContent_IsExactlyOneLine()
    {
        Assert.Equal("https://archive.org/download/a/b.mp4\n", StrmNaming.StrmContent("https://archive.org/download/a/b.mp4"));
    }

    [Fact]
    public void StrmName_CombinesIdentifierAndStem()
    {
        Assert.Equal("classic_tv_ads - Cereal Ad 1983.strm", StrmNaming.StrmName("classic_tv_ads", "Cereal Ad 1983.mp4"));
    }

    [Fact]
    public void StrmName_FlattensSubdirectories()
    {
        Assert.Equal("ads - 1980s toys robot.strm", StrmNaming.StrmName("ads", "1980s/toys/robot.mp4"));
    }

    [Theory]
    [InlineData("sample.mp4")]
    [InlineData("Sample Reel.mp4")]
    [InlineData("SAMPLE.mp4")]
    [InlineData("my_sampler.mp4")]
    [InlineData("samsampleple.mp4")]
    [InlineData("s a m p l e/sample.mp4")]
    public void StrmName_NeverContainsSample(string fileName)
    {
        var name = StrmNaming.StrmName("ads", fileName);

        Assert.DoesNotContain("sample", name, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".strm", name, StringComparison.Ordinal);
    }

    [Fact]
    public void StrmName_CleansSampleFromTheIdentifierToo()
    {
        Assert.DoesNotContain("sample", StrmNaming.StrmName("sample_ads", "a.mp4"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StrmName_RemovesUnsafeCharacters()
    {
        Assert.Matches(new Regex(@"^[A-Za-z0-9 ._()\-]+\.strm$"), StrmNaming.StrmName("ads", "bad:<>|*?\"\\name\u0000.mp4"));
    }

    [Fact]
    public void StrmName_FallsBackWhenNothingIsLeft()
    {
        Assert.Equal("ads - video.strm", StrmNaming.StrmName("ads", "!!!.mp4"));
        Assert.DoesNotMatch(new Regex(@"^\."), StrmNaming.StrmName("ads", "....mp4"));
    }

    [Fact]
    public void StrmName_KeepsNamesWithoutAnExtension()
    {
        Assert.Equal("ads - spot.strm", StrmNaming.StrmName("ads", "spot"));
        Assert.Equal("ads - hidden.strm", StrmNaming.StrmName("ads", ".hidden"));
    }

    [Fact]
    public void StrmName_IsLengthCapped()
    {
        var name = StrmNaming.StrmName("ads", new string('x', 400) + ".mp4");

        Assert.True(Encoding.UTF8.GetByteCount(name) <= 200);
        Assert.EndsWith(".strm", name, StringComparison.Ordinal);
    }

    [Fact]
    public void StrmName_LengthCapNeverSplitsACharacter()
    {
        var name = StrmNaming.StrmName("ads", new string('\u00e9', 300) + ".mp4");

        Assert.True(Encoding.UTF8.GetByteCount(name) <= 200);
    }

    [Fact]
    public void AssignNames_MapsNamesToUrlsInOrder()
    {
        var names = StrmNaming.AssignNames([new VideoFile("ads", "a.mp4"), new VideoFile("ads", "b.mp4")]);

        Assert.Equal(["ads - a.strm", "ads - b.strm"], names.Keys);
        Assert.Equal("https://archive.org/download/ads/a.mp4", names["ads - a.strm"]);
    }

    [Fact]
    public void AssignNames_NumbersCollisions()
    {
        var names = StrmNaming.AssignNames(
            [new VideoFile("ads", "a?.mp4"), new VideoFile("ads", "a*.mp4"), new VideoFile("ads", "a|.mp4")]);

        Assert.Equal(["ads - a.strm", "ads - a (2).strm", "ads - a (3).strm"], names.Keys);
    }
}
