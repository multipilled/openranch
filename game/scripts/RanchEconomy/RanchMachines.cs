using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Game.World;
using OpenRanch.Ranch;
using N = System.Numerics;

namespace OpenRanch.Game.RanchEconomy;

/// <summary>
/// The plots' machines in play (docs/behavior/plot-machines.md), each running while its object in the
/// plot prefab is switched on (bought): silo catchers put what is thrown in into their store's slot;
/// a corral's auto-feeder queues drops every cycle and drops food from its store; a plort collector
/// sweeps the plorts in its area into its store every period; the incinerator burns what falls in and
/// its ash trough gathers ash from burnt food. What they hold lives in the plot (<see cref="Plot.Silo"/>,
/// <see cref="Plot.Feeder"/>, <see cref="Plot.CollectorNextTime"/>, <see cref="Plot.AshUnits"/>), which
/// the save writer writes.
/// </summary>
public partial class RanchMachines : Node
{
    private readonly Economy _economy;
    private readonly Dictionary<string, int> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _nextEject = new(StringComparer.Ordinal);
    private readonly int _incinerator;

    public RanchMachines(Economy economy)
    {
        Name = "Machines";
        _economy = economy;
        Data = PlotMachineData.Read(economy.Plots.Layout, economy.Names, economy.M2.Scripts);
        _incinerator = economy.Names.Value(GameEnum.PlotType, "INCINERATOR");
    }

    public PlotMachineData Data { get; }

    /// <summary>Items each site's machines took in (caught, swept, burnt) and dropped (fed), for the checks.</summary>
    public List<(string Site, string What, string Item, double At)> Log { get; } = [];

    private RanchPlots Plots => _economy.Plots;
    private double Now => _economy.Clock.Ranch.WorldTime;

    public override void _Ready()
    {
        foreach (var site in Plots.Sites)
        {
            _types[site.Info.Id] = site.Placed.Plot.Type;
            Wire(site);
        }
        Plots.Changed += Changed;
    }

    private void Changed(string siteId)
    {
        if (Plots.Get(siteId) is not { } site)
            return;
        var plot = site.Placed.Plot;
        // A new incinerator's trough starts full (FillableAshSource.InitModel).
        if (_types.GetValueOrDefault(siteId) != plot.Type && plot.Type == _incinerator && Data.Ash.TryGetValue(plot.Type, out var ash))
            plot.AshUnits = ash.MaxAsh;
        _types[siteId] = plot.Type;
        Wire(site);
    }

    private bool IsOn(RanchPlots.Site site, long obj) => site.Placed.Prefab.Tree?.IsOn(obj, site.Placed.Upgrades.Switched) == true;

    private PrefabScriptPart? ScriptOn(RanchPlots.Site site, string scriptClass) =>
        site.Placed.Prefab.Tree?.Scripts.FirstOrDefault(s => s.Class == scriptClass && s.Enabled && IsOn(site, s.Object));

    // The trigger areas of the site's catchers and incinerator, under the site's node (rebuilt with it).
    private void Wire(RanchPlots.Site site)
    {
        if (site.Node is null || site.Placed.Prefab.Tree is not { } tree)
            return;
        var type = site.Placed.Plot.Type;
        var id = site.Info.Id;
        foreach (var catcher in Data.Catchers.GetValueOrDefault(type) ?? [])
            if (catcher.Input && tree.IsOn(catcher.Trigger.Chain, site.Placed.Upgrades.Switched))
                site.Node.AddChild(Area(site, catcher.Trigger, "SiloCatcher", body => Catch(id, catcher, body)));
        foreach (var fire in tree.Triggers.Where(t => t.Region.Class == "Incinerate" && tree.IsOn(t.Chain, site.Placed.Upgrades.Switched)))
            site.Node.AddChild(Area(site, fire, "Incinerate", body => Burn(id, body)));
    }

    private static Area3D Area(RanchPlots.Site site, PrefabTrigger trigger, string name, Action<Node> entered)
    {
        var area = RanchPlots.Activator(trigger.Region.ToRoot * site.Info.PlotWorld, trigger.Region, RanchPlots.SiteMeta, site.Info.Id, trigger.Sphere);
        area.Name = name;
        area.CollisionLayer = 0;
        area.CollisionMask = Slimes.Actor.ActorLayer;
        area.Monitoring = true;
        area.Monitorable = false;
        area.BodyEntered += body => Callable.From(() => entered(body)).CallDeferred();
        return area;
    }

