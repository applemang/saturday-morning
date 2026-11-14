using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SaturdayMorning.Configuration;
using Jellyfin.Plugin.SaturdayMorning.Selection;
using Xunit;

namespace Jellyfin.Plugin.SaturdayMorning.Tests;

public class ConfigValidatorTests
{
    private static readonly Guid LibA = Guid.Parse("11111111111111111111111111111111");
    private static readonly Guid LibB = Guid.Parse("22222222222222222222222222222222");
    private static readonly Guid Ads = Guid.Parse("33333333333333333333333333333333");
    private static readonly Guid Owner = Guid.Parse("44444444444444444444444444444444");

    private static PluginConfiguration Valid() => new()
    {
        SourceLibraryIds = [LibA.ToString("N"), LibB.ToString()],
        AirDateFrom = "1985-01-01",
        AirDateTo = "1989-12-31",
        CommercialsLibraryId = Ads.ToString("N"),
        PlaylistOwnerUserId = Owner.ToString("N"),
    };

    [Fact]
    public void Defaults_MatchTheSpecification()
    {
        var config = new PluginConfiguration();

        Assert.Empty(config.SourceLibraryIds);
        Assert.Equal(string.Empty, config.AirDateFrom);
        Assert.Equal(string.Empty, config.AirDateTo);
        Assert.Equal(MissingDateFallback.Exclude, config.MissingDateFallback);
        Assert.True(config.OnePerSeries);
        Assert.Equal(0, config.MaxEpisodes);
        Assert.Equal(string.Empty, config.CommercialsLibraryId);
        Assert.Equal(2, config.CommercialsMin);
        Assert.Equal(3, config.CommercialsMax);
        Assert.True(config.AvoidRepeatingCommercials);
        Assert.Equal("Saturday Morning", config.PlaylistName);
        Assert.Equal(string.Empty, config.PlaylistOwnerUserId);
        Assert.True(config.AllSeries);
        Assert.Empty(config.SelectedSeriesIds);
        Assert.Empty(config.CommercialSources);
    }

    [Fact]
    public void Validate_ValidConfig_ProducesTypedSettings()
    {
        var config = Valid();
        config.MissingDateFallback = MissingDateFallback.UseSeriesPremiereYear;
        config.OnePerSeries = false;
        config.MaxEpisodes = 8;
        config.AvoidRepeatingCommercials = false;
        config.PlaylistName = "  Cartoons  ";

        var result = ConfigValidator.Validate(config);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        var v = Assert.IsType<ValidatedConfig>(result.Config);
        Assert.Equal([LibA, LibB], v.SourceLibraryIds);
        Assert.Equal(Ads, v.CommercialsLibraryId);
        Assert.Equal(Owner, v.OwnerUserId);
        Assert.Equal("Cartoons", v.PlaylistName);
        Assert.Equal(
            new SelectionSettings(new DateOnly(1985, 1, 1), new DateOnly(1989, 12, 31), MissingDateFallback.UseSeriesPremiereYear, false, 8, 2, 3, false),
            v.Selection);
    }

