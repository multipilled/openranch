using System;
using Godot;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// A box of ash or water that fire or puddle slimes feed on and are safe in (an ash trough's ash, a
/// pond). <see cref="Units"/> is how many bites are left (negative: endless, like a pond).
/// </summary>
public sealed class FeedingPatch(string kind, Vector3 center, Vector3 halfSize, int units = -1)
{
    public const string Ash = "ash", Water = "water";

    public string Kind { get; } = kind;
    public Vector3 Center { get; } = center;
    public Vector3 HalfSize { get; } = halfSize;
    public int Units { get; private set; } = units;
    public bool HasSome => Units != 0;

    /// <summary>Whether a body of <paramref name="radius"/> at <paramref name="point"/> touches the patch.</summary>
    public bool Touches(Vector3 point, float radius)
    {
        var d = (point - Center).Abs() - HalfSize;
        return d.X <= radius && d.Y <= radius && d.Z <= radius;
    }

    /// <summary>Takes one bite; false when none are left.</summary>
    public bool Take()
    {
        if (Units == 0)
            return false;
        if (Units > 0)
            Units--;
        return true;
    }
}
