using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.SaturdayMorning.Commercials;
using Xunit;

namespace Jellyfin.Plugin.SaturdayMorning.Tests;

public sealed class StrmFolderSyncTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sm-sync-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string P(string name) => Path.Combine(_dir, name);

    private string[] Listing() => Directory.EnumerateFileSystemEntries(_dir).Select(Path.GetFileName).Order().ToArray()!;

    [Fact]
    public void WritesNewFiles()
    {
        var report = StrmFolderSync.Sync(_dir, new Dictionary<string, string> { ["a.strm"] = "https://x/a\n", ["b.strm"] = "https://x/b\n" });

        Assert.Equal(new SyncReport(2, 0, 0), report);
        Assert.Equal("https://x/a\n", File.ReadAllText(P("a.strm")));
    }

    [Fact]
    public void RerunWithSameInputChangesNothing()
    {
        var desired = new Dictionary<string, string> { ["a.strm"] = "https://x/a\n" };
        StrmFolderSync.Sync(_dir, desired);
        var before = File.GetLastWriteTimeUtc(P("a.strm"));
        File.SetLastWriteTimeUtc(P("a.strm"), before.AddDays(-1));

        var report = StrmFolderSync.Sync(_dir, desired);

        Assert.Equal(new SyncReport(0, 1, 0), report);
        Assert.Equal(before.AddDays(-1), File.GetLastWriteTimeUtc(P("a.strm")));
    }

    [Fact]
    public void UpdatesChangedContent()
    {
        StrmFolderSync.Sync(_dir, new Dictionary<string, string> { ["a.strm"] = "https://x/old\n" });

        var report = StrmFolderSync.Sync(_dir, new Dictionary<string, string> { ["a.strm"] = "https://x/new\n" });

        Assert.Equal(1, report.Written);
        Assert.Equal("https://x/new\n", File.ReadAllText(P("a.strm")));
    }

    [Fact]
    public void RemovesStrmFilesNoLongerListed()
    {
        StrmFolderSync.Sync(_dir, new Dictionary<string, string> { ["a.strm"] = "1\n", ["b.strm"] = "2\n" });

        var report = StrmFolderSync.Sync(_dir, new Dictionary<string, string> { ["a.strm"] = "1\n" });

        Assert.Equal(new SyncReport(0, 1, 1), report);
        Assert.Equal(["a.strm"], Listing());
    }

    [Fact]
    public void TouchesNothingElse()
    {
        File.WriteAllText(P("notes.txt"), "keep");
        File.WriteAllText(P("a.nfo"), "keep");
        Directory.CreateDirectory(P("sub"));
        File.WriteAllText(Path.Combine(P("sub"), "nested.strm"), "keep");

        StrmFolderSync.Sync(_dir, new Dictionary<string, string>());

        Assert.Equal(["a.nfo", "notes.txt", "sub"], Listing());
        Assert.Equal("keep", File.ReadAllText(Path.Combine(P("sub"), "nested.strm")));
    }

    [Fact]
    public void LeavesNoTemporaryFiles()
    {
        StrmFolderSync.Sync(_dir, new Dictionary<string, string> { ["a.strm"] = "1\n" });

        Assert.Equal(["a.strm"], Listing());
    }

    [Fact]
    public void MissingFolderIsAnError()
    {
        var ex = Assert.Throws<CommercialsException>(
            () => StrmFolderSync.Sync(P("nope"), new Dictionary<string, string>()));
        Assert.Contains("does not exist", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../escape.strm")]
    [InlineData("sub/a.strm")]
    [InlineData("sub\\a.strm")]
    [InlineData("a.txt")]
    [InlineData(".strm")]
    [InlineData(".hidden.strm")]
    public void RejectsUnsafeOrNonStrmNames(string name)
    {
        var ex = Assert.Throws<CommercialsException>(
            () => StrmFolderSync.Sync(_dir, new Dictionary<string, string> { [name] = "x\n" }));
        Assert.Contains("invalid", ex.Message, StringComparison.Ordinal);
        Assert.Empty(Listing());
    }
}
