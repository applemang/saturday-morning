using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.SaturdayMorning.Commercials;

/// <summary>
/// One video file inside an Internet Archive item.
/// </summary>
/// <param name="Identifier">Item identifier.</param>
/// <param name="Name">File name inside the item; may contain '/'.</param>
public sealed record VideoFile(string Identifier, string Name);

/// <summary>
/// A validated Internet Archive item with its file name filters.
/// </summary>
/// <param name="Identifier">Item identifier.</param>
/// <param name="Include">Case-insensitive patterns (* and ?); empty keeps every video.</param>
/// <param name="Exclude">Case-insensitive patterns that drop a video.</param>
public sealed record CommercialSourceSpec(string Identifier, IReadOnlyList<string> Include, IReadOnlyList<string> Exclude);

/// <summary>
/// What a folder sync changed.
/// </summary>
/// <param name="Written">Files created or rewritten.</param>
/// <param name="Unchanged">Files already correct.</param>
/// <param name="Removed">Stale .strm files deleted.</param>
public sealed record SyncReport(int Written, int Unchanged, int Removed);

/// <summary>
/// Videos found for one source.
/// </summary>
/// <param name="Identifier">Item identifier.</param>
/// <param name="Videos">Number of videos chosen.</param>
public sealed record SourceSummary(string Identifier, int Videos);

/// <summary>
/// The desired folder contents for a sync run.
/// </summary>
/// <param name="Files">File name to file content, in a stable order.</param>
/// <param name="PerSource">Video count per source.</param>
public sealed record CommercialsPlan(IReadOnlyDictionary<string, string> Files, IReadOnlyList<SourceSummary> PerSource);

/// <summary>
/// Raised when the commercials cannot be synced; the folder is left as it was.
/// </summary>
public class CommercialsException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommercialsException"/> class.
    /// </summary>
    public CommercialsException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommercialsException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public CommercialsException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommercialsException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">Underlying error.</param>
    public CommercialsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
