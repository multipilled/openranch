using System;
using System.Collections.Generic;
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
///   --zone NAME                     which area to build (default zoneRANCH); other areas start at a teleporter
///   --spawn NAME                    with --zone: start at the zone's teleporter of this name (World/ZoneSpawn.cs)
///   --camera x,y,z,yaw,pitch        start position and view in the original game's coordinates
///   --save FILE                     open this save: an original v12 save or an openranch .ranch.json (plots,
///                                   corral slimes, money, ranch upgrades, time of day); read only
///   --m3-check                      with --save: check the ranch against the save reader, print a report, quit
///   --save-out FILE                 with --save: once the world has settled, write the live ranch to FILE
///                                   (.ranch.json), then quit (unless --m3-check runs); in play F5 saves
///   --screenshot FILE [--frames N]  save a screenshot after N frames (default 90), then quit
///   --collision-check               test that the area's ground can be stood on, print a report, quit
///   --no-slimes, --m2-check         milestone 2 options, see Slimes/M2World.cs
///   --slime-zoo [--zoo-focus ID]    every slime and largo in pens with its food; a check unless --screenshot (Slimes/SlimeZoo.cs)
///   --hour H, --day-speed X, --day-check   the world clock, see World/WorldTime.cs
///   --sleep-check                   sleep through the ranch house's door and check the clock, see Home/RanchHouse.cs
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
        // Milestone 3: a save's plots stand on their sites (game/scripts/SaveLoad).
        var saved = Arg("--save") is { } savePath ? SaveLoad.SavedRanch.Load(install, assets, savePath, zoneName) : null;
        // The world clock starts at the save's world time, or 9:00 on day 1, and runs (World/WorldTime.cs).
        var ranchState = saved?.Ranch ?? new OpenRanch.Ranch.RanchState();
        WorldTime.ApplyStartHour(ranchState, args);
        var state = (saved?.State ?? WorldState.NewGame) with { Hour = (float)ranchState.Clock.Hour, RunningClock = true };
        var layers = PhysicsLayers.Read(assets);
        var rig = PlayerRig.Read(assets, scene);
        var lighting = SceneLighting.Read(assets, scene);
        var ambience = ZoneAmbience.ReadAll(assets, install);
        var world = new WorldAssets(assets);
        ZoneExtract zone;
        WorldLighting worldLighting;
        TimedObjects timed;
        // Milestone 4: without --zone, every zone of the world scene, its cells loaded around the player (World/WorldMap.cs).
        // --m3-check checks The Ranch alone against the save (every plot on built ground), so it keeps the single zone.
        WorldMap? map = null;
        var savedPlots = new Dictionary<string, IReadOnlyList<OpenRanch.Ranch.PlacedPlot>>();
        if (Arg("--zone") is null && Array.IndexOf(args, "--m3-check") < 0)
        {
            worldLighting = new WorldLighting(lighting, ambience, [], [], state.Hour, TimeOfDayLight.Read(assets, scene));
            timed = TimedObjects.Empty(state.Hour);
            map = WorldMap.Create(install, assets, scene, state, name => SavedPlots(install, assets, saved, Arg("--save"), state, name, savedPlots),
                layers, world, worldLighting, timed);
            map.LoadAll = Array.IndexOf(args, "--load-all") >= 0 || Array.IndexOf(args, "--collision-check") >= 0;
            AddChild(worldLighting);
            AddChild(timed);
            AddChild(map);
            zone = map.Merged(zoneName);
            GD.Print($"World: read {map.ZonesOf(map.Sets.Home).Count()} HOME zones and the rest in {map.ReadMilliseconds} ms");
        }
        else
        {
            zone = ZoneExtractor.Extract(assets, scene, zoneName, state);
            if (saved is not null)
                zone = saved.Apply(zone);
            var read = clock.ElapsedMilliseconds;

            var built = ZoneBuilder.Build(zone, world, layers);
            AddChild(built.Root);
            GD.Print($"{zoneName}: read in {read} ms, built in {clock.ElapsedMilliseconds - read} ms: " +
                     $"{built.MeshInstances} meshes, {built.MultiMeshes} multimeshes, {built.Instances} instances, " +
                     $"{world.MaterialCount} materials, {world.TextureCount} textures, {built.Shapes} collision shapes");

            worldLighting = new WorldLighting(lighting, ambience, zone.Caves, zone.Lights, state.Hour, TimeOfDayLight.Read(assets, scene), zone.Cells);
            AddChild(worldLighting);
            timed = TimedObjects.Build(zone, world, layers, worldLighting, state.Hour);
            AddChild(timed);
        }

        var player = new PlayerController { Name = "Player" };
        AddChild(player);
        player.Configure(rig.Height, rig.Radius, rig.SlopeLimitDegrees, rig.EyeHeight);
        player.Position = UnityConvert.Position(rig.Spawn);
        if (map is not null)
        {
            WorldStart.Place(map, player, rig, saved?.Ranch.Player, Arg("--spawn"), Arg("--camera"));
            GD.Print($"World: built {map.CellsBuilt} cells around the player in {map.BuildMilliseconds} ms, {world.MaterialCount} materials, " +
                     $"{world.TextureCount} textures; {clock.ElapsedMilliseconds} ms since start");
        }
        // Milestone 4: other areas start where a teleporter puts the player (World/ZoneSpawn.cs).
        var otherZone = zoneName != "zoneRANCH";
        if (otherZone && ZoneSpawn.Pick(zone, Arg("--spawn")) is { } spawn)
        {
            player.Position = UnityConvert.Position(spawn.Position);
            player.Look(-spawn.YawDegrees, 0);
            GD.Print($"{zoneName}: player starts at {spawn.Point.Name} ({spawn.Position.X:F1}, {spawn.Position.Y:F1}, {spawn.Position.Z:F1})");
        }

        if (Array.IndexOf(args, "--collision-check") >= 0)
        {
            _collisionCheck = new CollisionCheck(this, player, map is null ? zone : map.MergedSet(map.CurrentSet), rig, layers);
            return;
        }

        // Milestone 3: the player stands where the save left them, looking the same way (SaveLoad/SavedRanch.cs).
        if (saved?.Player is { } start)
        {
            player.Position = UnityConvert.Position(start.Position);
            // Unity yaw turns clockwise seen from above and positive pitch looks down; Godot is the reverse.
            player.Look(-start.Yaw, -start.Pitch);
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
        if (map is not null)
            AddChild(new WorldCheck(map, player, worldLighting, args, saved?.Ranch, savedPlots));
        if (otherZone || map is not null)
            GD.Print($"{zoneName}: ambience zone {worldLighting.OutsideZone} outside caves at the start");

        // Milestone 2: slimes, food, vacpack, corral walls and the plort market (game/scripts/Slimes).
        // With a save, its money and corral slimes take the place of the demo slimes.
        // Milestone 5's slime zoo (Slimes/SlimeZoo.cs) replaces the demo slimes too.
        var zoo = Array.IndexOf(args, "--slime-zoo") >= 0;
        var m2Args = saved is null && !otherZone && !zoo ? args : args.Append("--no-slimes").ToArray();
        var m2 = Slimes.M2World.Create(install, zone, layers, player, state.Hour, m2Args);
        if (m2 is not null)
        {
            AddChild(m2);
            map?.Manage(m2.Catalog.Actors);
            if (zoo)
                AddChild(new Slimes.SlimeZoo(m2, player, args));
            saved?.Populate(m2);
            // Milestone 3: the live ranch saves back to openranch's own format (SaveLoad/SaveWriter.cs).
            if (saved is not null)
                AddChild(new SaveLoad.SaveWriter(saved, m2, install, Arg("--save-out"), quitAfterWrite: Array.IndexOf(args, "--m3-check") < 0, player));
            if (saved is not null && Array.IndexOf(args, "--m3-check") >= 0)
                AddChild(new SaveLoad.M3Check(saved, zone, m2, install, player));
        }
        var worldTime = WorldTime.Create(install, m2?.Scripts, ranchState, worldLighting, timed,
            ambience.FirstOrDefault(a => a.Zone == ZoneAmbience.DefaultZone), saved is not null, args);
        AddChild(worldTime);
        // One clock: milestone 2's game time (hunger, plorts, market days) follows the world clock.
        m2?.Follow(worldTime);
        // The ranch house's door: sleeping until morning (Home/RanchHouse.cs).
        if (m2 is not null && Home.RanchHouse.Create(m2.Scripts, zoneName, player, worldTime, m2, args) is { } house)
            AddChild(house);
    }

    // Milestone 4: the save's plots on another zone's sites (SaveLoad/SavedRanch.cs reads one zone's sites at a time).
    private static (WorldState, Func<ZoneExtract, ZoneExtract>)? SavedPlots(GameInstall install, AssetSet assets, SaveLoad.SavedRanch? ranch,
        string? savePath, WorldState state, string zoneName, Dictionary<string, IReadOnlyList<OpenRanch.Ranch.PlacedPlot>> placed)
    {
        if (savePath is null)
            return null;
        var saved = zoneName == "zoneRANCH" && ranch is not null ? ranch : SaveLoad.SavedRanch.Load(install, assets, savePath, zoneName);
        placed[zoneName] = saved.Plots;
        GD.Print($"World: {zoneName} has {saved.Plots.Count} of the save's plots");
        return (state with { Hidden = saved.State.Hidden }, saved.Apply);
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
