using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.SaturdayMorning.Selection;
using Xunit;

namespace Jellyfin.Plugin.SaturdayMorning.Tests;

public class PlaylistPlannerTests
{
    private static readonly SelectionSettings Defaults = new(
        AirDateFrom: new DateOnly(1985, 1, 1),
        AirDateTo: new DateOnly(1989, 12, 31),
        Fallback: MissingDateFallback.Exclude,
        OnePerSeries: true,
        MaxEpisodes: 0,
        CommercialsMin: 2,
        CommercialsMax: 3,
        AvoidRepeatingCommercials: true);

    private static Guid G(int n) => new(n, 0, 0, new byte[8]);

    private static EpisodeCandidate Ep(int id, int series, DateOnly? aired)
        => new(G(id), G(1000 + series), aired, null);

    private static List<Guid> Commercials(int count) => Enumerable.Range(5001, count).Select(G).ToList();

    private static List<EpisodeCandidate> Library()
    {
        var aired = new DateOnly(1987, 9, 12);
        var list = new List<EpisodeCandidate>();
        for (var s = 1; s <= 6; s++)
        {
            for (var e = 0; e < 10; e++)
            {
                list.Add(Ep((s * 100) + e, s, aired));
            }
        }

        list.Add(Ep(9001, 7, new DateOnly(1999, 1, 1)));
        list.Add(Ep(9002, 8, null));
        return list;
    }

    [Fact]
    public void Plan_FollowsEveryRule()
    {
        var plan = PlaylistPlanner.Plan(Library(), Commercials(40), Defaults, new Random(42));

        var episodes = plan.Items.Where(i => i.Kind == PlanItemKind.Episode).Select(i => i.Id).ToList();
        Assert.Equal(6, episodes.Count);
        Assert.Equal(6, plan.EpisodeCount);
        Assert.Equal(6, plan.SeriesCount);
        Assert.DoesNotContain(G(9001), episodes);
        Assert.DoesNotContain(G(9002), episodes);
        Assert.Equal(1, plan.SkippedMissingDate);
        Assert.Equal(PlanItemKind.Episode, plan.Items[0].Kind);
        Assert.Equal(PlanItemKind.Episode, plan.Items[^1].Kind);
        Assert.Equal(plan.Items.Count(i => i.Kind == PlanItemKind.Commercial), plan.CommercialCount);
        Assert.InRange(plan.CommercialCount, 10, 15);
        Assert.False(plan.CommercialPoolExhausted);
    }

    [Fact]
    public void Plan_OnePerSeriesOff_KeepsAllEpisodesInRange()
    {
        var settings = Defaults with { OnePerSeries = false, CommercialsMin = 0, CommercialsMax = 0 };

        var plan = PlaylistPlanner.Plan(Library(), Commercials(1), settings, new Random(1));

        Assert.Equal(60, plan.EpisodeCount);
        Assert.Equal(6, plan.SeriesCount);
    }

    [Fact]
    public void Plan_ShufflesEpisodeOrder()
    {
        var settings = Defaults with { OnePerSeries = false, CommercialsMin = 0, CommercialsMax = 0 };
        var library = Library();

        var plan = PlaylistPlanner.Plan(library, Commercials(1), settings, new Random(1));

        var inputOrder = library.Take(60).Select(e => e.Id);
        Assert.NotEqual(inputOrder, plan.Items.Select(i => i.Id));
    }

    [Fact]
    public void Plan_SeriesFilterRunsBeforeEverythingElse()
    {
        var settings = Defaults with { SeriesIds = new HashSet<Guid> { G(1001), G(1008) } };

        var plan = PlaylistPlanner.Plan(Library(), Commercials(40), settings, new Random(3));

        Assert.Equal(1, plan.EpisodeCount);
        Assert.Equal(1, plan.SkippedMissingDate);
        var seriesOne = Enumerable.Range(100, 10).Select(G).ToHashSet();
        Assert.Contains(plan.Items.Single(i => i.Kind == PlanItemKind.Episode).Id, seriesOne);
    }

    [Fact]
    public void Plan_SeriesFilterMatchingNothing_Throws()
    {
        var settings = Defaults with { SeriesIds = new HashSet<Guid> { G(4242) } };

        Assert.Throws<SelectionException>(() => PlaylistPlanner.Plan(Library(), Commercials(4), settings, new Random(3)));
    }

    [Fact]
    public void Plan_AppliesCapAfterSelection()
    {
        var settings = Defaults with { MaxEpisodes = 4 };

        var plan = PlaylistPlanner.Plan(Library(), Commercials(40), settings, new Random(3));

        Assert.Equal(4, plan.EpisodeCount);
        Assert.Equal(4, plan.SeriesCount);
    }

    [Fact]
    public void Plan_ReportsPoolExhaustion()
    {
        var plan = PlaylistPlanner.Plan(Library(), Commercials(2), Defaults, new Random(3));

        Assert.True(plan.CommercialPoolExhausted);
    }

    [Fact]
    public void Plan_NoEpisodesInRange_Throws()
    {
        var library = new[] { Ep(1, 1, new DateOnly(2001, 1, 1)), Ep(2, 1, null) };

        var ex = Assert.Throws<SelectionException>(
            () => PlaylistPlanner.Plan(library, Commercials(5), Defaults, new Random(1)));

        Assert.Contains("No episodes", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_EmptyLibrary_Throws()
    {
        Assert.Throws<SelectionException>(() => PlaylistPlanner.Plan([], Commercials(5), Defaults, new Random(1)));
    }

    [Fact]
    public void Plan_NoCommercialsButBreaksConfigured_Throws()
    {
        var ex = Assert.Throws<SelectionException>(
            () => PlaylistPlanner.Plan(Library(), [], Defaults, new Random(1)));

        Assert.Contains("commercial", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectionException_SupportsStandardConstructors()
    {
        var inner = new InvalidOperationException("x");

        Assert.NotNull(new SelectionException().Message);
        Assert.Equal("m", new SelectionException("m").Message);
        Assert.Same(inner, new SelectionException("m", inner).InnerException);
    }
}
