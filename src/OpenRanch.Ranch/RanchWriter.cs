using System.Numerics;

namespace OpenRanch.Ranch;

/// <summary>An actor as the running world holds it now: in the original's world units and Euler degrees.</summary>
/// <param name="ActorId">The save's id when the actor came from the loaded save; null for one that appeared in play.</param>
/// <param name="TypeId">What it is (<see cref="GameEnum.ItemId"/>).</param>
/// <param name="Emotions">The moods the world models (<see cref="GameEnum.Emotion"/>); others keep the saved value.</param>
public sealed record LiveActor(long? ActorId, int TypeId, Vec3 Position, Vec3 Rotation, IReadOnlyDictionary<int, float> Emotions);

/// <summary>A plot as the running world holds it: what is built on the site and the upgrades bought.</summary>
public sealed record LivePlot(int Type, IReadOnlyList<int> Upgrades);

/// <summary>
/// What the running world changes about a ranch. Everything not given here is written as loaded.
/// </summary>
public sealed class LiveRanch
{
    /// <summary>Newbucks on hand.</summary>
    public int Money { get; init; }
    /// <summary>Newbucks earned since the ranch was loaded; added to the money earned over the game.</summary>
    public int MoneyEarned { get; init; }
    /// <summary>The world clock now, in game seconds (<see cref="WorldClock"/>).</summary>
    public double WorldTime { get; init; }

    /// <summary>Plots the world has built or upgraded, by site id.</summary>
    public IReadOnlyDictionary<string, LivePlot> Plots { get; init; } = new Dictionary<string, LivePlot>();
    /// <summary>Progress counters the world has changed (<see cref="GameEnum.Progress"/>).</summary>
    public IReadOnlyDictionary<int, int> Progress { get; init; } = new Dictionary<int, int>();
    /// <summary>Expansion doors the world has changed (<see cref="GameEnum.AccessDoorState"/>), by door id.</summary>
    public IReadOnlyDictionary<string, int> AccessDoors { get; init; } = new Dictionary<string, int>();
    /// <summary>The vacpack's slots now, for each vacpack mode the world runs (<see cref="GameEnum.AmmoMode"/>); other modes keep the saved slots.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<AmmoSlot>> Ammo { get; init; } = new Dictionary<int, IReadOnlyList<AmmoSlot>>();

    /// <summary>
    /// The saved actors the world took in. Those still in <see cref="Actors"/> are written as they are
    /// now; the rest are gone from the game (eaten, sold, sucked up) and are left out.
    /// </summary>
    public IReadOnlySet<long> TrackedActorIds { get; init; } = new HashSet<long>();
    /// <summary>Every actor in the world now, the saved ones it took in and those that appeared in play.</summary>
    public IReadOnlyList<LiveActor> Actors { get; init; } = [];
}

/// <summary>
/// Saves the live ranch: the ranch as it was loaded, with what the running world changed laid over it.
/// Saved actors the world never took in, and every part of the ranch it doesn't model yet, pass
/// through unchanged, so a ranch loaded and written again without play reads back the same.
/// The rules are in docs/behavior/ranch-saves.md, "Saving the live ranch".
/// </summary>
public static class RanchWriter
{
    /// <summary>
    /// The first id the original hands to an actor made in play (static analysis: its actor registry
    /// gives ids below this to fixed actors such as the player and starts dynamic ones here).
    /// </summary>
    public const long FirstDynamicActorId = 100;

    /// <summary>The ranch to write: a copy of <paramref name="loaded"/> with <paramref name="live"/> applied. Neither input is changed.</summary>
    public static RanchState Merge(RanchState loaded, LiveRanch live)
    {
        // A deep copy through openranch's own format: every member the format holds comes along.
        var ranch = RanchSave.FromBytes(RanchSave.ToBytes(loaded));

        ranch.WorldTime = live.WorldTime;
        ranch.Player.Money = live.Money;
        ranch.Player.MoneyEverCollected = checked(ranch.Player.MoneyEverCollected + Math.Max(0, live.MoneyEarned));
        foreach (var (type, count) in live.Progress)
            ranch.Player.Progress[type] = count;
        foreach (var (door, state) in live.AccessDoors)
            ranch.AccessDoors[door] = state;
        foreach (var (mode, slots) in live.Ammo)
            ranch.Player.Ammo[mode] = slots.Select(s => s with { Emotions = new Dictionary<int, float>(s.Emotions) }).ToList();

        foreach (var (id, plot) in live.Plots)
        {
            var saved = ranch.FindPlot(id) ?? throw new ArgumentException($"The ranch has no plot site {id}.", nameof(live));
            saved.Type = plot.Type;
            saved.Upgrades = [.. plot.Upgrades];
        }

        ranch.Actors = MergeActors(ranch.Actors, live, ranch.Player.RegionSetId);
        return ranch;
    }

