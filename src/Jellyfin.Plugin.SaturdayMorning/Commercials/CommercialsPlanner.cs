using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.SaturdayMorning.Commercials;

/// <summary>
/// Turns the configured archive.org items into the desired .strm folder contents.
/// </summary>
public static class CommercialsPlanner
{
    /// <summary>
    /// Fetches every source and builds the file set. Any failure aborts the whole run so the
    /// folder is never half-updated.
    /// </summary>
    /// <param name="sources">Validated sources.</param>
    /// <param name="client">Metadata client.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Desired files and per-source counts.</returns>
    /// <exception cref="CommercialsException">A fetch failed or a source has no matching videos.</exception>
    public static async Task<CommercialsPlan> BuildAsync(
        IReadOnlyList<CommercialSourceSpec> sources,
        ArchiveClient client,
        CancellationToken cancellationToken)
    {
        var videos = new List<VideoFile>();
        var summary = new List<SourceSummary>();
        foreach (var source in sources)
        {
            using var metadata = await client.GetMetadataAsync(source.Identifier, cancellationToken).ConfigureAwait(false);
            var chosen = ArchiveSelection.SelectVideoFiles(source.Identifier, metadata.RootElement, source.Include, source.Exclude);
            if (chosen.Count == 0)
            {
                throw new CommercialsException($"archive.org item '{source.Identifier}' has no matching video files.");
            }

            videos.AddRange(chosen);
            summary.Add(new SourceSummary(source.Identifier, chosen.Count));
        }

        var files = StrmNaming.AssignNames(videos)
            .Select(kv => KeyValuePair.Create(kv.Key, StrmNaming.StrmContent(kv.Value)));
        return new CommercialsPlan(new OrderedDictionary<string, string>(files), summary);
    }
}
