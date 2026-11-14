using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.SaturdayMorning.Commercials;

/// <summary>
/// Download URLs and .strm file names for Internet Archive videos.
/// </summary>
public static partial class StrmNaming
{
    private const int MaxNameBytes = 200;
    private const string Extension = ".strm";

    /// <summary>
    /// Builds the direct download URL of a file.
    /// </summary>
    /// <param name="identifier">Item identifier.</param>
    /// <param name="fileName">File name in the item; '/' separates subfolders.</param>
    /// <returns><c>https://archive.org/download/{identifier}/{url-encoded file name}</c>.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1055:URI-like return values should not be strings", Justification = "Kept as the exact encoded text written into .strm files; Uri.ToString() would un-escape it.")]
    public static string DownloadUrl(string identifier, string fileName)
        => "https://archive.org/download/" + identifier + "/"
            + string.Join('/', fileName.Split('/').Select(Uri.EscapeDataString));

    /// <summary>
    /// Returns the .strm body: exactly one line holding the URL.
    /// </summary>
    /// <param name="url">Stream URL.</param>
    /// <returns>The URL and a newline.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Kept as the exact encoded text written into .strm files; Uri.ToString() would un-escape it.")]
    public static string StrmContent(string url) => url + "\n";

    /// <summary>
    /// Derives a safe, readable .strm file name that never contains "sample"
    /// (Jellyfin hides files with that word as sample clips).
    /// </summary>
    /// <param name="identifier">Item identifier.</param>
    /// <param name="fileName">File name in the item.</param>
    /// <returns><c>"{identifier} - {stem}.strm"</c>, sanitised and at most 200 UTF-8 bytes.</returns>
    public static string StrmName(string identifier, string fileName)
    {
        var parts = fileName.Split('/');
        var leaf = parts[^1];
        var dot = leaf.LastIndexOf('.');
        parts[^1] = dot > 0 ? leaf[..dot] : leaf;
        var stem = Clean(string.Join(' ', parts));
        if (stem.Length == 0)
        {
            stem = "video";
        }

        var name = Clean(identifier) + " - " + stem;
        var budget = MaxNameBytes - Extension.Length;
        while (Encoding.UTF8.GetByteCount(name) > budget)
        {
            name = name[..^1];
        }

        return Clean(name) + Extension;
    }

    /// <summary>
    /// Maps unique .strm names to URLs, numbering collisions "(2)", "(3)", ...
    /// </summary>
    /// <param name="files">Videos in a stable order.</param>
    /// <returns>Name to URL, in input order.</returns>
    public static IReadOnlyDictionary<string, string> AssignNames(IEnumerable<VideoFile> files)
    {
        var result = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var video in files)
        {
            var baseName = StrmName(video.Identifier, video.Name);
            var name = baseName;
            for (var n = 2; result.ContainsKey(name); n++)
            {
                name = $"{baseName[..^Extension.Length]} ({n}){Extension}";
            }

            result.Add(name, DownloadUrl(video.Identifier, video.Name));
        }

        return result;
    }

    private static string Clean(string text)
    {
        text = Spaces().Replace(Unsafe().Replace(text, " "), " ").Trim(' ', '.', '-');
        while (Reserved().IsMatch(text))
        {
            text = Reserved().Replace(text, "smpl");
        }

        return text;
    }

    [GeneratedRegex(@"[^A-Za-z0-9 ._()\-]+")]
    private static partial Regex Unsafe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex("sample", RegexOptions.IgnoreCase)]
    private static partial Regex Reserved();
}
