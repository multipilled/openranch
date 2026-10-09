using System.Numerics;
using OpenRanch.Formats.Game;

namespace OpenRanch.Simulation;

/// <summary>One thing that pops out of a bursting gordo: its item id, where (an offset from the gordo, Unity coordinates) and which way it faces.</summary>
public sealed record GordoSpawn(string Id, Vector3 Offset, Vector3 Facing);

/// <summary>
/// A gordo's appetite: it eats anything in its slime's diet that touches it, any time, until it has
/// eaten its target count, then strains and bursts into its rewards and a ring of its slimes. The
/// rules are in docs/behavior/gordos.md; the target and rewards come from the install
/// (<see cref="GordoInfo"/>), the few fixed numbers from the game's code (static analysis of GordoEat
/// and GordoRewardsBase).
/// </summary>
public sealed class Gordo
{
    /// <summary>Seconds between reaching the target (straining) and bursting. Code-only.</summary>
    public const float BurstDelaySeconds = 2f;
    /// <summary>Above this share fed the gordo starts to wobble. Code-only.</summary>
    public const float VibrateFromFed = 0.7f;
    /// <summary>How many things a burst spawns: the centre point and twelve around it. Code-only.</summary>
    public const int SpawnPoints = 13;
    /// <summary>Spawn points sit this far from the gordo's centre (times their unit offsets)...</summary>
    public const float SpawnRadius = 1.2f;
    /// <summary>...and this far above it. Code-only.</summary>
    public const float SpawnHeight = 1.7f;
    /// <summary>Each spawned thing gets a random spin of up to this much about each axis. Code-only.</summary>
    public const float SpawnTorque = 10f;

    /// <summary>
    /// The unit offsets of the spawn points (Unity coordinates): the centre, six round the middle
    /// every 60 degrees, three above at 30, 150 and 270 degrees and three below at -30, 90 and 210,
    /// each of those half out and 0.866 up or down. Code-only (GordoRewardsBase).
    /// </summary>
    public static readonly IReadOnlyList<Vector3> SpawnOffsets = BuildOffsets();

    private readonly SlimeSpecies _diet;

    public Gordo(GordoInfo info, SlimeSpecies diet)
    {
        Info = info;
        _diet = diet;
    }

    public GordoInfo Info { get; }
    /// <summary>How much it has eaten (each meal counts 1, a favourite its slime's favourite production count).</summary>
    public int EatenCount { get; private set; }
    public int TargetCount => Info.TargetCount;
    /// <summary>It has eaten enough and is straining, about to burst (or has burst).</summary>
    public bool Full => EatenCount >= TargetCount;
    public bool HasBurst { get; private set; }
    /// <summary>How far along it is, 0 to 1 (1 once burst).</summary>
    public float Fed => HasBurst || TargetCount <= 0 ? 1f : Math.Min(1f, EatenCount / (float)TargetCount);

    /// <summary>Its size relative to its starting size: from 1 unfed to the growth factor fully fed.</summary>
    public float Scale => 1f + (Info.GrowthFactor - 1f) * Fed;

    /// <summary>How strongly it wobbles (0 to 1 of its vibration): nothing until 70% fed, then up to full at 100%.</summary>
    public float Wobble => Fed <= VibrateFromFed ? 0f : (Fed - VibrateFromFed) / (1f - VibrateFromFed);

    /// <summary>Whether it would eat <paramref name="food"/> now: in its slime's diet, and it isn't full yet.</summary>
    public bool WillEat(string food) => !Full && MealCount(food) > 0;

    /// <summary>
    /// Eats <paramref name="food"/> if it will; returns how much the meal counts (0 when it won't eat
    /// it). The gordo goes by its slime's first eat rule for the food: a favourite counts the
    /// favourite production count, anything else 1. Hunger plays no part.
    /// </summary>
    public int Feed(string food)
    {
        if (!WillEat(food))
            return 0;
        var count = MealCount(food);
        EatenCount += count;
        return count;
    }

    private int MealCount(string food) =>
        _diet.FoodEffect(food) is { Becomes: null, Produces.Count: > 0 } effect ? (effect.IsFavorite ? effect.CountEach : 1) : 0;

    /// <summary>
    /// Bursts: what pops out and where. The rewards go first, the first at the centre point and the rest
    /// at spawn points picked at random; every point left over gets one of the gordo's slimes.
    /// <paramref name="rewards"/> defaults to the gordo's own (pass a game mode's override instead).
    /// </summary>
    public IReadOnlyList<GordoSpawn> Burst(Random random, IReadOnlyList<string>? rewards = null)
    {
        HasBurst = true;
        rewards ??= Info.Rewards;
        var free = SpawnOffsets.Skip(1).ToList();
        var spawns = new List<GordoSpawn>();
        for (var i = 0; free.Count > 0; i++)
        {
            var id = i < rewards.Count ? rewards[i] : Info.FillSlime;
            Vector3 unit;
            if (i == 0)
                unit = SpawnOffsets[0];
            else
            {
                var pick = random.Next(free.Count);
                unit = free[pick];
                free.RemoveAt(pick);
            }
            if (id is not null)
                spawns.Add(new GordoSpawn(id, unit * SpawnRadius + new Vector3(0, SpawnHeight, 0), i == 0 ? Vector3.UnitZ : unit));
        }
        return spawns;
    }

    private static Vector3[] BuildOffsets()
    {
        var points = new Vector3[SpawnPoints];
        points[0] = Vector3.Zero;
        for (var i = 0; i < 6; i++)
        {
            var a = MathF.PI * 2 * i / 6;
            points[1 + i] = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
        }
        for (var i = 0; i < 3; i++)
        {
            var up = MathF.PI * 2 * i / 3 + MathF.PI / 6;
            points[7 + i] = new Vector3(MathF.Cos(up) * 0.5f, 0.866f, MathF.Sin(up) * 0.5f);
            var down = MathF.PI * 2 * i / 3 - MathF.PI / 6;
            points[10 + i] = new Vector3(MathF.Cos(down) * 0.5f, -0.866f, MathF.Sin(down) * 0.5f);
        }
        return points;
    }
}
