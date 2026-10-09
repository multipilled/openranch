using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Scene;
using N = System.Numerics;

namespace OpenRanch.Game.World;

/// <summary>
/// Sky, ambient light, fog, sun, night light and the scene's own lamps as the original sets them for
/// the zone, the hour and whether the camera is inside a cave (docs/behavior/day-and-night.md). Falls
/// back to the scene's stored values when the zone settings can't be read. The world clock
/// (<see cref="WorldTime"/>) moves the hour with <see cref="SetHour"/>.
/// </summary>
public partial class WorldLighting : Node
{
    private static readonly Shader FogShader = GD.Load<Shader>("res://shaders/sr_fog.gdshader");

    /// <summary>
    /// How long the lighting takes to change on entering or leaving a cave, in seconds. The world clock
    /// sets it from the install (AmbianceDirector's zoneSettingTransitionTime); 1 until then.
    /// </summary>
    public float TransitionSeconds { get; set; } = 1f;

    private readonly SceneLighting _scene;
    private readonly IReadOnlyDictionary<int, ZoneAmbience> _zones;
    private readonly IReadOnlyList<CaveVolume> _caves;
    private float _hour;
    private readonly ProceduralSkyMaterial _sky = new() { SunAngleMax = 0 };
    private readonly Godot.Environment _environment;
    // The sun and the night light: each rig light with its Godot light.
    private readonly List<(TimeOfDayLight Rig, DirectionalLight3D Light)> _rigLights = new();
    private readonly ShaderMaterial _fog = new() { Shader = FogShader };
    // How far the camera has gone into each cave trigger (0 outside, 1 after a second inside), by trigger.
    private readonly Dictionary<string, float> _caveAmount = new();
    // Lamps a cave trigger switches on, with their stored energy, by controller id.
    private readonly List<(Light3D Light, float Energy, long Controller)> _caveLights = new();
    private Camera3D? _camera;
    private float _caveDarkness;
    private int _caveZone = -1;

