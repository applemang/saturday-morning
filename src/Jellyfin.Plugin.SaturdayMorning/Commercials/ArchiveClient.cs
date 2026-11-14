using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.SaturdayMorning.Commercials;

/// <summary>
/// Polite Metadata API client: sequential, a pause between requests, retries with backoff.
/// </summary>
public sealed class ArchiveClient
{
    private readonly Func<Uri, CancellationToken, Task<HttpResponseMessage>> _send;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly int _retries;
    private readonly TimeSpan _backoff;
    private readonly TimeSpan _pause;
    private bool _used;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveClient"/> class.
    /// </summary>
    /// <param name="send">Sends a GET and returns the response.</param>
    /// <param name="delay">Waits (injectable for tests).</param>
    /// <param name="retries">Extra attempts after the first for transient failures.</param>
    /// <param name="backoff">First retry wait; doubles on each further retry.</param>
    /// <param name="pause">Wait before every request after the first.</param>
    public ArchiveClient(
        Func<Uri, CancellationToken, Task<HttpResponseMessage>> send,
        Func<TimeSpan, CancellationToken, Task> delay,
        int retries,
        TimeSpan backoff,
        TimeSpan pause)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retries);
        _send = send;
        _delay = delay;
        _retries = retries;
        _backoff = backoff;
        _pause = pause;
    }

    /// <summary>
    /// Fetches and parses an item's metadata.
    /// </summary>
    /// <param name="identifier">Validated item identifier.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The parsed JSON; the caller disposes it.</returns>
    /// <exception cref="CommercialsException">Non-retryable status, retries exhausted, or not JSON.</exception>
    public async Task<JsonDocument> GetMetadataAsync(string identifier, CancellationToken cancellationToken)
    {
        var uri = ArchiveSelection.MetadataUri(identifier);
        if (_used && _pause > TimeSpan.Zero)
        {
            await _delay(_pause, cancellationToken).ConfigureAwait(false);
        }

        _used = true;
        var attempts = _retries + 1;
        for (var attempt = 1; ; attempt++)
        {
            TimeSpan? retryAfter = null;
            string reason;
            try
            {
                using var response = await _send(uri, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return await ParseAsync(response, uri, cancellationToken).ConfigureAwait(false);
                }

                var code = (int)response.StatusCode;
                if (response.StatusCode != HttpStatusCode.TooManyRequests && code < 500)
                {
                    throw new CommercialsException($"GET {uri} failed with HTTP {code}.");
                }

                retryAfter = response.StatusCode == HttpStatusCode.TooManyRequests ? response.Headers.RetryAfter?.Delta : null;
                reason = $"HTTP {code}";
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                reason = ex.GetType().Name;
            }

            if (attempt >= attempts)
            {
                throw new CommercialsException($"GET {uri} failed after {attempts} attempts ({reason}).");
            }

            await _delay(retryAfter ?? _backoff * Math.Pow(2, attempt - 1), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage response, Uri uri, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new CommercialsException($"GET {uri} did not return JSON.", ex);
            }
        }
    }
}