    private List<AmmoSlot> Slots(Plot plot, StoreRules store)
    {
        if (!plot.Silo.TryGetValue(store.Kind, out var slots))
            plot.Silo[store.Kind] = slots = [];
        return slots;
    }

    private int ItemNumber(string id) => _economy.Names.Value(GameEnum.ItemId, id);

    /// <summary>A silo catcher takes what is thrown in, when it is vacuumable and not held, into its slot (SiloCatcher.OnTriggerEnter).</summary>
    public bool Catch(string siteId, CatcherPart catcher, Node body)
    {
        if (body is not Slimes.Actor actor || !IsInstanceValid(actor) || actor.Consumed || !actor.Vacuumable || actor.CaughtBy is not null
            || Plots.Ranch.FindPlot(siteId) is not { } plot || !catcher.Store.TryAdd(Slots(plot, catcher.Store), ItemNumber(actor.Id), actor.Id, catcher.SlotFor(plot.SiloSlotSelections)))
            return false;
        Log.Add((siteId, "caught", actor.Id, Now));
        actor.Consume();
        return true;
    }

    /// <summary>The incinerator burns what falls in, but fire (Incinerate.OnCollisionEnter); food feeds the ash trough when it is on.</summary>
    public bool Burn(string siteId, Node body)
    {
        if (body is not Slimes.Actor actor || !IsInstanceValid(actor) || actor.Consumed || !AshRules.Burns(actor.Id)
            || Plots.Get(siteId) is not { } site || !Data.Ash.TryGetValue(site.Placed.Plot.Type, out var ash))
            return false;
        var trough = ScriptOn(site, "FillableAshSource") is not null;
        site.Placed.Plot.AshUnits = ash.Burn(site.Placed.Plot.AshUnits, actor.Id, trough);
        Log.Add((siteId, "burnt", actor.Id, Now));
        actor.Consume();
        return true;
    }

    // Seconds of play (physics steps), for the machines' real-time pauses (SlimeFeeder and SiloCatcher wait on Unity's
    // Time.time, game time that stops with the game and doesn't follow the world clock's speed).
    private double _seconds;

    public override void _PhysicsProcess(double delta)
    {
        _seconds += delta;
        CatchUp();
    }

    /// <summary>Runs the machines up to the world clock now (saving calls it first, as for produce).</summary>
    public void CatchUp()
    {
        var now = Now;
        foreach (var site in Plots.Sites)
        {
            var plot = site.Placed.Plot;
            if (Data.Feeders.TryGetValue(plot.Type, out var feeder) && ScriptOn(site, "SlimeFeeder") is { } feederScript)
                Feed(site, feeder, feederScript, now);
            if (Data.Collectors.TryGetValue(plot.Type, out var collector) && ScriptOn(site, "PlortCollector") is not null)
                Collect(site, collector.Rules, collector.Area, now);
            if (_economy.M2.Tool.VacHeld && Data.Catchers.TryGetValue(plot.Type, out var catchers))
                foreach (var catcher in catchers.Where(c => c.Output))
                    GiveOut(site, catcher, now);
        }
    }

    // SiloCatcher.OnTriggerStay: a vacpack pulling at a catcher's front (within 45 degrees) gets one item
    // of its slot every quarter second, put 1.2 m out towards the vacpack.
    private void GiveOut(RanchPlots.Site site, CatcherPart catcher, double now)
    {
        if (!site.Placed.Prefab.Tree!.IsOn(catcher.Trigger.Chain, site.Placed.Upgrades.Switched))
            return;
        var key = $"{site.Info.Id}/{catcher.Trigger.Object}";
        var real = _seconds;
        if (real < _nextEject.GetValueOrDefault(key))
            return;
        var world = catcher.Trigger.Region.ToRoot * site.Info.PlotWorld;
        var at = UnityConvert.Position(N.Vector3.Transform(catcher.Trigger.Region.Center, world));
        var tool = _economy.M2.Tool;
        if (!tool.InCone(at))
            return;
        var forward = UnityConvert.Position(N.Vector3.Normalize(N.Vector3.TransformNormal(N.Vector3.UnitZ, world)));
        var toward = (tool.GlobalPosition - at).Normalized();
        if (Mathf.RadToDeg(forward.AngleTo(toward)) > CatcherPart.OutputAngleDegrees)
            return;
        var plot = site.Placed.Plot;
        if (StoreRules.TakeOne(Slots(plot, catcher.Store), catcher.SlotFor(plot.SiloSlotSelections)) is not { } item)
            return;
        var name = _economy.Names.Name(GameEnum.ItemId, item);
        if (_economy.M2.Catalog.Prefabs.Has(name))
        {
            _economy.M2.Catalog.Spawn(name, at + toward * 1.2f);
            Log.Add((site.Info.Id, "gave", name, now));
        }
        _nextEject[key] = real + CatcherPart.OutputSeconds;
    }