    public WorldLighting(SceneLighting scene, IReadOnlyList<ZoneAmbience> zones, IReadOnlyList<CaveVolume> caves,
        IReadOnlyList<LightItem> lights, float hour, IReadOnlyList<TimeOfDayLight>? rigLights = null)
    {
        Name = "Lighting";
        _scene = scene;
        _zones = zones.GroupBy(z => z.Zone).ToDictionary(g => g.Key, g => g.First());
        _caves = caves;
        _hour = hour;
        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightEnergy = scene.AmbientIntensity,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            // The original has no tone mapping: colours go to the screen as lit.
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            SsaoEnabled = true,
            SsaoIntensity = 1,
        };
        AddChild(new WorldEnvironment { Environment = _environment });
        // Without the rigs, the scene's main day light stands in for the sun.
        if (rigLights is not { Count: > 0 })
            rigLights = [new TimeOfDayLight("Light - Main", false, scene.SunRotation, N.Quaternion.Identity,
                TimeOfDayLight.Directional, scene.SunColor, scene.SunIntensity)];
        foreach (var rig in rigLights.Where(r => r.Type == TimeOfDayLight.Directional))
        {
            var light = new DirectionalLight3D
            {
                Name = rig.IsNight ? "NightLight" : "Sun",
                LightEnergy = 1,
                SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
                ShadowEnabled = true,
                DirectionalShadowMaxDistance = 150,
            };
            AddChild(light);
            _rigLights.Add((rig, light));
        }
        foreach (var item in lights)
            AddLamp(item);
    }

    /// <summary>The hour of the day the lighting shows (0 to 24).</summary>
    public float Hour => _hour;

    /// <summary>Moves the lighting to another hour of the day (0 to 24).</summary>
    public void SetHour(float hour)
    {
        if (hour == _hour)
            return;
        _hour = hour;
        Apply();
    }

    /// <summary>
    /// A point or spot light from the scene, added under <paramref name="parent"/> (this node if null).
    /// Unity's spot angle is the whole cone; Godot's is half of it.
    /// </summary>
    public Light3D AddLamp(LightItem item, Node? parent = null)
    {
        Light3D light = item.Type == 0
            ? new SpotLight3D { SpotRange = item.Range, SpotAngle = Math.Clamp(item.SpotAngle / 2, 1, 89) }
            : new OmniLight3D { OmniRange = item.Range };
        light.Name = item.Path.Split('/').Last();
        light.Transform = UnityConvert.Transform(item.World);
        light.LightColor = UnityConvert.Color(item.Color) with { A = 1 };
        // Unity's lamps fade as 1 / (1 + 25 (d / range)^2). Godot's fall off as d^-decay inside the range,
        // so pick the decay and an energy scale that agree with Unity at 20% and 50% of the range.
        const float decay = 1.4f;
        var energy = item.Intensity * 0.5f * MathF.Pow(0.2f * item.Range, decay);
        light.SetParam(Light3D.Param.Attenuation, decay);
        light.LightEnergy = energy;
        light.ShadowEnabled = false;
        (parent ?? this).AddChild(light);
        if (item.CaveController is { } controller)
        {
            light.Visible = false;
            _caveLights.Add((light, energy, controller));
        }
        return light;
    }

    /// <summary>Draws the fog from this camera and starts the lighting where the camera is.</summary>
    public void Attach(Camera3D camera)
    {
        _camera = camera;
        if (_scene.FogEnabled)
        {
            camera.AddChild(new MeshInstance3D
            {
                Name = "Fog",
                Mesh = new QuadMesh { Size = Vector2.One },
                MaterialOverride = _fog,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                ExtraCullMargin = 16384,
            });
        }
        foreach (var cave in _caves)
            if (cave.Contains(UnityPosition(camera.GlobalPosition)))
                _caveAmount[cave.Path] = 1;
        _caveZone = CaveZoneAt(camera.GlobalPosition);
        _caveDarkness = _caveZone >= 0 ? 1 : 0;
        Apply();
    }

    public override void _Process(double delta)
    {
        if (_camera is null)
            return;
        var step = (float)delta / TransitionSeconds;
        var changed = false;
        var p = UnityPosition(_camera.GlobalPosition);
        foreach (var cave in _caves.GroupBy(c => c.Path))
        {
            var inside = cave.Any(c => c.Contains(p));
            var amount = _caveAmount.GetValueOrDefault(cave.Key);
            var next = inside ? Math.Min(1, amount + step) : Math.Max(0, amount - step);
            if (next != amount)
            {
                _caveAmount[cave.Key] = next;
                changed = true;
            }
        }

        var zone = CaveZoneAt(_camera.GlobalPosition);
        if (zone >= 0)
            _caveZone = zone;
        var target = zone >= 0 ? 1f : 0f;
        if (_caveDarkness != target)
        {
            _caveDarkness = target > _caveDarkness ? Math.Min(target, _caveDarkness + step) : Math.Max(target, _caveDarkness - step);
            changed = true;
        }
        if (changed)
            Apply();
    }

    /// <summary>
    /// A rig light's intensity: its stored intensity times the sun's or night light's strength, less the
    /// cave darkness, kept between 0.001 and 1 while the rig shines at all (static analysis:
    /// AmbianceDirector.UpdateLightIntensity).
    /// </summary>
    public static float RigIntensity(TimeOfDayLight rig, DayCycle day, float caveDarkness)
    {
        var strength = (rig.IsNight ? day.MoonStrength : day.SunStrength) * (1 - caveDarkness);
        return Math.Clamp(rig.Intensity * strength, strength == 0 ? 0 : 0.001f, 1);
    }

    /// <summary>What the lighting is set to now, read back from the Godot objects (for --day-check).</summary>
    public LightingNow Now() => new(
        _environment.AmbientLightColor,
        (Color)_fog.GetShaderParameter("fog_color"),
        (float)_fog.GetShaderParameter("density"),
        _sky.SkyTopColor,
        _sky.SkyHorizonColor,
        _caveDarkness,
        _rigLights.Select(r => (r.Rig, r.Light.Visible, r.Light.LightColor, r.Light.Basis)).ToList());

    private static N.Vector3 UnityPosition(Vector3 godot) => new(godot.X, godot.Y, -godot.Z);

    // The highest-numbered cave zone whose lighting volume holds the point, or -1 outside every cave.
    private int CaveZoneAt(Vector3 godotPosition)
    {
        var p = UnityPosition(godotPosition);
        var zone = -1;
        foreach (var cave in _caves)
            if (cave.AffectsLighting && cave.Zone > zone && cave.Contains(p))
                zone = cave.Zone;
        return zone;
    }

    private AmbienceAt? Ambience(int zone) => _zones.TryGetValue(zone, out var z) ? z.At(_hour) : null;

    private void Apply()
    {
        var outside = Ambience(ZoneAmbience.DefaultZone);
        var cave = _caveZone >= 0 ? Ambience(_caveZone) : null;
        var t = cave is null ? 0 : _caveDarkness;
        N.Vector4 Mix(N.Vector4 a, N.Vector4? b) => b is { } v ? N.Vector4.Lerp(a, v, t) : a;
        float MixF(float a, float? b) => b is { } v ? a + (v - a) * t : a;

        var fogColor = Mix(outside?.FogColor ?? _scene.FogColor, cave?.FogColor);
        var fogDensity = MixF(outside?.FogDensity ?? _scene.FogDensity, cave?.FogDensity);
        var ambient = Mix(outside?.Ambient ?? _scene.AmbientColor, cave?.Ambient);
        var top = UnityConvert.Color(outside?.Sky ?? _scene.SkyColor ?? new N.Vector4(0.33f, 0.58f, 0.92f, 1));
        var horizon = UnityConvert.Color(outside?.Horizon ?? _scene.HorizonColor ?? _scene.FogColor);

        _sky.SkyTopColor = top;
        _sky.SkyHorizonColor = horizon;
        _sky.GroundHorizonColor = horizon;
        _sky.GroundBottomColor = top.Darkened(0.4f);
        _environment.AmbientLightColor = UnityConvert.Color(ambient) with { A = 1 };
        _fog.SetShaderParameter("fog_color", UnityConvert.Color(fogColor) with { A = 1 });
        _fog.SetShaderParameter("density", fogDensity);

        // The sun and night light rigs turn about their own X axis through the day (at noon they point as
        // stored); each light shines at its stored intensity times the rig's strength, dimmed inside caves.
        var day = outside?.Day ?? DayCycle.At(_hour);
        foreach (var (rig, light) in _rigLights)
        {
            var intensity = RigIntensity(rig, day, _caveDarkness);
            light.Visible = intensity > 0;
            light.Basis = UnityConvert.Transform(N.Matrix4x4.CreateFromQuaternion(rig.Rotation(day.TurnDegrees))).Basis;
            light.LightColor = UnityConvert.GammaMatchedLight(ambient, rig.Color, intensity);
        }

        // Cave lamps shine in proportion to how far the camera is into the caves that list them.
        foreach (var (light, energy, controller) in _caveLights)
        {
            var amount = _caves.Where(c => c.LightControllers.Contains(controller))
                .Select(c => _caveAmount.GetValueOrDefault(c.Path)).DefaultIfEmpty(0).Max();
            light.Visible = amount > 0;
            light.LightEnergy = energy * amount;
        }
    }
}

/// <summary>The lighting's current settings, as <see cref="WorldLighting.Now"/> reads them back.</summary>
public sealed record LightingNow(Color Ambient, Color FogColor, float FogDensity, Color SkyTop, Color SkyHorizon, float CaveDarkness,
    IReadOnlyList<(TimeOfDayLight Rig, bool Visible, Color Color, Basis Basis)> Rigs);
