using System.Numerics;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Scene;

/// <summary>Which physics layers collide with which, plus the layer names, from the game's project settings.</summary>
public sealed class PhysicsLayers
{
    public const int PlayerLayer = 8;

    public IReadOnlyList<string> Names { get; }
    public IReadOnlyList<uint> CollisionMatrix { get; }

    private PhysicsLayers(List<string> names, List<uint> matrix)
    {
        Names = names;
        CollisionMatrix = matrix;
    }

    public bool Collide(int a, int b) => a < CollisionMatrix.Count && (CollisionMatrix[a] & (1u << b)) != 0;

    public static PhysicsLayers Read(AssetSet assets)
    {
        var settings = assets.File("globalgamemanagers") ?? throw new IOException("globalgamemanagers is missing.");
        List<string> names = [];
        List<uint> matrix = [];
        foreach (var info in settings.Objects)
        {
            var asset = new AssetRef(settings, info);
            if (info.ClassId == 78) // TagManager: tags, then the 32 layer names
            {
                var r = assets.Reader(asset);
                r.ReadArray(x => x.ReadAlignedString());
                names = r.ReadArray(x => x.ReadAlignedString());
            }
            else if (info.ClassId == 55) // PhysicsManager
            {
                var r = assets.Reader(asset);
                r.Skip(12 + 12); // gravity, default material
                r.Skip(4 * 3 + 4 * 2); // thresholds, contact offset, solver iterations
                r.Skip(4); // query flags and adaptive force, padded
                r.Skip(4 * 3); // cloth distance and stiffness, contacts generation
                matrix = r.ReadArray(x => x.ReadUInt32());
            }
        }
        if (matrix.Count != 32)
            throw new InvalidDataException("The physics layer collision matrix wasn't found.");
        return new PhysicsLayers(names, matrix);
    }
}

/// <summary>The player rig from the world scene: where it starts and the size of its body.</summary>
public sealed record PlayerRig(Vector3 Spawn, float Height, float Radius, float SlopeLimitDegrees, float StepOffset, Vector3 Center, float EyeHeight)
{
    public static PlayerRig Read(AssetSet assets, SerializedFile scene, string rootName = "SimplePlayer")
    {
        var roots = ZoneExtractor.RootObjects(assets, scene);
        if (!roots.TryGetValue(rootName, out var root))
            throw new KeyNotFoundException($"No '{rootName}' in the world scene.");
        var t = assets.Read(root, TransformData.Read);
        var go = assets.Read(scene, t.GameObject, GameObjectData.Read)!;

        float height = 2, radius = 0.5f, slope = 45, step = 0.3f;
        var center = new Vector3(0, 1, 0);
        foreach (var c in go.Components)
        {
            if (assets.Resolve(scene, c) is not { ClassId: 143 } controller) // CharacterController
                continue;
            var r = assets.Reader(controller);
            r.Skip(12 + 12); // game object, physic material
            r.Skip(4); // trigger and enabled flags, padded
            height = r.ReadSingle();
            radius = r.ReadSingle();
            slope = r.ReadSingle();
            step = r.ReadSingle();
            r.Skip(8); // skin width, min move distance
            center = r.ReadVector3();
        }

        // The first-person camera is a direct child of the rig.
        var eye = 1.75f;
        foreach (var childPtr in t.Children)
        {
            if (assets.Resolve(scene, childPtr) is not { } childRef)
                continue;
            var child = assets.Read(childRef, TransformData.Read);
            var childGo = assets.Read(scene, child.GameObject, GameObjectData.Read);
            if (childGo?.Name == "FPSCamera")
                eye = child.LocalPosition.Y;
        }
        return new PlayerRig(t.LocalPosition, height, radius, slope, step, center, eye);
    }
}
