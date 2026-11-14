using System;
using Jellyfin.Plugin.SaturdayMorning.Selection;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SaturdayMorning.Configuration;

/// <summary>
/// Persisted plugin settings. Ids are stored as strings and dates as yyyy-MM-dd so the
/// admin page can bind them directly; <see cref="ConfigValidator"/> turns them into types.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the ids of the libraries episodes are drawn from.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "XML serialisation of plugin configuration needs an array.")]
    public string[] SourceLibraryIds { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether every series in the source libraries is in the rotation.</summary>
    public bool AllSeries { get; set; } = true;

    /// <summary>Gets or sets the series in the rotation when <see cref="AllSeries"/> is off.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "XML serialisation of plugin configuration needs an array.")]
    public string[] SelectedSeriesIds { get; set; } = [];

    /// <summary>Gets or sets the first air date to include (yyyy-MM-dd).</summary>
    public string AirDateFrom { get; set; } = string.Empty;

    /// <summary>Gets or sets the last air date to include (yyyy-MM-dd).</summary>
    public string AirDateTo { get; set; } = string.Empty;

    /// <summary>Gets or sets the handling of episodes without a premiere date.</summary>
    public MissingDateFallback MissingDateFallback { get; set; } = MissingDateFallback.Exclude;

    /// <summary>Gets or sets a value indicating whether to keep one episode per series per run.</summary>
    public bool OnePerSeries { get; set; } = true;

    /// <summary>Gets or sets the episode cap per run; 0 means no cap.</summary>
    public int MaxEpisodes { get; set; }

    /// <summary>Gets or sets the id of the commercials library.</summary>
    public string CommercialsLibraryId { get; set; } = string.Empty;

    /// <summary>Gets or sets the Internet Archive items the commercials sync task turns into .strm files.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "XML serialisation of plugin configuration needs an array.")]
    public CommercialSource[] CommercialSources { get; set; } = [];

    /// <summary>Gets or sets the minimum commercials between episodes.</summary>
    public int CommercialsMin { get; set; } = 2;

    /// <summary>Gets or sets the maximum commercials between episodes.</summary>
    public int CommercialsMax { get; set; } = 3;

    /// <summary>Gets or sets a value indicating whether to avoid repeating a commercial within a run.</summary>
    public bool AvoidRepeatingCommercials { get; set; } = true;

    /// <summary>Gets or sets the playlist name.</summary>
    public string PlaylistName { get; set; } = "Saturday Morning";

    /// <summary>Gets or sets the id of the user who owns the playlist.</summary>
    public string PlaylistOwnerUserId { get; set; } = string.Empty;
}
