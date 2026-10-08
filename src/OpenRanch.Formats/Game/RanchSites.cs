using System.Numerics;
using OpenRanch.Formats.Scene;
using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Formats.Game;

/// <summary>A script component placed in a scene: its object's path and world matrix, its fields, and the colliders beside it.</summary>
public sealed record PlacedScript(string Path, Matrix4x4 World, string Class, SerializedObject Data, IReadOnlyList<ColliderData> Colliders);

/// <summary>Finds script components of given classes on the active objects under one root object of a scene.</summary>
public static class SceneScripts
{
    public static List<PlacedScript> Find(GameScripts scripts, SerializedFile scene, string rootName, params string[] classes)
    {
        var assets = scripts.Assets;
        var wanted = new HashSet<string>(classes, StringComparer.Ordinal);
        var found = new List<PlacedScript>();
        if (!ZoneExtractor.RootObjects(assets, scene).TryGetValue(rootName, out var root))
            return found;

        var stack = new Stack<(AssetRef Transform, Matrix4x4 Parent, string Path)>();
        stack.Push((root, Matrix4x4.Identity, ""));
        while (stack.Count > 0)
        {
            var (transformRef, parent, parentPath) = stack.Pop();
            var t = assets.Read(transformRef, TransformData.Read);
            var go = assets.Read(scene, t.GameObject, GameObjectData.Read);
            if (go is null || !go.IsActive)
                continue;
            var world = t.LocalMatrix * parent;
            var path = parentPath.Length == 0 ? go.Name : parentPath + "/" + go.Name;

            List<ColliderData>? colliders = null;
            foreach (var c in go.Components)
            {
                if (assets.Resolve(scene, c) is not { ClassId: UnityClassId.MonoBehaviour } behaviour)
                    continue;
                var (_, enabled, script, _) = MonoBehaviourReader.ReadHeader(assets.Reader(behaviour));
                if (!enabled || assets.Resolve(scene, script) is not { } scriptRef
                    || !wanted.Contains(assets.Read(scriptRef, MonoBehaviourReader.ReadMonoScript).ClassName))
                    continue;
                var mb = scripts.Reader.Read(behaviour);
                if (mb.Data is null || mb.ScriptClass is null)
                    continue;
                colliders ??= Colliders(assets, scene, go);
                found.Add(new PlacedScript(path, world, mb.ScriptClass, mb.Data, colliders));
            }
            foreach (var child in t.Children)
                if (assets.Resolve(scene, child) is { } childRef)
                    stack.Push((childRef, world, path));
        }
        return found;
    }

    private static List<ColliderData> Colliders(AssetSet assets, SerializedFile scene, GameObjectData go)
    {
        var list = new List<ColliderData>();
        foreach (var c in go.Components)
        {
            if (assets.Resolve(scene, c) is { ClassId: UnityClassId.BoxCollider or UnityClassId.SphereCollider or UnityClassId.CapsuleCollider } col)
                list.Add(assets.Read(col, x => ColliderData.Read(x, col.ClassId)));
        }
        return list;
    }
}

/// <summary>A plort market's deposit hole: a trigger sphere (world matrix, centre and radius in its own space).</summary>
public sealed record MarketSite(string Path, Matrix4x4 World, Vector3 Center, float Radius);

/// <summary>The inside of a corral: a trigger box (world matrix, centre and size in its own space).</summary>
public sealed record CorralSite(string Path, Matrix4x4 World, Vector3 Center, Vector3 Size)
{
    public bool Contains(Vector3 point)
    {
        if (!Matrix4x4.Invert(World, out var toLocal))
            return false;
        var p = Vector3.Transform(point, toLocal) - Center;
        return Math.Abs(p.X) <= Size.X / 2 && Math.Abs(p.Y) <= Size.Y / 2 && Math.Abs(p.Z) <= Size.Z / 2;
    }
}

/// <summary>
/// The ranch's working parts that milestone 2 needs, found by their script components in the world
/// scene: plort market deposit holes (ScorePlort) and corral insides (CorralRegion). See
/// docs/behavior/plort-market.md and docs/behavior/corrals.md.
/// </summary>
public sealed record RanchSites(IReadOnlyList<MarketSite> Markets, IReadOnlyList<CorralSite> Corrals)
{
    public static RanchSites Read(GameScripts scripts, SerializedFile scene, string rootName)
    {
        var markets = new List<MarketSite>();
        var corrals = new List<CorralSite>();
        foreach (var s in SceneScripts.Find(scripts, scene, rootName, "ScorePlort", "CorralRegion"))
        {
            foreach (var c in s.Colliders.Where(c => c.IsTrigger && c.Enabled))
            {
                if (s.Class == "ScorePlort" && c.Shape == ColliderShape.Sphere)
                    markets.Add(new MarketSite(s.Path, s.World, c.Center, c.Radius));
                else if (s.Class == "CorralRegion" && c.Shape == ColliderShape.Box)
                    corrals.Add(new CorralSite(s.Path, s.World, c.Center, c.Size));
            }
        }
        return new RanchSites(markets, corrals);
    }
}

