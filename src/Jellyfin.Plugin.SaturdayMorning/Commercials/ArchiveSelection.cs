using System;
using System.Collections.Generic;
using System.IO.Enumeration;
using System.Linq;
using System.Text.Json;

namespace Jellyfin.Plugin.SaturdayMorning.Commercials;

/// <summary>
/// Chooses playable video files from an Internet Archive Metadata API response.
/// </summary>
public static class ArchiveSelection
{
    // Lower rank wins. MP4 / h.264 derivatives first because every Jellyfin client can
    // direct-play them; other video formats are a fallback. Anything not listed
    // (thumbnails, torrents, metadata, text, GIF previews) is never selected.
    private static readonly Dictionary<string, int> FormatRank = new(StringComparer.Ordinal)
    {
        ["h.264"] = 0,
        ["h.264 IA"] = 1,
        ["MPEG4"] = 2,
        ["512Kb MPEG4"] = 3,
        ["HiRes MPEG4"] = 4,
        ["WebM"] = 10,
        ["Matroska"] = 11,
        ["QuickTime"] = 12,
        ["MPEG2"] = 13,
        ["MPEG1"] = 14,
        ["Ogg Video"] = 15,
        ["Windows Media"] = 16,
        ["Cinepack"] = 17,
        ["AVI"] = 18,
        ["DivX"] = 19,
    };

    /// <summary>
    /// Returns the Metadata API URL of an item.
    /// </summary>
    /// <param name="identifier">Validated item identifier.</param>
    /// <returns>The metadata URL.</returns>
    public static Uri MetadataUri(string identifier) => new("https://archive.org/metadata/" + identifier);

    /// <summary>
    /// Picks the best-ranked video file for each source video in an item.
    /// </summary>
    /// <remarks>
    /// Derivatives name the file they were made from in <c>original</c>; files are grouped by
    /// it. Include patterns, when given, must match the chosen file or its original; exclude
    /// patterns drop a group when they match either.
    /// </remarks>
    /// <param name="identifier">Item identifier.</param>
    /// <param name="metadata">Parsed Metadata API response.</param>
    /// <param name="include">Patterns to keep; empty keeps all.</param>
    /// <param name="exclude">Patterns to drop.</param>
    /// <returns>Chosen files sorted by name.</returns>
    /// <exception cref="CommercialsException">The item does not exist or the response is malformed.</exception>
    public static IReadOnlyList<VideoFile> SelectVideoFiles(
        string identifier,
        JsonElement metadata,
        IReadOnlyList<string> include,
        IReadOnlyList<string> exclude)
    {
        if (metadata.ValueKind != JsonValueKind.Object || !metadata.TryGetProperty("files", out var files))
        {
            throw new CommercialsException($"archive.org item '{identifier}' not found (no files listed).");
        }

        if (files.ValueKind != JsonValueKind.Array)
        {
            throw new CommercialsException($"archive.org item '{identifier}' has malformed metadata.");
        }

        var best = new Dictionary<string, (int Rank, string Name)>(StringComparer.Ordinal);
        foreach (var entry in files.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || StringProperty(entry, "name") is not { } name
                || StringProperty(entry, "format") is not { } format
                || !FormatRank.TryGetValue(format, out var rank))
            {
                continue;
            }

            var group = StringProperty(entry, "original") ?? name;
            if (!best.TryGetValue(group, out var current) || (rank, name).CompareTo((current.Rank, current.Name)) < 0)
            {
                best[group] = (rank, name);
            }
        }

        return best
            .Where(g => (include.Count == 0 || Matches(include, g.Value.Name, g.Key)) && !Matches(exclude, g.Value.Name, g.Key))
            .Select(g => new VideoFile(identifier, g.Value.Name))
            .OrderBy(v => v.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static string? StringProperty(JsonElement entry, string name)
        => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Matches(IReadOnlyList<string> patterns, string chosen, string original)
        => patterns.Any(p => FileSystemName.MatchesSimpleExpression(p, chosen, ignoreCase: true)
            || FileSystemName.MatchesSimpleExpression(p, original, ignoreCase: true));
}
