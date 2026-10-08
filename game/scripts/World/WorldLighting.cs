using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Scene;
using N = System.Numerics;

namespace OpenRanch.Game.World;

/// <summary>
/// Sky, ambient light, fog and sun as the original sets them for the zone, the hour and whether the
/// camera is inside a cave (docs/behavior/day-and-night.md). Falls back to the scene's stored values
/// when the zone settings can't be read.
/// </summary>
public partial class WorldLighting : Node
{
    private static readonly Shader FogShader = GD.Load<Shader>("res://shaders/sr_fog.gdshader");

    // How long the lighting takes to change on entering or leaving a cave, in seconds.
    private const float CaveTransitionSeconds = 1f;

    private readonly SceneLighting _scene;
    private readonly IReadOnlyDictionary<int, ZoneAmbience> _zones;
    private readonly IReadOnlyList<CaveVolume> _caves;
    private readonly float _hour;
    private readonly ProceduralSkyMaterial _sky = new() { SunAngleMax = 0 };
    private readonly Godot.Environment _environment;
    private readonly DirectionalLight3D _sun;
    private readonly ShaderMaterial _fog = new() { Shader = FogShader };
    private Camera3D? _camera;
    private float _caveDarkness;
    private int _caveZone = -1;

    public WorldLighting(SceneLighting scene, IReadOnlyList<ZoneAmbience> zones, IReadOnlyList<CaveVolume> caves, float hour)
    {
        Name = "Lighting";
        _scene = scene;
        _zones = zones.GroupBy(z => z.Zone).ToDictionary(g => g.Key, g => g.First());
        _caves = caves.Where(c => c.AffectsLighting).ToList();
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
        };
        AddChild(new WorldEnvironment { Environment = _environment });
        _sun = new DirectionalLight3D
        {
            LightEnergy = 1,
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 150,
        };
        AddChild(_sun);
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
        _caveZone = CaveZoneAt(camera.GlobalPosition);
        _caveDarkness = _caveZone >= 0 ? 1 : 0;
        Apply();
    }

    public override void _Process(double delta)
    {
        if (_camera is null)
            return;
        var zone = CaveZoneAt(_camera.GlobalPosition);
        if (zone >= 0)
            _caveZone = zone;
        var target = zone >= 0 ? 1f : 0f;
        if (_caveDarkness == target)
            return;
        var step = (float)delta / CaveTransitionSeconds;
        _caveDarkness = target > _caveDarkness ? Math.Min(target, _caveDarkness + step) : Math.Max(target, _caveDarkness - step);
        Apply();
    }

    // The highest-numbered cave zone whose volume holds the point, or -1 outside every cave.
    private int CaveZoneAt(Vector3 godotPosition)
    {
        var p = new N.Vector3(godotPosition.X, godotPosition.Y, -godotPosition.Z);
        var zone = -1;
        foreach (var cave in _caves)
            if (cave.Zone > zone && cave.Contains(p))
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

        // The sun turns about its own X axis through the day (at noon it points as stored) and is dimmed
        // inside caves.
        var day = outside?.Day ?? DayCycle.At(12);
        var strength = day.SunStrength * (1 - _caveDarkness);
        _sun.Visible = strength > 0;
        var turned = _scene.SunRotation * N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, day.TurnDegrees * MathF.PI / 180f);
        _sun.Basis = UnityConvert.Transform(N.Matrix4x4.CreateFromQuaternion(turned)).Basis;
        _sun.LightColor = UnityConvert.GammaMatchedLight(ambient, _scene.SunColor, _scene.SunIntensity * strength);
    }
}
