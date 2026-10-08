using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Scene;
using OpenRanch.Formats.Unity;
using OpenRanch.Game.Player;
using OpenRanch.Game.World;

namespace OpenRanch.Game;

/// <summary>
/// Milestone 1: The Ranch, built at startup from the player's own copy of Slime Rancher, with a
/// first-person controller. Command-line options after "--":
///   --game DIR                      the Slime Rancher folder, if it isn't found automatically
///   --zone NAME                     which area to build (default zoneRANCH)
///   --camera x,y,z,yaw,pitch        start position and view in the original game's coordinates
///   --screenshot FILE [--frames N]  save a screenshot after N frames (default 90), then quit
///   --collision-check               test that the area's ground can be stood on, print a report, quit
/// </summary>
public partial class Ranch : Node3D
{
    private string? _screenshot;
    private int _framesLeft;
    private CollisionCheck? _collisionCheck;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string? Arg(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        _screenshot = Arg("--screenshot");
        _framesLeft = int.TryParse(Arg("--frames"), out var f) ? f : 90;

        var install = Arg("--game") is { } dir ? GameInstall.Open(dir) : GameInstall.Find();
        if (install is null)
        {
            ShowMessage($"Slime Rancher wasn't found. Install it from Steam or set {GameInstall.GameDirVariable}.");
            return;
        }

        var clock = Stopwatch.StartNew();
        var assets = new AssetSet(install);
        var scene = assets.File("level3")!;
        var zoneName = Arg("--zone") ?? "zoneRANCH";
        var zone = ZoneExtractor.Extract(assets, scene, zoneName);
        var layers = PhysicsLayers.Read(assets);
        var rig = PlayerRig.Read(assets, scene);
        var read = clock.ElapsedMilliseconds;

        var world = new WorldAssets(assets);
        var built = ZoneBuilder.Build(zone, world, layers);
        AddChild(built.Root);
        GD.Print($"{zoneName}: read in {read} ms, built in {clock.ElapsedMilliseconds - read} ms: " +
                 $"{built.MeshInstances} meshes, {built.MultiMeshes} multimeshes, {built.Instances} instances, " +
                 $"{world.MaterialCount} materials, {world.TextureCount} textures, {built.Shapes} collision shapes");

        AddEnvironment();

        var player = new PlayerController { Name = "Player" };
        AddChild(player);
        player.Configure(rig.Height, rig.Radius, rig.SlopeLimitDegrees, rig.EyeHeight);
        player.Position = UnityConvert.Position(rig.Spawn);

        if (Array.IndexOf(args, "--collision-check") >= 0)
        {
            _collisionCheck = new CollisionCheck(this, player, zone, rig);
            return;
        }

        if (Arg("--camera") is { } camera)
        {
            var v = camera.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            player.Position = UnityConvert.Position(new System.Numerics.Vector3(v[0], v[1], v[2]));
            // Unity yaw turns clockwise seen from above and positive pitch looks down; Godot is the reverse.
            player.Look(v.Length > 3 ? -v[3] : 0, v.Length > 4 ? -v[4] : 0);
            if (_screenshot is not null)
                player.SetPhysicsProcess(false); // hold the exact view for the capture
        }
    }

    private void AddEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.33f, 0.58f, 0.92f),
            SkyHorizonColor = new Color(0.72f, 0.84f, 0.95f),
            GroundHorizonColor = new Color(0.72f, 0.84f, 0.95f),
            GroundBottomColor = new Color(0.35f, 0.4f, 0.45f),
            SunAngleMax = 20,
        };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1,
            AmbientLightEnergy = 1.1f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightColor = new Color(0.7f, 0.82f, 0.95f),
            FogDensity = 0.0004f,
            SsaoEnabled = true,
        };
        AddChild(new WorldEnvironment { Environment = env });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-50, -35, 0),
            LightEnergy = 1.15f,
            LightColor = new Color(1f, 0.96f, 0.88f),
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 250,
        });
    }

    private void ShowMessage(string text)
    {
        GD.PrintErr(text);
        var layer = new CanvasLayer();
        layer.AddChild(new Label { Text = text, Position = new Vector2(48, 48) });
        AddChild(layer);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_collisionCheck is not null && _collisionCheck.Step())
        {
            GD.Print(_collisionCheck.Report);
            GetTree().Quit(_collisionCheck.Passed ? 0 : 1);
            _collisionCheck = null;
        }
    }

    public override void _Process(double delta)
    {
        if (_screenshot is null || --_framesLeft > 0)
            return;
        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(_screenshot);
        GD.Print(error == Error.Ok ? $"Saved {_screenshot}" : $"Could not save {_screenshot}: {error}");
        _screenshot = null;
        GetTree().Quit();
    }
}
