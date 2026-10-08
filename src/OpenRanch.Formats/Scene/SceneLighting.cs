using System.Numerics;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Scene;

/// <summary>
/// The world scene's lighting setup: fog, ambient colour, the sky material's colours and the main
/// daytime light. The game animates these through the day; these are the scene's starting values.
/// </summary>
public sealed record SceneLighting(
    bool FogEnabled,
    Vector4 FogColor,
    int FogMode,
    float FogDensity,
    float FogStart,
    float FogEnd,
    Vector4 AmbientColor,
    float AmbientIntensity,
    Vector4? SkyColor,
    Vector4? HorizonColor,
    Quaternion SunRotation,
    Vector4 SunColor,
    float SunIntensity)
{
    public const int FogLinear = 1, FogExponential = 2, FogExponentialSquared = 3;

    public static SceneLighting Read(AssetSet assets, SerializedFile scene)
    {
        var settingsInfo = scene.Objects.First(o => o.ClassId == 104); // RenderSettings
        var settingsRef = new AssetRef(scene, settingsInfo);
        var r = assets.Reader(settingsRef);
        var fog = r.ReadBool();
        r.Align();
        var fogColor = r.ReadVector4();
        var fogMode = r.ReadInt32();
        var fogDensity = r.ReadSingle();
        var fogStart = r.ReadSingle();
        var fogEnd = r.ReadSingle();
        var ambientSky = r.ReadVector4();
        r.ReadVector4(); // equator
        r.ReadVector4(); // ground
        var ambientIntensity = r.ReadSingle();
        r.ReadInt32(); // ambient mode
        r.ReadVector4(); // subtractive shadow colour
        var skybox = PPtr.Read(r);

        Vector4? skyColor = null, horizon = null;
        if (assets.Read(scene, skybox, MaterialData.Read) is { } sky)
        {
            if (sky.Colors.TryGetValue("_SkyColor", out var s))
                skyColor = s;
            if (sky.Colors.TryGetValue("_HorizonColor", out var h))
                horizon = h;
        }

        var (sunRotation, sunColor, sunIntensity) = MainLight(assets, scene);
        return new SceneLighting(fog, fogColor, fogMode, fogDensity, fogStart, fogEnd, ambientSky, ambientIntensity,
            skyColor, horizon, sunRotation, sunColor, sunIntensity);
    }

    // The daytime key light is "Range Lights / Range Lights - Day / Light - Main".
    private static (Quaternion, Vector4, float) MainLight(AssetSet assets, SerializedFile scene)
    {
        var roots = ZoneExtractor.RootObjects(assets, scene);
        if (!roots.TryGetValue("Range Lights", out var root))
            return (Quaternion.Identity, Vector4.One, 1);

        (Quaternion, Vector4, float)? found = null;
        void Walk(AssetRef transformRef, Quaternion parent, bool underDay)
        {
            var t = assets.Read(transformRef, TransformData.Read);
            var go = assets.Read(scene, t.GameObject, GameObjectData.Read);
            if (go is null)
                return;
            // Row-vector matrices: a child's world rotation is its local rotation followed by the parent's.
            var rotation = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateFromQuaternion(t.LocalRotation) * Matrix4x4.CreateFromQuaternion(parent));
            var day = underDay || go.Name.EndsWith("Day", StringComparison.Ordinal);
            if (day && go.IsActive && go.Name == "Light - Main")
            {
                foreach (var c in go.Components)
                {
                    if (assets.Resolve(scene, c) is not { ClassId: UnityClassId.Light } light)
                        continue;
                    var lr = assets.Reader(light);
                    lr.Skip(12); // game object
                    lr.ReadBool();
                    lr.Align();
                    lr.ReadInt32(); // type
                    lr.ReadInt32(); // shape
                    var color = lr.ReadVector4();
                    var intensity = lr.ReadSingle();
                    found ??= (rotation, color, intensity);
                }
            }
            foreach (var child in t.Children)
                if (assets.Resolve(scene, child) is { } childRef)
                    Walk(childRef, rotation, day);
        }
        Walk(root, Quaternion.Identity, false);
        return found ?? (Quaternion.Identity, Vector4.One, 1);
    }
}
