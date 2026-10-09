using System.Numerics;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Scene;

/// <summary>
/// A light the day cycle turns and dims: one under a world-scene object carrying a
/// <c>TimeOfDayRotator</c> script (the sun rig, or the night light's with <see cref="IsNight"/>).
/// Through the day the rig turns about its own X axis and the light follows as its child; the light's
/// brightness is its stored intensity times the sun's or the night light's strength
/// (docs/behavior/day-and-night.md).
/// </summary>
public sealed record TimeOfDayLight(string Path, bool IsNight, Quaternion RigRotation, Quaternion LocalRotation, int Type,
    Vector4 Color, float Intensity)
{
    public const string ScriptClass = "TimeOfDayRotator";
    public const int Directional = 1;

    /// <summary>The light's world rotation (Unity) with the rig turned by <paramref name="degrees"/> about its X axis.</summary>
    public Quaternion Rotation(float degrees) =>
        RigRotation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, degrees * MathF.PI / 180f) * LocalRotation;

    /// <summary>
    /// Reads every light under a time-of-day rig in the scene's "Range Lights" object (the rigs The Ranch
    /// uses; static analysis: TimeOfDayRotator registers its object with AmbianceDirector, which keeps
    /// the object's starting rotation and the starting intensity of every light below it).
    /// </summary>
    public static IReadOnlyList<TimeOfDayLight> Read(AssetSet assets, SerializedFile scene)
    {
        var result = new List<TimeOfDayLight>();
        if (!ZoneExtractor.RootObjects(assets, scene).TryGetValue("Range Lights", out var root))
            return result;

        string? ScriptOf(AssetRef behaviour, out EndianReader r)
        {
            r = assets.Reader(behaviour);
            var (_, enabled, scriptPtr, _) = Unity.Managed.MonoBehaviourReader.ReadHeader(r);
            return enabled && assets.Resolve(behaviour.File, scriptPtr) is { } scriptRef
                ? assets.Read(scriptRef, Unity.Managed.MonoBehaviourReader.ReadMonoScript).ClassName
                : null;
        }

        // The night rig is stored switched off; a script on the rigs' parent switches it on when the scene
        // wakes (static analysis: EnableObjectsOnAwake.Awake activates every object in toEnable).
        var enabledOnAwake = new HashSet<long>();
        void FindEnablers(AssetRef transformRef)
        {
            var t = assets.Read(transformRef, TransformData.Read);
            var go = assets.Read(scene, t.GameObject, GameObjectData.Read);
            if (go is null || !go.IsActive)
                return;
            foreach (var c in go.Components)
            {
                if (assets.Resolve(scene, c) is not { ClassId: UnityClassId.MonoBehaviour } script
                    || ScriptOf(script, out var r) != "EnableObjectsOnAwake")
                    continue;
                var count = r.ReadInt32();
                for (var i = 0; i < count; i++)
                    if (PPtr.Read(r) is { FileId: 0, IsNull: false } target)
                        enabledOnAwake.Add(target.PathId);
            }
            foreach (var child in t.Children)
                if (assets.Resolve(scene, child) is { } childRef)
                    FindEnablers(childRef);
        }
        FindEnablers(root);

        void Walk(AssetRef transformRef, Quaternion parent, string parentPath, (bool Night, Quaternion Rotation)? rig)
        {
            var t = assets.Read(transformRef, TransformData.Read);
            var go = assets.Read(scene, t.GameObject, GameObjectData.Read);
            if (go is null || !go.IsActive && !(t.GameObject.FileId == 0 && enabledOnAwake.Contains(t.GameObject.PathId)))
                return;
            var rotation = parent * t.LocalRotation;
            var path = parentPath.Length == 0 ? go.Name : parentPath + "/" + go.Name;
            foreach (var c in go.Components)
            {
                if (assets.Resolve(scene, c) is { ClassId: UnityClassId.MonoBehaviour } script && ScriptOf(script, out var r) == ScriptClass)
                    rig = (r.ReadBool(), rotation); // isNightLight
            }
            if (rig is { } owner)
            {
                foreach (var c in go.Components)
                {
                    if (assets.Resolve(scene, c) is not { ClassId: UnityClassId.Light } light)
                        continue;
                    // Light fields in stored order: game object, enabled, type, shape, colour, intensity.
                    var lr = assets.Reader(light);
                    lr.Skip(12);
                    var lightEnabled = lr.ReadBool();
                    lr.Align();
                    var type = lr.ReadInt32();
                    lr.ReadInt32();
                    var color = lr.ReadVector4();
                    var intensity = lr.ReadSingle();
                    if (lightEnabled)
                        result.Add(new TimeOfDayLight(path, owner.Night, owner.Rotation,
                            Quaternion.Inverse(owner.Rotation) * rotation, type, color, intensity));
                }
            }
            foreach (var child in t.Children)
                if (assets.Resolve(scene, child) is { } childRef)
                    Walk(childRef, rotation, path, rig);
        }
        Walk(root, Quaternion.Identity, "", null);
        return result;
    }
}
