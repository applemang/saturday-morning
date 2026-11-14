using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.SaturdayMorning.Selection;
using Xunit;

namespace Jellyfin.Plugin.SaturdayMorning.Tests;

public class CommercialInterleaverTests
{
    private static Guid G(int n) => new(n, 0, 0, new byte[8]);

    private static List<Guid> Episodes(int count) => Enumerable.Range(1, count).Select(G).ToList();

    private static List<Guid> Commercials(int count) => Enumerable.Range(1001, count).Select(G).ToList();

    private static List<List<PlanItem>> BreaksBetweenEpisodes(IReadOnlyList<PlanItem> items)
    {
        var breaks = new List<List<PlanItem>>();
        List<PlanItem>? current = null;
        foreach (var item in items)
        {
            if (item.Kind == PlanItemKind.Episode)
            {
                if (current is not null)
                {
                    breaks.Add(current);
                }

                current = [];
            }
            else
            {
                Assert.NotNull(current);
                current.Add(item);
            }
        }

        return breaks;
    }

    [Fact]
    public void Interleave_StartsAndEndsWithEpisodes()
    {
        var result = CommercialInterleaver.Interleave(Episodes(4), Commercials(20), 2, 3, true, new Random(1));

        Assert.Equal(PlanItemKind.Episode, result.Items[0].Kind);
        Assert.Equal(PlanItemKind.Episode, result.Items[^1].Kind);
    }

    [Fact]
    public void Interleave_KeepsEpisodeOrder()
    {
        var episodes = Episodes(5);

        var result = CommercialInterleaver.Interleave(episodes, Commercials(20), 1, 2, true, new Random(1));

        Assert.Equal(episodes, result.Items.Where(i => i.Kind == PlanItemKind.Episode).Select(i => i.Id));
    }

    [Fact]
    public void Interleave_EachBreakSizeIsWithinRange()
    {
        var result = CommercialInterleaver.Interleave(Episodes(30), Commercials(200), 2, 3, true, new Random(11));

        var breaks = BreaksBetweenEpisodes(result.Items);
        Assert.Equal(29, breaks.Count);
        Assert.All(breaks, b => Assert.InRange(b.Count, 2, 3));
        Assert.Contains(breaks, b => b.Count == 2);
        Assert.Contains(breaks, b => b.Count == 3);
        Assert.Equal(breaks.Sum(b => b.Count), result.CommercialsInserted);
    }

    [Fact]
    public void Interleave_BreakSizeComesFromInjectedRandomInclusiveOfMax()
    {
        // First draw is the break size: requesting 3 within [2,3] must yield 3.
        var result = CommercialInterleaver.Interleave(Episodes(2), Commercials(5), 2, 3, false, new ScriptedRandom(3));

        Assert.Equal(3, result.CommercialsInserted);
    }

    [Fact]
    public void Interleave_FixedCount_InsertsExactlyThatMany()
    {
        var result = CommercialInterleaver.Interleave(Episodes(3), Commercials(10), 2, 2, true, new Random(5));

        Assert.All(BreaksBetweenEpisodes(result.Items), b => Assert.Equal(2, b.Count));
        Assert.Equal(4, result.CommercialsInserted);
    }

    [Fact]
    public void Interleave_SingleEpisode_HasNoCommercials()
    {
        var result = CommercialInterleaver.Interleave(Episodes(1), Commercials(10), 2, 3, true, new Random(5));

        Assert.Equal([new PlanItem(G(1), PlanItemKind.Episode)], result.Items);
        Assert.Equal(0, result.CommercialsInserted);
        Assert.False(result.PoolExhausted);
    }

    [Fact]
    public void Interleave_NoEpisodes_ReturnsEmpty()
    {
        var result = CommercialInterleaver.Interleave([], Commercials(10), 2, 3, true, new Random(5));

        Assert.Empty(result.Items);
    }

    [Fact]
    public void Interleave_ZeroRange_InsertsNothingAndNeedsNoPool()
    {
        var result = CommercialInterleaver.Interleave(Episodes(3), [], 0, 0, true, new Random(5));

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(0, result.CommercialsInserted);
    }

    [Fact]
    public void Interleave_NoRepeat_UsesEachCommercialOnceWhilePoolLasts()
    {
        var result = CommercialInterleaver.Interleave(Episodes(6), Commercials(10), 2, 2, true, new Random(9));

        var used = result.Items.Where(i => i.Kind == PlanItemKind.Commercial).Select(i => i.Id).ToList();
        Assert.Equal(10, used.Count);
        Assert.Equal(10, used.Distinct().Count());
        Assert.False(result.PoolExhausted);
    }

    [Fact]
    public void Interleave_NoRepeat_PoolExhausted_AllowsRepeatsAndFlagsIt()
    {
        var result = CommercialInterleaver.Interleave(Episodes(4), Commercials(3), 2, 2, true, new Random(9));

        var used = result.Items.Where(i => i.Kind == PlanItemKind.Commercial).Select(i => i.Id).ToList();
        Assert.Equal(6, used.Count);
        Assert.True(result.PoolExhausted);
        Assert.Equal(3, used.Take(3).Distinct().Count());
        Assert.All(used, id => Assert.Contains(id, Commercials(3)));
    }

    [Fact]
    public void Interleave_NoRepeat_ExactlyEnoughCommercials_IsNotExhausted()
    {
        var result = CommercialInterleaver.Interleave(Episodes(3), Commercials(4), 2, 2, true, new Random(9));

        Assert.False(result.PoolExhausted);
        Assert.Equal(4, result.Items.Where(i => i.Kind == PlanItemKind.Commercial).Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void Interleave_RepeatsAllowed_DrawsIndependentlyFromPool()
    {
        // Break size 2, then draws index 0 twice: the same commercial back to back is legal.
        var result = CommercialInterleaver.Interleave(Episodes(2), Commercials(3), 2, 2, false, new ScriptedRandom(0, 0));

        var used = result.Items.Where(i => i.Kind == PlanItemKind.Commercial).Select(i => i.Id).ToList();
        Assert.Equal([G(1001), G(1001)], used);
        Assert.False(result.PoolExhausted);
    }

    [Fact]
    public void Interleave_EmptyPoolWhenCommercialsNeeded_Throws()
    {
        Assert.Throws<SelectionException>(
            () => CommercialInterleaver.Interleave(Episodes(2), [], 1, 2, true, new Random(1)));
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(3, 2)]
    public void Interleave_InvalidRange_Throws(int min, int max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CommercialInterleaver.Interleave(Episodes(2), Commercials(3), min, max, true, new Random(1)));
    }
}
