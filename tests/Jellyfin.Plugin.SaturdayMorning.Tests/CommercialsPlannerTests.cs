using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SaturdayMorning.Commercials;
using Xunit;

namespace Jellyfin.Plugin.SaturdayMorning.Tests;

public class CommercialsPlannerTests
{
    private static ArchiveClient Client(Dictionary<string, Func<HttpResponseMessage>> routes)
        => new(
            (uri, _) => Task.FromResult(routes[uri.AbsolutePath.Split('/').Last()]()),
            (_, _) => Task.CompletedTask,
            0,
            TimeSpan.Zero,
            TimeSpan.Zero);

    private static Func<HttpResponseMessage> Files(params string[] names)
    {
        var json = "{\"files\":[" + string.Join(',', names.Select(n => $"{{\"name\":\"{n}\",\"format\":\"h.264\"}}")) + "]}";
        return () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8) };
    }

    [Fact]
    public async Task BuildsDesiredStrmFilesFromAllSources()
    {
        var client = Client(new() { ["ads1"] = Files("a.mp4"), ["ads2"] = Files("keep me.mp4", "drop.mp4") });
        CommercialSourceSpec[] sources = [new("ads1", [], []), new("ads2", ["keep*"], [])];

        var result = await CommercialsPlanner.BuildAsync(sources, client, CancellationToken.None);

        Assert.Equal(["ads1 - a.strm", "ads2 - keep me.strm"], result.Files.Keys);
        Assert.Equal("https://archive.org/download/ads2/keep%20me.mp4\n", result.Files["ads2 - keep me.strm"]);
        Assert.Equal([1, 1], result.PerSource.Select(p => p.Videos));
    }

    [Fact]
    public async Task SourceWithNoMatchingVideosFailsTheWholeRun()
    {
        var client = Client(new() { ["ads1"] = Files("a.mp4"), ["ads2"] = Files("drop.mp4") });
        CommercialSourceSpec[] sources = [new("ads1", [], []), new("ads2", ["keep*"], [])];

        var ex = await Assert.ThrowsAsync<CommercialsException>(() => CommercialsPlanner.BuildAsync(sources, client, CancellationToken.None));

        Assert.Contains("ads2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchFailureFailsTheWholeRun()
    {
        var client = Client(new()
        {
            ["ads1"] = Files("a.mp4"),
            ["ads2"] = () => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        await Assert.ThrowsAsync<CommercialsException>(
            () => CommercialsPlanner.BuildAsync([new("ads1", [], []), new("ads2", [], [])], client, CancellationToken.None));
    }

    [Fact]
    public void CommercialsException_SupportsStandardConstructors()
    {
        var inner = new InvalidOperationException("x");

        Assert.NotNull(new CommercialsException().Message);
        Assert.Same(inner, new CommercialsException("m", inner).InnerException);
    }
}
