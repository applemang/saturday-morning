using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.SaturdayMorning.Tests;

/// <summary>
/// A <see cref="Random"/> that replays a fixed script of choices so tests can
/// steer every random decision. Each call consumes the next scripted value and
/// clamps it into the requested range; an exhausted script returns the lower bound.
/// </summary>
internal sealed class ScriptedRandom : Random
{
    private readonly Queue<int> _script;

    public ScriptedRandom(params int[] script)
    {
        _script = new Queue<int>(script);
    }

    public int Calls { get; private set; }

    public override int Next(int maxValue) => Next(0, maxValue);

    public override int Next(int minValue, int maxValue)
    {
        Calls++;
        if (maxValue <= minValue)
        {
            return minValue;
        }

        var value = _script.Count > 0 ? _script.Dequeue() : minValue;
        return Math.Clamp(value, minValue, maxValue - 1);
    }
}
