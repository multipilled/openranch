using System.Numerics;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Ranch;

/// <summary>
/// The cells of one zone as the game registers them for its actors: each cell's <c>Region</c>
/// script carries a world-space bounding box (<c>bounds</c>, centre and half size), and the zone's
/// region set (the ranch is in the "HOME" set; static analysis of <c>Region.Awake</c> and
/// <c>ZoneDirector.GetRegionSetId</c>). An actor belongs to the zone when its saved region set is the
/// zone's and its position is inside one of the boxes (<c>RegionRegistry</c> finds an actor's regions
/// by these boxes).
/// </summary>
public sealed record ZoneRegions(int RegionSet, IReadOnlyList<(string Path, Vector3 Center, Vector3 Extent)> Boxes)
{
    public bool Contains(Actor actor) => actor.RegionSetId == RegionSet && Contains(new Vector3(actor.Position.X, actor.Position.Y, actor.Position.Z));

    public bool Contains(Vector3 p) => Boxes.Any(b =>
        Math.Abs(p.X - b.Center.X) <= b.Extent.X && Math.Abs(p.Y - b.Center.Y) <= b.Extent.Y && Math.Abs(p.Z - b.Center.Z) <= b.Extent.Z);

    /// <param name="regionSet">The zone's region set (<see cref="GameEnum.RegionSet"/>).</param>
    public static ZoneRegions Read(GameScripts scripts, OpenRanch.Formats.Unity.SerializedFile scene, string rootName, int regionSet)
    {
        var boxes = SceneScripts.Find(scripts, scene, rootName, "Region")
            .Where(r => r.Data["bounds"] is ValueTuple<Vector3, Vector3>)
            .Select(r =>
            {
                var (center, extent) = ((Vector3, Vector3))r.Data["bounds"]!;
                return (r.Path, center, extent);
            })
            .ToList();
        return new ZoneRegions(regionSet, boxes);
    }
}

/// <summary>A saved actor to put back into the zone, and whether it hangs from a crop's spawn joint.</summary>
/// <param name="Joint">The spawn joint it hangs from (world position), or null when it lies loose.</param>
/// <param name="Unripe">Still growing: drawn at a third of its size and not vacuumable until ripe.</param>
public sealed record ZoneActor(Actor Saved, string Id, Vector3 Position, Vector3 EulerDegrees, Vector3? Joint, bool Unripe);

/// <summary>
/// The loose actors of a save inside one zone (everything but slimes and largos: food, plorts,
/// chickens, toys, ornaments and so on), and which produce goes back onto a crop. Produce saved as
/// unripe or ripe re-attaches to the nearest crop spawner closer than 10 m, at that crop's nearest
/// spawn joint closer than 0.1 m; when there is none it falls loose as edible (static analysis of
/// <c>ResourceCycle.SetInitState</c>, <c>ResourceCycle.AttachToNearest</c> and
/// <c>SpawnResource.NearestJoint</c>). Unripe produce is a third of its full size. See
/// docs/behavior/plots.md, "Crops".
/// </summary>
public static class ZoneActors
{
    /// <summary>The distance within which produce finds its crop spawner (metres).</summary>
    public const float SpawnerReach = 10f;
    /// <summary>The distance within which produce finds a spawn joint of that spawner (metres).</summary>
    public const float JointReach = 0.1f;
    /// <summary>The size of unripe produce, relative to its full size.</summary>
    public const float UnripeScale = 0.33f;

    /// <param name="hasCycle">Whether an item id grows on crops (its prefab has a <c>ResourceCycle</c> script).</param>
    public static List<ZoneActor> Of(RanchState ranch, ZoneRegions zone, IGameNames names, IReadOnlyList<CropSpawner> spawners, Func<string, bool> hasCycle)
    {
        var unripe = names.Value(GameEnum.ResourceCycleState, "UNRIPE");
        var ripe = names.Value(GameEnum.ResourceCycleState, "RIPE");
        var list = new List<ZoneActor>();
        foreach (var actor in ranch.Actors)
        {
            var id = names.Name(GameEnum.ItemId, actor.TypeId);
            if (OpenRanch.Simulation.Items.KindOf(id) is OpenRanch.Simulation.ItemKind.Slime or OpenRanch.Simulation.ItemKind.Largo || !zone.Contains(actor))
                continue;
            var at = new Vector3(actor.Position.X, actor.Position.Y, actor.Position.Z);
            var euler = new Vector3(actor.Rotation.X, actor.Rotation.Y, actor.Rotation.Z);
            Vector3? joint = null;
            if (hasCycle(id) && (actor.CycleState == unripe || actor.CycleState == ripe))
                joint = NearestJoint(spawners, at);
            list.Add(new ZoneActor(actor, id, at, euler, joint, joint is not null && actor.CycleState == unripe));
        }
        return list;
    }

    /// <summary>The spawn joint produce at <paramref name="at"/> attaches to, or null.</summary>
    public static Vector3? NearestJoint(IReadOnlyList<CropSpawner> spawners, Vector3 at)
    {
        CropSpawner? spawner = null;
        var best = SpawnerReach * SpawnerReach;
        foreach (var s in spawners)
        {
            var d = Vector3.DistanceSquared(s.Position, at);
            if (d < best)
            {
                best = d;
                spawner = s;
            }
        }
        if (spawner is null)
            return null;
        Vector3? joint = null;
        best = JointReach * JointReach;
        foreach (var j in spawner.Joints)
        {
            var d = Vector3.DistanceSquared(j, at);
            if (d < best)
            {
                best = d;
                joint = j;
            }
        }
        return joint;
    }
}