/// <summary>One trigger capsule of the vacpack's suction zone, as a segment and radius in the first-person camera's space.</summary>
public sealed record VacCapsule(Vector3 A, Vector3 B, float Radius)
{
    public bool Contains(Vector3 point)
    {
        var ab = B - A;
        var t = ab.LengthSquared() > 0 ? Math.Clamp(Vector3.Dot(point - A, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
        return Vector3.DistanceSquared(point, A + ab * t) <= Radius * Radius;
    }
}

/// <summary>
/// The vacpack's tuning from the player rig's WeaponVacuum component, and its suction zone: the
/// nozzle (the "vac shape" object) and the trigger capsules under it, all in the first-person
/// camera's space (Unity coordinates). See docs/behavior/vacpack.md.
/// </summary>
public sealed record VacuumTuning(
    float EjectSpeed,
    float ShootCooldown,
    float MaxVacDist,
    float CaptureDist,
    float MinJointSpeed,
    float MaxJointSpeed,
    Matrix4x4 NozzleToCamera,
    IReadOnlyList<VacCapsule> Cone)
{
    public bool InCone(Vector3 cameraSpacePoint) => Cone.Any(c => c.Contains(cameraSpacePoint));

    public static VacuumTuning Read(GameScripts scripts, SerializedFile scene, string rigName = "SimplePlayer", string cameraName = "FPSCamera")
    {
        var vacuum = SceneScripts.Find(scripts, scene, rigName, "WeaponVacuum").FirstOrDefault()
                     ?? throw new InvalidDataException($"No WeaponVacuum on {rigName}.");
        var d = vacuum.Data;
        var assets = scripts.Assets;

        // The camera's world matrix, to express the nozzle and capsules relative to it.
        var cameraPath = rigName + "/" + cameraName;
        Matrix4x4? cameraWorld = null;
        var nozzleWorld = Matrix4x4.Identity;
        var capsules = new List<(Matrix4x4 World, ColliderData Collider)>();
        var nozzleId = d["vacOrigin"] is PPtr p ? p.PathId : 0;
        var regionId = d["vacRegion"] is PPtr q ? q.PathId : 0;
        Walk(assets, scene, rigName, (path, world, goId, go, inRegion) =>
        {
            if (path == cameraPath)
                cameraWorld = world;
            if (goId == nozzleId)
                nozzleWorld = world;
            if (!inRegion)
                return goId == regionId;
            foreach (var c in go.Components)
                if (assets.Resolve(scene, c) is { ClassId: UnityClassId.CapsuleCollider } col)
                    capsules.Add((world, assets.Read(col, x => ColliderData.Read(x, col.ClassId))));
            return true;
        });
        if (cameraWorld is null || !Matrix4x4.Invert(cameraWorld.Value, out var toCamera))
            throw new InvalidDataException($"No {cameraPath} in the world scene.");

        var cone = new List<VacCapsule>();
        foreach (var (world, c) in capsules.Where(c => c.Collider.IsTrigger && c.Collider.Enabled))
        {
            var axis = c.Direction switch { 0 => Vector3.UnitX, 2 => Vector3.UnitZ, _ => Vector3.UnitY };
            var half = Math.Max(0, c.Height / 2 - c.Radius);
            var m = world * toCamera;
            cone.Add(new VacCapsule(Vector3.Transform(c.Center - axis * half, m), Vector3.Transform(c.Center + axis * half, m), c.Radius));
        }
        return new VacuumTuning(
            d.Get<float>("ejectSpeed"), d.Get<float>("shootCooldown"), d.Get<float>("maxVacDist"), d.Get<float>("captureDist"),
            d.Get<float>("minJointSpeed"), d.Get<float>("maxJointSpeed"), nozzleWorld * toCamera, cone);
    }

    // Walks active objects under a root; the visitor gets whether the object is inside the vac
    // region and returns whether its children are.
    private static void Walk(AssetSet assets, SerializedFile scene, string rootName,
        Func<string, Matrix4x4, long, GameObjectData, bool, bool> visit)
    {
        if (!ZoneExtractor.RootObjects(assets, scene).TryGetValue(rootName, out var root))
            return;
        var stack = new Stack<(AssetRef, Matrix4x4, string, bool)>();
        stack.Push((root, Matrix4x4.Identity, "", false));
        while (stack.Count > 0)
        {
            var (transformRef, parent, parentPath, inRegion) = stack.Pop();
            var t = assets.Read(transformRef, TransformData.Read);
            var go = assets.Read(scene, t.GameObject, GameObjectData.Read);
            if (go is null || !go.IsActive)
                continue;
            var world = t.LocalMatrix * parent;
            var path = parentPath.Length == 0 ? go.Name : parentPath + "/" + go.Name;
            var childrenInRegion = visit(path, world, t.GameObject.PathId, go, inRegion);
            foreach (var child in t.Children)
                if (assets.Resolve(scene, child) is { } childRef)
                    stack.Push((childRef, world, path, childrenInRegion));
        }
    }
}

/// <summary>How long a game day lasts in real seconds, from the TimeDirector component. See docs/behavior/slimes.md, "Game time".</summary>
public static class GameTimeData
{
    public static float SecondsPerGameDay(GameScripts scripts)
    {
        var seconds = scripts.OfClass("TimeDirector").Select(t => t.Data.Data!.Get<float>("secsPerGameDay")).FirstOrDefault(s => s > 0);
        return seconds > 0 ? seconds : throw new InvalidDataException("No TimeDirector was found in the install.");
    }
}
