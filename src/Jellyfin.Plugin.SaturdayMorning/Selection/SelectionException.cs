using System;

namespace Jellyfin.Plugin.SaturdayMorning.Selection;

/// <summary>
/// Raised when a run cannot produce a playlist (nothing to select from).
/// </summary>
public class SelectionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SelectionException"/> class.
    /// </summary>
    public SelectionException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectionException"/> class.
    /// </summary>
    /// <param name="message">Reason the run cannot continue.</param>
    public SelectionException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectionException"/> class.
    /// </summary>
    /// <param name="message">Reason the run cannot continue.</param>
    /// <param name="innerException">Underlying error.</param>
    public SelectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
