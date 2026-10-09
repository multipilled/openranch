using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Scene;
using OpenRanch.Formats.Unity;
using OpenRanch.Game.World;
using OpenRanch.Ranch;

namespace OpenRanch.Game.RanchEconomy;

/// <summary>
/// The ranch expansions in play (docs/behavior/plots.md, "Ranch expansions"): every expansion barrier
/// still closed stands as its own node, and each door sold through a purchase screen gets an activator
/// at its <c>UIActivator</c>. Using a locked door opens <see cref="DoorMenu"/>; buying spends the price,
/// opens the door and recounts the progress counters (src/OpenRanch.Ranch/Expansions.cs), and every
/// barrier that is now open is lifted. Using a closed (unlocked) door opens it. The original saves the
/// game right after a purchase (static analysis: <c>AccessDoorUI</c> calls the auto-save director's
/// save-all), raised here as <see cref="Bought"/>.
/// </summary>
public partial class ExpansionShop : Node3D
{
    /// <summary>The meta key on a door's activator area holding its door id.</summary>
    public const string DoorMeta = "expansion_door";

    private readonly SaveLoad.SavedRanch _saved;
    private readonly ZoneExtract _zone;
    private readonly Slimes.M2World _m2;
    private readonly PhysicsLayers _layers;
    private readonly Dictionary<string, Node3D> _barrierNodes = new();

    public ExpansionShop(SaveLoad.SavedRanch saved, ZoneExtract zone, Slimes.M2World m2, PhysicsLayers layers, GameEnums names, IPurse purse)
    {
        Name = "Expansions";
        _saved = saved;
        _zone = zone;
        _m2 = m2;
        _layers = layers;
        Names = names;
        Purse = purse;
        var scene = m2.Scripts.Assets.File("level3") ?? throw new System.IO.IOException("level3 is missing.");
        Doors = Expansions.Read(m2.Scripts, scene);
        // The doors' purchase activators: the UIActivator trigger under each door sold in this zone.
        var activators = SceneScripts.Find(m2.Scripts, scene, zone.Name, "UIActivator");
        foreach (var barrier in saved.Barriers.Where(b => b.DoorId is { } id && Doors.Doors.TryGetValue(id, out var d) && d.Cost > 0))
            if (activators.FirstOrDefault(a => a.Path.StartsWith(barrier.Path + "/", StringComparison.Ordinal)) is { } activator
                && activator.Colliders.FirstOrDefault(c => c.Shape is ColliderShape.Box or ColliderShape.Sphere) is { } box)
                _doorActivators.Add((barrier.DoorId!, activator.World, new PlotRegion("UIActivator", System.Numerics.Matrix4x4.Identity, box.Center,
                    box.Shape == ColliderShape.Box ? box.Size : new System.Numerics.Vector3(box.Radius * 2)), box.Shape == ColliderShape.Sphere));
    }

    private readonly List<(string Door, System.Numerics.Matrix4x4 World, PlotRegion Box, bool Sphere)> _doorActivators = [];

    public RanchState Ranch => _saved.Ranch;
    public GameEnums Names { get; }
    public IPurse Purse { get; }
    public Expansions Doors { get; }

    /// <summary>The ids of the doors this zone sells.</summary>
    public IEnumerable<string> DoorsForSale => _doorActivators.Select(a => a.Door);

    /// <summary>Raised after an expansion was bought, with its door id.</summary>
    public event Action<string>? Bought;

    /// <summary>The barriers standing now (scene paths).</summary>
    public IEnumerable<string> Standing => _barrierNodes.Where(kv => kv.Value.Visible).Select(kv => kv.Key);

    public override void _Ready()
    {
        foreach (var (path, (renderers, colliders)) in _saved.BarrierParts)
        {
            var part = _zone with { Name = path.Replace('/', '_'), Renderers = renderers, Colliders = colliders };
            var node = ZoneBuilder.Build(part, _m2.Catalog.World, _layers).Root;
            node.AddChild(Slimes.PenWalls.Build(part, _layers));
            _barrierNodes[path] = node;
            AddChild(node);
        }
        foreach (var (door, world, box, sphere) in _doorActivators)
            AddChild(RanchPlots.Activator(world, box, DoorMeta, door, sphere));
        Lift();
        GD.Print($"Expansions: {_barrierNodes.Count} barriers standing as their own nodes, {_doorActivators.Count} doors for sale " +
                 $"({string.Join(", ", _doorActivators.Select(a => $"{a.Door} {Doors.Doors[a.Door].Cost}"))})");
    }

    public ExpansionPurchase Buy(string doorId)
    {
        var result = Doors.Buy(Ranch, doorId, Names, Purse);
        if (result == ExpansionPurchase.Done)
        {
            Lift();
            Bought?.Invoke(doorId);
        }
        return result;
    }

    /// <summary>
    /// Uses a door that isn't locked: static analysis of <c>AccessDoorUI</c>, which sets it OPEN (and
    /// the door recounts its progress) instead of showing the purchase screen. Returns false for a
    /// locked door.
    /// </summary>
    public bool Open(string doorId)
    {
        if (!Doors.Doors.TryGetValue(doorId, out var door) || Expansions.StateOf(Ranch, doorId, Names) == Names.Value(GameEnum.AccessDoorState, "LOCKED"))
            return false;
        Ranch.AccessDoors[doorId] = Names.Value(GameEnum.AccessDoorState, "OPEN");
        Doors.Recount(Ranch, door, Names);
        Lift();
        return true;
    }

    // Static analysis of AccessDoor: an open door switches its barrier off (BarrierController.SetIsOpen);
    // the barriers without a door use openranch's stand-in rule (ExpansionBarriers.IsOpen). A lifted
    // barrier also leaves physics.
    private void Lift()
    {
        foreach (var barrier in _saved.Barriers)
        {
            if (!_barrierNodes.TryGetValue(barrier.Path, out var node) || !node.Visible || !ExpansionBarriers.IsOpen(barrier, Ranch, Names))
                continue;
            node.Visible = false;
            node.ProcessMode = ProcessModeEnum.Disabled;
            GD.Print($"Expansions: barrier {barrier.Path} lifted");
        }
    }
}

/// <summary>The purchase screen of a locked expansion door: what it opens and its price (<c>doorPurchase.cost</c>).</summary>
public partial class DoorMenu : MenuScreen
{
    private readonly ExpansionShop _shop;

    public DoorMenu(ExpansionShop shop) : base("DoorMenu") => _shop = shop;

    public string? DoorId { get; private set; }
    public ExpansionPurchase? LastResult { get; private set; }

    public void Open(string doorId)
    {
        DoorId = doorId;
        var door = _shop.Doors.Doors[doorId];
        var what = string.Join(", ", door.Progress.Select(p => _shop.Names.Name(GameEnum.Progress, p)));
        ShowScreen($"Expansion {what}, {_shop.Purse.Money} newbucks");
        ClearItems();
        AddItem("buy", $"Buy for {door.Cost}", _shop.Purse.Money >= door.Cost, () =>
        {
            LastResult = _shop.Buy(doorId);
            if (LastResult == ExpansionPurchase.Done)
                Close();
            else
                SetStatus($"{LastResult}");
        });
    }
}