    [Fact]
    public void Validate_EmptyConfig_ListsEveryRequiredSetting()
    {
        var result = ConfigValidator.Validate(new PluginConfiguration());

        Assert.False(result.IsValid);
        Assert.Null(result.Config);
        Assert.Contains(result.Errors, e => e.Contains("source librar", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, e => e.Contains("Air date from", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("Air date to", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("Commercials library", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("Playlist owner", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsUnparseableIdsAndDates()
    {
        var config = Valid();
        config.SourceLibraryIds = ["not-a-guid"];
        config.AirDateFrom = "1985/01/01";
        config.CommercialsLibraryId = "nope";
        config.PlaylistOwnerUserId = Guid.Empty.ToString();

        var result = ConfigValidator.Validate(config);

        Assert.Equal(4, result.Errors.Count);
    }

    [Fact]
    public void Validate_RejectsReversedDateRange()
    {
        var config = Valid();
        config.AirDateFrom = "1990-01-01";

        var result = ConfigValidator.Validate(config);

        Assert.Contains(result.Errors, e => e.Contains("before", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MissingToDateWithValidFromDate_ReportsOnlyTheMissingDate()
    {
        var config = Valid();
        config.AirDateTo = string.Empty;

        var result = ConfigValidator.Validate(config);

        Assert.Equal(["Air date to is required."], result.Errors);
    }

    [Fact]
    public void Validate_AcceptsSingleDayRange()
    {
        var config = Valid();
        config.AirDateFrom = "1987-09-12";
        config.AirDateTo = "1987-09-12";

        Assert.True(ConfigValidator.Validate(config).IsValid);
    }

    [Theory]
    [InlineData(-1, 3, 0)]
    [InlineData(4, 3, 0)]
    [InlineData(2, 3, -1)]
    public void Validate_RejectsBadCounts(int min, int max, int cap)
    {
        var config = Valid();
        config.CommercialsMin = min;
        config.CommercialsMax = max;
        config.MaxEpisodes = cap;

        Assert.Single(ConfigValidator.Validate(config).Errors);
    }

    [Fact]
    public void Validate_RejectsBlankPlaylistName()
    {
        var config = Valid();
        config.PlaylistName = "   ";

        Assert.Contains(ConfigValidator.Validate(config).Errors, e => e.Contains("Playlist name", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsUndefinedFallback()
    {
        var config = Valid();
        config.MissingDateFallback = (MissingDateFallback)42;

        Assert.Single(ConfigValidator.Validate(config).Errors);
    }

    [Fact]
    public void Validate_AllSeries_LeavesSeriesFilterEmpty()
    {
        var config = Valid();
        config.SelectedSeriesIds = ["55555555555555555555555555555555"];

        var v = ConfigValidator.Validate(config).Config!;

        Assert.Null(v.Selection.SeriesIds);
    }

    [Fact]
    public void Validate_SelectedSeries_BecomesTheSeriesFilter()
    {
        var s1 = Guid.Parse("55555555555555555555555555555555");
        var s2 = Guid.Parse("66666666666666666666666666666666");
        var config = Valid();
        config.AllSeries = false;
        config.SelectedSeriesIds = [s1.ToString("N"), s2.ToString(), s1.ToString()];

        var v = ConfigValidator.Validate(config).Config!;

        Assert.NotNull(v.Selection.SeriesIds);
        Assert.Equal(new HashSet<Guid> { s1, s2 }, v.Selection.SeriesIds);
    }

    [Fact]
    public void Validate_SelectedSeriesRequiredWhenNotAllSeries()
    {
        var config = Valid();
        config.AllSeries = false;

        Assert.Contains(ConfigValidator.Validate(config).Errors, e => e.Contains("series", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsInvalidSeriesIds()
    {
        var config = Valid();
        config.AllSeries = false;
        config.SelectedSeriesIds = ["nope"];

        Assert.Single(ConfigValidator.Validate(config).Errors);
    }

    [Fact]
    public void Validate_PlaylistDoesNotNeedCommercialSources()
    {
        var config = Valid();
        config.CommercialSources = [new CommercialSource { Identifier = "bad id!" }];

        Assert.True(ConfigValidator.Validate(config).IsValid);
    }

    [Fact]
    public void ValidateSources_ParsesIdentifiersAndPatterns()
    {
        var config = Valid();
        config.CommercialSources =
        [
            new CommercialSource { Identifier = " classic_tv_ads " },
            new CommercialSource { Identifier = "toy-ads.1980s", Include = "*1983*, *1984* ,", Exclude = "*promo*" },
        ];

        var result = ConfigValidator.ValidateSources(config);

        Assert.Empty(result.Errors);
        Assert.Equal(Ads, result.CommercialsLibraryId);
        Assert.Equal(2, result.Sources.Count);
        Assert.Equal("classic_tv_ads", result.Sources[0].Identifier);
        Assert.Empty(result.Sources[0].Include);
        Assert.Equal(["*1983*", "*1984*"], result.Sources[1].Include);
        Assert.Equal(["*promo*"], result.Sources[1].Exclude);
    }

    [Fact]
    public void ValidateSources_RequiresLibraryAndAtLeastOneSource()
    {
        var result = ConfigValidator.ValidateSources(new PluginConfiguration());

        Assert.Equal(2, result.Errors.Count);
        Assert.Empty(result.Sources);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("../etc")]
    [InlineData("-leading")]
    public void ValidateSources_RejectsBadIdentifiers(string identifier)
    {
        var config = Valid();
        config.CommercialSources = [new CommercialSource { Identifier = identifier }];

        Assert.Single(ConfigValidator.ValidateSources(config).Errors);
    }

    [Fact]
    public void ValidateSources_RejectsDuplicates()
    {
        var config = Valid();
        config.CommercialSources = [new CommercialSource { Identifier = "a" }, new CommercialSource { Identifier = "A" }];

        Assert.Contains(ConfigValidator.ValidateSources(config).Errors, e => e.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateSources_RejectsInvalidLibraryId()
    {
        var config = Valid();
        config.CommercialsLibraryId = "x";
        config.CommercialSources = [new CommercialSource { Identifier = "a" }];

        Assert.Single(ConfigValidator.ValidateSources(config).Errors);
    }
}
