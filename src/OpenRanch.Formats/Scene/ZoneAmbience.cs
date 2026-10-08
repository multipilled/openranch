using System.Numerics;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Formats.Scene;

/// <summary>
/// One zone's fog, ambient light and sky colours for day and night, from the game's ambience
/// settings assets. See docs/behavior/day-and-night.md for how they blend through the day.
/// </summary>
public sealed record ZoneAmbience(
    int Zone,
    Vector4 DayFogColor, float DayFogDensity, Vector4 DayAmbient,
    Vector4 NightFogColor, float NightFogDensity, Vector4 NightAmbient,
    Vector4 DaySky, Vector4 DayHorizon, Vector4 NightSky, Vector4 NightHorizon)
{
    /// <summary>The zone used where a cell names no other (The Ranch, among others).</summary>
    public const int DefaultZone = 0;

    public const string ScriptClass = "AmbianceDirectorZoneSetting";

    /// <summary>Reads every zone's settings from the game's asset files.</summary>
    public static IReadOnlyList<ZoneAmbience> ReadAll(AssetSet assets, GameInstall install)
    {
        var result = new List<ZoneAmbience>();
        foreach (var path in AssetCensus.SerializedFilePaths(install.DataDirectory))
        {
            var name = Path.GetFileName(path);
            if (!name.StartsWith("sharedassets", StringComparison.Ordinal) && name != "resources.assets")
                continue;
            var file = assets.File(name)!;
            foreach (var info in file.Objects.Where(o => o.ClassId == UnityClassId.MonoBehaviour))
            {
                var asset = new AssetRef(file, info);
                var r = assets.Reader(asset);
                var (_, _, script, _) = MonoBehaviourReader.ReadHeader(r);
                if (assets.Resolve(file, script) is not { } scriptRef
                    || assets.Read(scriptRef, MonoBehaviourReader.ReadMonoScript).ClassName != ScriptClass)
                    continue;
                result.Add(new ZoneAmbience(r.ReadInt32(),
                    r.ReadVector4(), r.ReadSingle(), r.ReadVector4(),
                    r.ReadVector4(), r.ReadSingle(), r.ReadVector4(),
                    r.ReadVector4(), r.ReadVector4(), r.ReadVector4(), r.ReadVector4()));
            }
        }
        return result;
    }

    /// <summary>The blended values at an hour of the day (0 to 24).</summary>
    public AmbienceAt At(float hour)
    {
        var day = DayCycle.At(hour);
        var night = day.Night;
        var nightFog = MathF.Pow(night, 6);
        return new AmbienceAt(
            Vector4.Lerp(DayFogColor, NightFogColor, night),
            (1 - nightFog) * DayFogDensity + nightFog * NightFogDensity,
            Vector4.Lerp(DayAmbient, NightAmbient, night),
            Vector4.Lerp(DaySky, NightSky, night),
            Vector4.Lerp(DayHorizon, NightHorizon, night),
            day);
    }
}

public sealed record AmbienceAt(Vector4 FogColor, float FogDensity, Vector4 Ambient, Vector4 Sky, Vector4 Horizon, DayCycle Day);

/// <summary>
/// Where the day is at a given hour: how far into night the colours are, how strong the sun and the
/// night light are, and how far the sun and moon have turned. See docs/behavior/day-and-night.md.
/// </summary>
public readonly record struct DayCycle(float Night, float SunStrength, float MoonStrength, float TurnDegrees)
{
    // The sun and the night light each take 1/288 of a day (5 game minutes) to fade in after
    // 6:00 or 18:00 and to fade out before them.
    private const float FadeRate = 288f;

    public static DayCycle At(float hour)
    {
        var f = (hour % 24f + 24f) % 24f / 24f;
        var night = Math.Clamp((MathF.Abs(0.5f - f) - 0.15f) * 5f, 0f, 1f);
        float sun = 0, moon = 0;
        if (f < 0.25f)
            moon = Math.Clamp((0.25f - f) * FadeRate, 0.01f, 1f);
        else if (f > 0.75f)
            moon = Math.Clamp((f - 0.75f) * FadeRate, 0.01f, 1f);
        else
            sun = Math.Clamp(MathF.Min(f - 0.25f, 0.75f - f) * FadeRate, 0.01f, 1f);
        return new DayCycle(night, sun, moon, 360f * (f - 0.5f));
    }
}
