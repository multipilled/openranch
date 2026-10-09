using OpenRanch.Formats.Game;
using OpenRanch.Formats.Scene;
using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Ranch;

/// <summary>
/// A barrier of the world scene that keeps the player and slimes out of a ranch expansion (an object
/// with a <c>BarrierController</c> script): its path and game object (path id in the scene file), the
/// access door on the same object if there is one (its saved id and the progress counters opening it
/// grants), the expansions its cell and name refer to (<see cref="GameEnum.Progress"/> values), and
/// where it stands (Unity world matrix).
/// </summary>
public sealed record ExpansionBarrier(string Path, long GameObject, string? DoorId, IReadOnlyList<int> DoorProgress, IReadOnlyList<int> Expansions,
    System.Numerics.Matrix4x4 World = default);

/// <summary>
/// The expansion barriers of one area of the world scene, and which of them a saved ranch has
/// opened. A barrier on an access door is open when the save lists that door as open. The other
/// barriers (the Lab's, the Overgrowth's and the Grotto's, and the passages between them) store
/// nothing that names their expansion; openranch counts them open once every expansion named in
/// their cell's or their own name is unlocked (UNVERIFIED: a stand-in for the original's rule).
/// </summary>
public static class ExpansionBarriers
{
    private const string UnlockPrefix = "UNLOCK_";

    public static IReadOnlyList<ExpansionBarrier> Read(GameScripts scripts, SerializedFile scene, string rootName, GameEnums names)
    {
        var ids = SceneIds.Read(scripts, scene);
        var expansions = names.Get(GameEnum.Progress).Names
            .Where(kv => kv.Value.StartsWith(UnlockPrefix, StringComparison.Ordinal))
            .Select(kv => (Value: (int)kv.Key, Name: kv.Value[UnlockPrefix.Length..]))
            .ToList();
        var barriers = new List<ExpansionBarrier>();
        if (!ZoneExtractor.RootObjects(scripts.Assets, scene).TryGetValue(rootName, out var root))
            return barriers;

        var stack = new Stack<(AssetRef Transform, string Path, string Cell, System.Numerics.Matrix4x4 Parent)>();
        stack.Push((root, "", "", System.Numerics.Matrix4x4.Identity));
        while (stack.Count > 0)
        {
            var (transformRef, parentPath, cell, parent) = stack.Pop();
            var t = scripts.Assets.Read(transformRef, TransformData.Read);
            var world = t.LocalMatrix * parent;
            if (scripts.Assets.Resolve(scene, t.GameObject) is not { } goRef || scripts.Assets.Read(goRef, GameObjectData.Read) is not { IsActive: true } go)
                continue;
            var path = parentPath.Length == 0 ? go.Name : parentPath + "/" + go.Name;
            if (go.Name.StartsWith("cell", StringComparison.Ordinal))
                cell = go.Name;

            var classes = new List<(string Class, AssetRef Ref, SerializedObject? Data)>();
            foreach (var c in go.Components)
                if (scripts.Assets.Resolve(scene, c) is { ClassId: UnityClassId.MonoBehaviour } b && scripts.Reader.Read(b) is { ScriptClass: { } cls } mb)
                    classes.Add((cls, b, mb.Data));
            if (classes.Any(c => c.Class == "BarrierController"))
            {
                // The Lab's door is a LabAccessDoor, derived from AccessDoor.
                var door = classes.FirstOrDefault(c => c.Class == "AccessDoor"
                    || scripts.Types.Find(GameScripts.GameAssembly, "", c.Class)?.DerivesFrom("AccessDoor") == true);
                var doorId = door.Data is not null && ids.TryGetValue(door.Ref.PathId, out var id) ? id : null;
                var progress = door.Data?.List("progress").Select(Convert.ToInt32).ToList() ?? [];
                var words = $"{cell} {go.Name}";
                var named = expansions.Where(e => words.Contains(e.Name, StringComparison.OrdinalIgnoreCase)).Select(e => e.Value).ToList();
                barriers.Add(new ExpansionBarrier(path, goRef.PathId, doorId, progress, named, world));
            }
            foreach (var child in t.Children)
                if (scripts.Assets.Resolve(scene, child) is { } childRef)
                    stack.Push((childRef, path, cell, world));
        }
        return barriers;
    }

    /// <summary>Whether <paramref name="ranch"/> has opened the barrier.</summary>
    public static bool IsOpen(ExpansionBarrier barrier, RanchState ranch, GameEnums names)
    {
        if (barrier.DoorId is not null)
            return ranch.AccessDoors.TryGetValue(barrier.DoorId, out var state) && state == names.Value(GameEnum.AccessDoorState, "OPEN");
        return barrier.Expansions.Count > 0 && barrier.Expansions.All(e => ranch.Player.ProgressOf(e) > 0);
    }
}

/// <summary>Saved ids of a scene's objects: each <c>IdDirector</c> pairs id holders (script components) with their ids.</summary>
public static class SceneIds
{
    /// <summary>Ids by the holder component's path id in <paramref name="scene"/>.</summary>
    public static Dictionary<long, string> Read(GameScripts scripts, SerializedFile scene)
    {
        var ids = new Dictionary<long, string>();
        foreach (var (director, data) in scripts.OfClass("IdDirector"))
        {
            if (director.File != scene)
                continue;
            var keys = data.Data!.List("persistenceKeys");
            var values = data.Data!.List("persistenceValues");
            for (var i = 0; i < Math.Min(keys.Count, values.Count); i++)
                if (keys[i] is PPtr key && values[i] is string id && scripts.Assets.Resolve(scene, key) is { } holder)
                    ids[holder.PathId] = id;
        }
        return ids;
    }
}
