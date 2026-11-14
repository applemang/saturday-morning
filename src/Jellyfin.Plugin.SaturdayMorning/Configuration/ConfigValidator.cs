using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.SaturdayMorning.Selection;

namespace Jellyfin.Plugin.SaturdayMorning.Configuration;

/// <summary>
/// Settings after validation, in typed form.
/// </summary>
/// <param name="SourceLibraryIds">Libraries to draw episodes from.</param>
/// <param name="CommercialsLibraryId">Library holding the commercials.</param>
/// <param name="OwnerUserId">User who owns the playlist.</param>
/// <param name="PlaylistName">Playlist name, trimmed.</param>
/// <param name="Selection">Selection rules.</param>
public sealed record ValidatedConfig(
    IReadOnlyList<Guid> SourceLibraryIds,
    Guid CommercialsLibraryId,
    Guid OwnerUserId,
    string PlaylistName,
    SelectionSettings Selection);

/// <summary>
/// Outcome of validating the stored configuration.
/// </summary>
/// <param name="Errors">Human-readable problems; empty when valid.</param>
/// <param name="Config">Typed settings when valid, otherwise null.</param>
public sealed record ConfigValidationResult(IReadOnlyList<string> Errors, ValidatedConfig? Config)
{
    /// <summary>Gets a value indicating whether the configuration is usable.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Checks the stored configuration and converts it to typed settings.
/// </summary>
public static class ConfigValidator
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Validates every setting and reports all problems at once.
    /// </summary>
    /// <param name="config">Stored configuration.</param>
    /// <returns>Errors, or typed settings.</returns>
    public static ConfigValidationResult Validate(PluginConfiguration config)
    {
        var errors = new List<string>();

        var libraries = new List<Guid>();
        if (config.SourceLibraryIds.Length == 0)
        {
            errors.Add("Select at least one source library.");
        }

        foreach (var raw in config.SourceLibraryIds)
        {
            if (TryParseId(raw, out var id))
            {
                libraries.Add(id);
            }
            else
            {
                errors.Add($"Source library id '{raw}' is not valid.");
            }
        }

        var from = ParseDate(config.AirDateFrom, "Air date from", errors);
        var to = ParseDate(config.AirDateTo, "Air date to", errors);
        if (from is { } f && to is { } t && f > t)
        {
            errors.Add("Air date from must be on or before air date to.");
        }

        if (!Enum.IsDefined(config.MissingDateFallback))
        {
            errors.Add("Missing date fallback is not a known option.");
        }

        if (config.MaxEpisodes < 0)
        {
            errors.Add("Maximum episodes per run cannot be negative (0 means no cap).");
        }

        var commercials = ParseRequiredId(config.CommercialsLibraryId, "Commercials library", errors);

        if (config.CommercialsMin < 0)
        {
            errors.Add("Minimum commercials between episodes cannot be negative.");
        }
        else if (config.CommercialsMax < config.CommercialsMin)
        {
            errors.Add("Maximum commercials between episodes must be at least the minimum.");
        }

        var name = config.PlaylistName.Trim();
        if (name.Length == 0)
        {
            errors.Add("Playlist name is required.");
        }

        var owner = ParseRequiredId(config.PlaylistOwnerUserId, "Playlist owner", errors);

        if (errors.Count > 0)
        {
            return new ConfigValidationResult(errors, null);
        }

        var selection = new SelectionSettings(
            from!.Value,
            to!.Value,
            config.MissingDateFallback,
            config.OnePerSeries,
            config.MaxEpisodes,
            config.CommercialsMin,
            config.CommercialsMax,
            config.AvoidRepeatingCommercials);
        return new ConfigValidationResult(errors, new ValidatedConfig(libraries, commercials, owner, name, selection));
    }

    private static bool TryParseId(string raw, out Guid id)
        => Guid.TryParse(raw, out id) && id != Guid.Empty;

    private static Guid ParseRequiredId(string raw, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            errors.Add($"{label} is required.");
            return Guid.Empty;
        }

        if (!TryParseId(raw, out var id))
        {
            errors.Add($"{label} id '{raw}' is not valid.");
        }

        return id;
    }

    private static DateOnly? ParseDate(string raw, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            errors.Add($"{label} is required.");
            return null;
        }

        if (DateOnly.TryParseExact(raw, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors.Add($"{label} '{raw}' is not a yyyy-MM-dd date.");
        return null;
    }
}
