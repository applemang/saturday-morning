namespace Jellyfin.Plugin.SaturdayMorning.Configuration;

/// <summary>
/// One Internet Archive item to take commercials from, as entered on the settings page.
/// </summary>
public class CommercialSource
{
    /// <summary>Gets or sets the item identifier (the part after /details/ in its URL).</summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>Gets or sets comma-separated file name patterns to keep; empty keeps every video.</summary>
    public string Include { get; set; } = string.Empty;

    /// <summary>Gets or sets comma-separated file name patterns to drop.</summary>
    public string Exclude { get; set; } = string.Empty;
}
