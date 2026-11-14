using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.SaturdayMorning.Selection;
using Xunit;

namespace Jellyfin.Plugin.SaturdayMorning.Tests;

public class EpisodeSelectionTests
{
    private static readonly DateOnly From = new(1985, 1, 1);
    private static readonly DateOnly To = new(1989, 12, 31);

    private static EpisodeCandidate Ep(int id, int series, DateOnly? aired, int? seriesYear = null)
        => new(G(id), G(1000 + series), aired, seriesYear);

    private static Guid G(int n) => new(n, 0, 0, new byte[8]);

    [Fact]
    public void FilterByAirDate_IncludesBothRangeEdges()
    {
        var episodes = new[] { Ep(1, 1, From), Ep(2, 1, To) };

        var result = EpisodeSelection.FilterByAirDate(episodes, From, To, MissingDateFallback.Exclude);

        Assert.Equal([G(1), G(2)], result.Kept.Select(e => e.Id));
        Assert.Equal(0, result.SkippedMissingDate);
    }

    [Fact]
    public void FilterByAirDate_ExcludesOneDayOutsideEitherEdge()
    {
        var episodes = new[] { Ep(1, 1, From.AddDays(-1)), Ep(2, 1, To.AddDays(1)), Ep(3, 1, new DateOnly(1987, 6, 1)) };

        var result = EpisodeSelection.FilterByAirDate(episodes, From, To, MissingDateFallback.Exclude);

        Assert.Equal([G(3)], result.Kept.Select(e => e.Id));
        Assert.Equal(0, result.SkippedMissingDate);
    }

    [Fact]
    public void FilterByAirDate_ExcludeFallback_SkipsAndCountsUndatedEpisodes()
    {
        var episodes = new[] { Ep(1, 1, null, 1986), Ep(2, 2, null), Ep(3, 3, new DateOnly(1986, 3, 1)) };

        var result = EpisodeSelection.FilterByAirDate(episodes, From, To, MissingDateFallback.Exclude);

        Assert.Equal([G(3)], result.Kept.Select(e => e.Id));
        Assert.Equal(2, result.SkippedMissingDate);
    }

    [Fact]
    public void FilterByAirDate_SeriesYearFallback_UsesSeriesYearWithinRangeYears()
    {
        var episodes = new[]
        {
            Ep(1, 1, null, 1985),
            Ep(2, 2, null, 1989),
            Ep(3, 3, null, 1984),
            Ep(4, 4, null, 1990),
        };

        var result = EpisodeSelection.FilterByAirDate(episodes, From, To, MissingDateFallback.UseSeriesPremiereYear);

        Assert.Equal([G(1), G(2)], result.Kept.Select(e => e.Id));
        Assert.Equal(0, result.SkippedMissingDate);
    }

    [Fact]
    public void FilterByAirDate_SeriesYearFallback_CountsEpisodesWithNoDateAndNoSeriesYear()
    {
        var episodes = new[] { Ep(1, 1, null), Ep(2, 2, null, 1987) };

        var result = EpisodeSelection.FilterByAirDate(episodes, From, To, MissingDateFallback.UseSeriesPremiereYear);

        Assert.Equal([G(2)], result.Kept.Select(e => e.Id));
        Assert.Equal(1, result.SkippedMissingDate);
    }

    [Fact]
    public void FilterByAirDate_SeriesYearFallback_IgnoresSeriesYearWhenEpisodeHasADate()
    {
        var episodes = new[] { Ep(1, 1, new DateOnly(1970, 1, 1), 1987) };

        var result = EpisodeSelection.FilterByAirDate(episodes, From, To, MissingDateFallback.UseSeriesPremiereYear);

        Assert.Empty(result.Kept);
        Assert.Equal(0, result.SkippedMissingDate);
    }

    [Fact]
    public void FilterByAirDate_EmptyInput_ReturnsEmpty()
    {
        var result = EpisodeSelection.FilterByAirDate([], From, To, MissingDateFallback.Exclude);

        Assert.Empty(result.Kept);
        Assert.Equal(0, result.SkippedMissingDate);
    }

    [Fact]
    public void PickOnePerSeries_ManyEpisodes_ReturnsExactlyOnePerSeries()
    {
        var episodes = Enumerable.Range(0, 50).Select(i => Ep(i, i % 5, From)).ToList();

        var picked = EpisodeSelection.PickOnePerSeries(episodes, new Random(7));

        Assert.Equal(5, picked.Count);
        Assert.Equal(5, picked.Select(e => e.SeriesId).Distinct().Count());
        Assert.All(picked, p => Assert.Contains(p, episodes));
    }

    [Fact]
    public void PickOnePerSeries_UsesRandomIndexWithinEachSeries()
    {
        var episodes = new[] { Ep(1, 1, From), Ep(2, 1, From), Ep(3, 1, From), Ep(4, 2, From), Ep(5, 2, From) };

        var picked = EpisodeSelection.PickOnePerSeries(episodes, new ScriptedRandom(2, 1));

        Assert.Equal([G(3), G(5)], picked.Select(e => e.Id));
    }

    [Fact]
    public void PickOnePerSeries_EachEpisodeIsReachable()
    {
        var episodes = new[] { Ep(1, 1, From), Ep(2, 1, From), Ep(3, 1, From) };

        var seen = new HashSet<Guid>();
        for (var i = 0; i < 3; i++)
        {
            seen.Add(EpisodeSelection.PickOnePerSeries(episodes, new ScriptedRandom(i)).Single().Id);
        }

        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void PickOnePerSeries_SingleSeries_ReturnsOneEpisode()
    {
        var episodes = new[] { Ep(1, 1, From), Ep(2, 1, From) };

        var picked = EpisodeSelection.PickOnePerSeries(episodes, new Random(1));

        Assert.Single(picked);
    }

    [Fact]
    public void PickOnePerSeries_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(EpisodeSelection.PickOnePerSeries([], new Random(1)));
    }

    [Fact]
    public void Shuffle_IsAPermutation()
    {
        var input = Enumerable.Range(0, 20).ToList();

        var shuffled = EpisodeSelection.Shuffle(input, new Random(3));

        Assert.Equal(input, shuffled.Order());
    }

    [Fact]
    public void Shuffle_FollowsFisherYatesWithInjectedRandom()
    {
        // i=2 swaps with 0 -> [c,b,a]; i=1 swaps with 1 -> unchanged.
        var shuffled = EpisodeSelection.Shuffle(["a", "b", "c"], new ScriptedRandom(0, 1));

        Assert.Equal(["c", "b", "a"], shuffled);
    }

    [Fact]
    public void Shuffle_DoesNotMutateInput()
    {
        var input = new List<int> { 1, 2, 3 };

        EpisodeSelection.Shuffle(input, new ScriptedRandom(0, 0));

        Assert.Equal([1, 2, 3], input);
    }

    [Fact]
    public void ApplyCap_ZeroMeansNoCap()
    {
        Assert.Equal([1, 2, 3], EpisodeSelection.ApplyCap([1, 2, 3], 0));
    }

    [Fact]
    public void ApplyCap_TakesFirstItemsUpToCap()
    {
        Assert.Equal([1, 2], EpisodeSelection.ApplyCap([1, 2, 3], 2));
    }

    [Fact]
    public void ApplyCap_CapLargerThanInput_ReturnsAll()
    {
        Assert.Equal([1, 2], EpisodeSelection.ApplyCap([1, 2], 10));
    }
}
