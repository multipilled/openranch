using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Saves;
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
///   --save FILE                     show the world as in this save (ranch upgrades, time of day); read only
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
        var state = Arg("--save") is { } savePath ? WorldStateFromSave(savePath) : WorldState.NewGame;
        var zone = ZoneExtractor.Extract(assets, scene, zoneName, state);
        var layers = PhysicsLayers.Read(assets);
        var rig = PlayerRig.Read(assets, scene);
        var lighting = SceneLighting.Read(assets, scene);
        var ambience = ZoneAmbience.ReadAll(assets, install);
        var read = clock.ElapsedMilliseconds;

        var world = new WorldAssets(assets);
        var built = ZoneBuilder.Build(zone, world, layers);
        AddChild(built.Root);
        GD.Print($"{zoneName}: read in {read} ms, built in {clock.ElapsedMilliseconds - read} ms: " +
                 $"{built.MeshInstances} meshes, {built.MultiMeshes} multimeshes, {built.Instances} instances, " +
                 $"{world.MaterialCount} materials, {world.TextureCount} textures, {built.Shapes} collision shapes");

        var worldLighting = new WorldLighting(lighting, ambience, zone.Caves, zone.Lights, state.Hour);
        AddChild(worldLighting);

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
        worldLighting.Attach(player.Camera);
    }

    // Reads the progress counters and the hour from a save. The file is only read.
    private static WorldState WorldStateFromSave(string path)
    {
        var game = SaveFile.Read(new System.IO.MemoryStream(System.IO.File.ReadAllBytes(path)));
        var progress = game.Block("player").Map("progress")
            .ToDictionary(kv => System.Convert.ToInt32(kv.Key), kv => System.Convert.ToInt32(kv.Value));
        var hour = (float)(game.Block("world").Get<double>("worldTime") % 86400.0 / 3600.0);
        return new WorldState(progress, hour);
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