    private static List<Actor> MergeActors(List<Actor> saved, LiveRanch live, int regionSetId)
    {
        var now = new Dictionary<long, LiveActor>();
        foreach (var actor in live.Actors)
            if (actor.ActorId is { } id && !now.TryAdd(id, actor))
                throw new ArgumentException($"Actor {id} is in the world twice.", nameof(live));
        foreach (var id in now.Keys)
            if (!live.TrackedActorIds.Contains(id))
                throw new ArgumentException($"Actor {id} is in the world but wasn't taken in from the save.", nameof(live));

        // Saved actors keep their place in the list; the ones that are gone are dropped.
        var result = new List<Actor>(saved.Count + live.Actors.Count);
        foreach (var actor in saved)
        {
            if (!live.TrackedActorIds.Contains(actor.ActorId))
                result.Add(actor);
            else if (now.TryGetValue(actor.ActorId, out var current))
                result.Add(Apply(actor, current));
        }

        // Actors that appeared in play get the next free ids, the way the original numbers them
        // (static analysis: loading an actor moves its next id past the loaded one). They are in the
        // world the player is in, and every timer the world doesn't model stays at 0, the value the
        // original's actor record starts with (static analysis: its save code fills per kind only
        // the members that kind has).
        var appeared = live.Actors.Where(a => a.ActorId is null).ToList();
        if (appeared.Count == 0)
            return result;
        var nextId = Math.Max(FirstDynamicActorId, saved.Count == 0 ? 0 : checked(saved.Max(a => a.ActorId) + 1));
        foreach (var actor in appeared)
            result.Add(Apply(new Actor { ActorId = checked(nextId++), RegionSetId = regionSetId }, actor));
        return result;
    }

    private static Actor Apply(Actor actor, LiveActor live)
    {
        actor.TypeId = live.TypeId;
        actor.Position = live.Position;
        actor.Rotation = live.Rotation;
        foreach (var (emotion, level) in live.Emotions)
            actor.Emotions[emotion] = Math.Clamp(level, 0f, 1f);
        return actor;
    }

    /// <summary>
    /// The Euler angles the original stores for a rotation (its engine's convention: degrees from 0 up
    /// to 360, applied about z, then x, then y). <paramref name="unity"/> is the rotation in the
    /// original's left-handed axes.
    /// </summary>
    public static Vec3 EulerDegrees(Quaternion unity)
    {
        var m = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(unity));
        // System.Numerics matrices are row-vector: m.Mij is row i, column j of the transposed rotation,
        // so the column-vector element Rij is m.Mji. For R = Ry(y) Rx(x) Rz(z): R12 = -sin x,
        // R02 = sin y cos x, R22 = cos y cos x, R10 = cos x sin z, R11 = cos x cos z.
        var sinX = Math.Clamp(-m.M32, -1f, 1f);
        float x = MathF.Asin(sinX), y, z;
        if (MathF.Abs(sinX) < 0.99999f)
        {
            y = MathF.Atan2(m.M31, m.M33);
            z = MathF.Atan2(m.M12, m.M22);
        }
        else
        {
            // Looking straight up or down: only y - z (or y + z) is known; put it all in y.
            y = MathF.Atan2(-m.M13, m.M11);
            z = 0;
        }
        return new Vec3(Degrees(x), Degrees(y), Degrees(z));
    }

    private static float Degrees(float radians)
    {
        var d = radians * (180f / MathF.PI) % 360f;
        if (d < 0)
            d += 360f;
        return d >= 360f ? 0f : d;
    }
}
