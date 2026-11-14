using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.SaturdayMorning.Selection;

/// <summary>
/// Places commercial breaks between episodes, never before the first or after the last.
/// </summary>
public static class CommercialInterleaver
{
    /// <summary>
    /// Builds the episode sequence with a random-size commercial break between each pair.
    /// </summary>
    /// <param name="episodes">Episodes in play order.</param>
    /// <param name="commercials">Commercial pool.</param>
    /// <param name="min">Minimum commercials per break.</param>
    /// <param name="max">Maximum commercials per break (inclusive).</param>
    /// <param name="avoidRepeats">Use each commercial once until the pool runs out.</param>
    /// <param name="random">Random source.</param>
    /// <returns>The interleaved items, the number of commercials and whether the pool ran out.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is negative or reversed.</exception>
    /// <exception cref="SelectionException">Commercials are needed but the pool is empty.</exception>
    public static InterleaveResult Interleave(
        IReadOnlyList<Guid> episodes,
        IReadOnlyList<Guid> commercials,
        int min,
        int max,
        bool avoidRepeats,
        Random random)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(min);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);

        if (max > 0 && episodes.Count > 1 && commercials.Count == 0)
        {
            throw new SelectionException("The commercials library returned no items, but commercial breaks are configured.");
        }

        var dealer = new Dealer(commercials, avoidRepeats, random);
        var items = new List<PlanItem>();
        var inserted = 0;
        for (var i = 0; i < episodes.Count; i++)
        {
            if (i > 0)
            {
                var breakSize = random.Next(min, max + 1);
                for (var c = 0; c < breakSize; c++)
                {
                    items.Add(new PlanItem(dealer.Draw(), PlanItemKind.Commercial));
                    inserted++;
                }
            }

            items.Add(new PlanItem(episodes[i], PlanItemKind.Episode));
        }

        return new InterleaveResult(items, inserted, dealer.Exhausted);
    }

    private sealed class Dealer
    {
        private readonly IReadOnlyList<Guid> _pool;
        private readonly bool _avoidRepeats;
        private readonly Random _random;
        private IReadOnlyList<Guid> _deck = [];
        private int _next;
        private bool _dealtOnce;

        public Dealer(IReadOnlyList<Guid> pool, bool avoidRepeats, Random random)
        {
            _pool = pool;
            _avoidRepeats = avoidRepeats;
            _random = random;
        }

        public bool Exhausted { get; private set; }

        public Guid Draw()
        {
            if (!_avoidRepeats)
            {
                return _pool[_random.Next(_pool.Count)];
            }

            if (_next >= _deck.Count)
            {
                Exhausted |= _dealtOnce;
                _deck = EpisodeSelection.Shuffle(_pool, _random);
                _next = 0;
                _dealtOnce = true;
            }

            return _deck[_next++];
        }
    }
}
