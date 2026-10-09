using System;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Scene;
using OpenRanch.Game.Player;
using OpenRanch.Game.World;
using OpenRanch.Ranch;

namespace OpenRanch.Game.RanchEconomy;

/// <summary>
/// Milestone 6, the ranch economy, on a ranch opened from a save or started new (--save, --new-game):
/// the plots in play (<see cref="RanchPlots"/>, <see cref="PlotMenu"/>), the expansions
/// (<see cref="ExpansionShop"/>, <see cref="DoorMenu"/>), using things the player looks at
/// (<see cref="Interactor"/>) and the ranch house screen, which saves when the player wakes. Command-line
/// options after "--":
///   --new-game                 start a new game instead of opening a save (SaveLoad/SavedRanch.cs)
///   --money N                  with --new-game: start with N newbucks instead of the game mode's (testing)
///   --sleep-save FILE          where saving after sleeping and after buying an expansion writes (default:
///                              openranch's user folder, as F5 does)
///   --m6-check                 run <see cref="M6Check"/>: build, upgrade, buy an expansion, grow produce,
///                              sleep and save; print a report and quit
/// </summary>
public partial class Economy : Node3D
{
    private readonly SaveLoad.SaveWriter _writer;
    private readonly string? _savePath;

    public Economy(SaveLoad.SavedRanch saved, ZoneExtract zone, Slimes.M2World m2, PhysicsLayers layers, PlayerController player,
        WorldTime clock, Home.RanchHouse? house, SaveLoad.SaveWriter writer, string[] args)
    {
        Name = "Economy";
        _writer = writer;
        string? Arg(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        _savePath = Arg("--sleep-save");
        Saved = saved;
        M2 = m2;
        Clock = clock;
        House = house;
        Player = player;
        Names = new GameEnums(m2.Scripts.Types);
        Plots = new RanchPlots(saved, zone, m2, layers, Names);
        Shop = new ExpansionShop(saved, zone, m2, layers, Names, Plots.Purse);
        PlotMenu = new PlotMenu(Plots);
        DoorMenu = new DoorMenu(Shop);
        // How far the player reaches: the rig's UIDetector (static analysis: a ray from the middle of the view, this long).
        var reach = m2.Scripts.OfClass("UIDetector").Select(d => d.Data.Data?["interactDistance"]).OfType<float>().FirstOrDefault(d => d > 0);
        Interactor = new Interactor(player, reach, PlotMenu, DoorMenu, Shop);
        AddChild(Plots);
        AddChild(Shop);
        AddChild(PlotMenu);
        AddChild(DoorMenu);
        AddChild(Interactor);
        // Produce, crops, hens and chicks on the world clock; the save writer writes their clocks.
        try
        {
            Produce = new RanchProduce(this);
            AddChild(Produce);
            writer.Timers = Produce.TimersOf;
            writer.Unsaved = Produce.Unsaved;
            writer.Crops = Produce.CropClocks;
            // Silos, feeders, plort collectors and the incinerator (RanchMachines.cs).
            Machines = new RanchMachines(this);
            AddChild(Machines);
        }
        catch (Exception e) when (Array.IndexOf(args, "--m6-check") >= 0)
        {
            // A check that can't start would leave the game idling until the runner's timeout: quit instead.
            GD.PrintErr($"m6-check could not start: {e}");
            Callable.From(() => GetTree().Quit(1)).CallDeferred();
            throw;
        }

        // The live plots are what the checks and the writer see.
        saved.LiveRenderers = () => Plots.Renderers;
        // The original saves right after an expansion is bought and when the player wakes up.
        Shop.Bought += _ => Save();
        if (house is not null)
        {
            HouseScreen = new RanchHouseScreen(house, () => Save());
            house.AddChild(HouseScreen);
            house.Screen = HouseScreen;
            house.Slept += (_, _) => Save();
        }
        if (Array.IndexOf(args, "--m6-check") >= 0)
            AddChild(new M6Check(this, args));
    }

    public SaveLoad.SavedRanch Saved { get; }
    public Slimes.M2World M2 { get; }
    public WorldTime Clock { get; }
    public Home.RanchHouse? House { get; }
    public PlayerController Player { get; }
    public GameEnums Names { get; }
    public RanchPlots Plots { get; }
    public ExpansionShop Shop { get; }
    public PlotMenu PlotMenu { get; }
    public DoorMenu DoorMenu { get; }
    public RanchHouseScreen? HouseScreen { get; }
    public Interactor Interactor { get; }
    public RanchProduce Produce { get; }
    public RanchMachines Machines { get; }

    /// <summary>Where <see cref="Save"/> writes.</summary>
    public string SavePath => _savePath ?? _writer.PlayPath;

    /// <summary>The number of saves written by <see cref="Save"/>.</summary>
    public int Saves { get; private set; }

    /// <summary>Writes the live ranch to <see cref="SavePath"/>.</summary>
    public bool Save()
    {
        var ok = _writer.Write(SavePath);
        if (ok)
            Saves++;
        return ok;
    }

    public override void _ExitTree() => Names.Dispose();
}
