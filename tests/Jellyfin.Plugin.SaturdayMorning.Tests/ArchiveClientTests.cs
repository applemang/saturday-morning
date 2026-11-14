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

public class ArchiveClientTests
{
    private sealed class Fake
    {
        private readonly Queue<Func<HttpResponseMessage>> _outcomes;

        public Fake(params Func<HttpResponseMessage>[] outcomes) => _outcomes = new Queue<Func<HttpResponseMessage>>(outcomes);

        public List<Uri> Requests { get; } = [];

        public List<TimeSpan> Delays { get; } = [];

        public Task<HttpResponseMessage> Send(Uri uri, CancellationToken token)
        {
            Requests.Add(uri);
            return Task.FromResult(_outcomes.Dequeue()());
        }

        public Task Delay(TimeSpan span, CancellationToken token)
        {
            Delays.Add(span);
            return Task.CompletedTask;
        }

        public ArchiveClient Client(int retries = 4)
            => new(Send, Delay, retries, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
    }

    private static Func<HttpResponseMessage> Json(string body)
        => () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Func<HttpResponseMessage> Status(HttpStatusCode code, int? retryAfterSeconds = null)
        => () =>
        {
            var r = new HttpResponseMessage(code);
            if (retryAfterSeconds is { } s)
            {
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(s));
            }

            return r;
        };

    private static Func<HttpResponseMessage> Throws(Exception ex) => () => throw ex;

    [Fact]
    public async Task FetchesMetadataJson()
    {
        var fake = new Fake(Json("{\"files\":[]}"));

        using var doc = await fake.Client().GetMetadataAsync("ads", CancellationToken.None);

        Assert.Equal(new Uri("https://archive.org/metadata/ads"), fake.Requests.Single());
        Assert.Equal(0, doc.RootElement.GetProperty("files").GetArrayLength());
        Assert.Empty(fake.Delays);
    }

    [Fact]
    public async Task PausesPolitelyBetweenRequests()
    {
        var fake = new Fake(Json("{}"), Json("{}"));
        var client = fake.Client();

        (await client.GetMetadataAsync("a", CancellationToken.None)).Dispose();
        (await client.GetMetadataAsync("b", CancellationToken.None)).Dispose();

        Assert.Equal([TimeSpan.FromSeconds(1)], fake.Delays);
    }

    [Fact]
    public async Task RetriesTransientFailuresWithExponentialBackoff()
    {
        var fake = new Fake(
            Status(HttpStatusCode.ServiceUnavailable),
            Throws(new HttpRequestException("down")),
            Throws(new TaskCanceledException("timeout")),
            Json("{}"));

        (await fake.Client(retries: 3).GetMetadataAsync("a", CancellationToken.None)).Dispose();

        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)], fake.Delays);
    }

    [Fact]
    public async Task HonoursRetryAfterOn429()
    {
        var fake = new Fake(Status(HttpStatusCode.TooManyRequests, 30), Json("{}"));

        (await fake.Client().GetMetadataAsync("a", CancellationToken.None)).Dispose();

        Assert.Equal([TimeSpan.FromSeconds(30)], fake.Delays);
    }

    [Fact]
    public async Task UsesBackoffWhen429HasNoRetryAfter()
    {
        var fake = new Fake(Status(HttpStatusCode.TooManyRequests), Json("{}"));

        (await fake.Client().GetMetadataAsync("a", CancellationToken.None)).Dispose();

        Assert.Equal([TimeSpan.FromSeconds(2)], fake.Delays);
    }

    [Fact]
    public async Task GivesUpAfterRetries()
    {
        var fake = new Fake(Status(HttpStatusCode.InternalServerError), Status(HttpStatusCode.BadGateway));

        var ex = await Assert.ThrowsAsync<CommercialsException>(() => fake.Client(retries: 1).GetMetadataAsync("a", CancellationToken.None));

        Assert.Contains("after 2 attempts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientErrorsAreNotRetried()
    {
        var fake = new Fake(Status(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<CommercialsException>(() => fake.Client().GetMetadataAsync("a", CancellationToken.None));

        Assert.Contains("404", ex.Message, StringComparison.Ordinal);
        Assert.Empty(fake.Delays);
    }

    [Fact]
    public async Task InvalidJsonIsAnError()
    {
        var fake = new Fake(Json("<html>"));

        var ex = await Assert.ThrowsAsync<CommercialsException>(() => fake.Client().GetMetadataAsync("a", CancellationToken.None));

        Assert.Contains("JSON", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellationIsNotRetried()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var fake = new Fake(Throws(new TaskCanceledException("cancelled")));

        await Assert.ThrowsAsync<TaskCanceledException>(() => fake.Client().GetMetadataAsync("a", cts.Token));
        Assert.Empty(fake.Delays);
    }

    [Fact]
    public void RejectsNegativeSettings()
    {
        var fake = new Fake();

        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchiveClient(fake.Send, fake.Delay, -1, TimeSpan.Zero, TimeSpan.Zero));
    }
}