    // SlimeFeeder.Update: cycles queue drops; one drop every half second from the first slot of the feeder's store.
    private void Feed(RanchPlots.Site site, FeederRules rules, PrefabScriptPart script, double now)
    {
        var plot = site.Placed.Plot;
        if (plot.Feeder.NextTime == 0)
            plot.Feeder = rules.Start(plot.Feeder.Speed, now);
        plot.Feeder = rules.CatchUp(plot.Feeder, now);
        var id = site.Info.Id;
        var real = _seconds;
        if (plot.Feeder.PendingCount <= 0 || real < _nextEject.GetValueOrDefault(id))
            return;
        var store = Data.Stores.GetValueOrDefault(plot.Type)?.FirstOrDefault(s => s.KindName == "FOOD");
        if (store is not null && StoreRules.TakeOne(Slots(plot, store), 0) is { } item)
        {
            var name = _economy.Names.Name(GameEnum.ItemId, item);
            if (_economy.M2.Catalog.Prefabs.Has(name))
            {
                var world = site.Placed.Prefab.Tree!.Objects[script.Object].ToRoot * site.Info.PlotWorld;
                var forward = N.Vector3.Normalize(N.Vector3.TransformNormal(N.Vector3.UnitZ, world));
                var food = _economy.M2.Catalog.Spawn(name, UnityConvert.Position(world.Translation + forward * 0.5f));
                // AddForce((forward * 500 + noise * 400) * mass) over one fixed step of the original's physics.
                var push = UnityConvert.Position(forward * 500) + RandomUnit() * 400;
                food.ApplyCentralImpulse(push * food.Mass * _economy.M2.Catalog.FixedTimestep);
                Log.Add((id, "fed", name, now));
            }
            // Only a drop waits half a second; with the store empty the queue runs down a drop a frame (SlimeFeeder.ProcessFeedOperation).
            _nextEject[id] = real + FeederRules.EjectSeconds;
        }
        plot.Feeder = plot.Feeder with { PendingCount = Math.Max(0, plot.Feeder.PendingCount - 1) };
    }

    private static Vector3 RandomUnit()
    {
        Vector3 v;
        do
            v = new Vector3((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0));
        while (v.LengthSquared() > 1);
        return v;
    }

    // PlortCollector: every period, the plorts lying in its area go into its store.
    private void Collect(RanchPlots.Site site, CollectorRules rules, PrefabTrigger? area, double now)
    {
        var plot = site.Placed.Plot;
        if (plot.CollectorNextTime == 0)
            plot.CollectorNextTime = rules.Start(now);
        var (sweep, next) = rules.Tick(plot.CollectorNextTime, now);
        plot.CollectorNextTime = next;
        if (!sweep || area is null)
            return;
        var store = Data.Stores.GetValueOrDefault(plot.Type)?.FirstOrDefault(s => s.KindName == "PLORT");
        if (store is null)
            return;
        foreach (var actor in _economy.M2.Catalog.Live.Where(a => a.Vacuumable && a.CaughtBy is null && store.Takes(a.Id)).ToList())
        {
            // The collector's area is a trigger: a plort counts once its collider touches it, not only its middle.
            var p = actor.GlobalPosition;
            var r = actor.Radius;
            if (!new[] { Vector3.Zero, Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Forward, Vector3.Back }
                    .Any(d => area.Region.Contains(site.Info.PlotWorld, new N.Vector3(p.X + d.X * r, p.Y + d.Y * r, -(p.Z + d.Z * r)))))
                continue;
            if (!store.TryAdd(Slots(plot, store), ItemNumber(actor.Id), actor.Id))
                continue;
            Log.Add((site.Info.Id, "swept", actor.Id, now));
            actor.Consume();
        }
    }
}
