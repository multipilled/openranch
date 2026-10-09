using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenRanch.Game.World;
using OpenRanch.Ranch;
using N = System.Numerics;

namespace OpenRanch.Game.RanchEconomy;

/// <summary>
/// Part of <see cref="M6Check"/>: the plots' machines. On the check's corral, buy the auto-feeder and the
/// plort collector; carrots thrown into the feeder's hopper come out as drops once its cycle has passed,
/// and a plort lying in the corral is swept into the collector's store at its period. A silo takes a
/// carrot thrown in; an incinerator burns one (its trough starts full).
/// </summary>
public sealed class MachinesCheck(M6Check check, Economy economy)
{
    private RanchMachines Machines => economy.Machines;
    private double Now => economy.Clock.Ranch.WorldTime;
    private double Frame => economy.Clock.Speed * WorldClock.SecondsPerDay / economy.Clock.Cycle.Length.RealSecondsPerDay / Engine.PhysicsTicksPerSecond;

    public async Task Run(string? corralSite, string? avoid)
    {
        if (corralSite is not null)
            await Corral(corralSite);
        await Silo(avoid);
        await Incinerator(avoid);
    }

    private bool Buy(string siteId, string upgradeName)
    {
        var plots = economy.Plots;
        var upgrade = economy.Names.Value(GameEnum.PlotUpgrade, upgradeName);
        var plot = plots.Ranch.FindPlot(siteId)!;
        var offer = plots.Catalog.Menu(plot.Type)?.Upgrades.FirstOrDefault(u => u.Upgrade == upgrade);
        var before = economy.M2.Wallet.Coins;
        economy.PlotMenu.Open(siteId);
        var pressed = offer is not null && economy.PlotMenu.Press(offer.MenuItem);
        economy.PlotMenu.Close();
        var ok = pressed && plot.HasUpgrade(upgrade) && before - economy.M2.Wallet.Coins == offer!.Cost;
        check.Check(ok, $"bought {upgradeName} on {siteId}: money {before} -> {economy.M2.Wallet.Coins} (price {offer?.Cost})");
        return ok;
    }

    private static Vector3 Center(RanchPlots.Site site, PrefabTrigger t) =>
        UnityConvert.Position(N.Vector3.Transform(t.Region.Center, t.Region.ToRoot * site.Info.PlotWorld));

    private async Task<bool> Throw(RanchPlots.Site site, PrefabTrigger into, string item)
    {
        var count = Machines.Log.Count;
        economy.M2.Catalog.Spawn(item, Center(site, into) + Vector3.Up * 0.8f);
        return await check.Until(() => Machines.Log.Count > count, 5);
    }

    private async Task Corral(string siteId)
    {
        if (!Buy(siteId, "FEEDER") || !Buy(siteId, "PLORT_COLLECTOR"))
            return;
        await check.Frames(2);
        var site = economy.Plots.Get(siteId)!;
        var plot = site.Placed.Plot;
        var type = plot.Type;
        var feeder = Machines.Data.Feeders[type];
        var food = Machines.Data.Stores[type].First(s => s.KindName == "FOOD");
        var hopper = Machines.Data.Catchers[type].FirstOrDefault(c => c.Store.KindName == "FOOD" && site.Placed.Prefab.Tree!.IsOn(c.Trigger.Chain, site.Placed.Upgrades.Switched));
        if (hopper is null)
        {
            check.Check(false, "the corral's feeder has a hopper (a food SiloCatcher)");
            return;
        }
        await check.Frames(2);
        var started = plot.Feeder.NextTime;
        var startedAt = Now;
        var thrown = 0;
        for (var i = 0; i < 3; i++)
            if (await Throw(site, hopper.Trigger, "CARROT_VEGGIE"))
                thrown++;
        var stored = plot.Silo.GetValueOrDefault(food.Kind)?.Sum(s => s.Count) ?? 0;
        var hours = feeder.Hours(plot.Feeder.Speed);
        check.Check(thrown == 3 && stored == 3 && Math.Abs(started - (startedAt + hours * 3600)) <= Frame * 2,
            $"3 carrots thrown into the feeder's hopper: {stored} stored; the feeder first feeds {hours} h after it was bought (speed {economy.Names.Name(GameEnum.FeedSpeed, plot.Feeder.Speed)})");
        var fedBefore = Machines.Log.Count(l => l.Site == siteId && l.What == "fed");
        var fed = await check.Until(() => Machines.Log.Count(l => l.Site == siteId && l.What == "fed") - fedBefore >= 3, hours * 3600 / (Frame * Engine.PhysicsTicksPerSecond) + 10);
        var first = Machines.Log.FirstOrDefault(l => l.Site == siteId && l.What == "fed");
        check.Check(fed && first.At >= started - 1 && (plot.Silo.GetValueOrDefault(food.Kind)?.Sum(s => s.Count) ?? 0) == 0,
            $"the feeder dropped {Machines.Log.Count(l => l.Site == siteId && l.What == "fed")} carrots from {new WorldClock(first.At)} (due {new WorldClock(started)}; {feeder.ItemsPerFeeding} per feeding), " +
            $"{plot.Feeder.PendingCount} drops still queued");

        var (collector, area) = Machines.Data.Collectors[type];
        if (area is null)
        {
            check.Check(false, "the plort collector has an area");
            return;
        }
        var plort = economy.M2.Catalog.Spawn("PINK_PLORT", Center(site, area) + Vector3.Up * 0.3f);
        var due = plot.CollectorNextTime;
        var swept = await check.Until(() => plort.Consumed, collector.PeriodHours * 3600 / (Frame * Engine.PhysicsTicksPerSecond) + 10);
        var plorts = Machines.Data.Stores[type].First(s => s.KindName == "PLORT");
        var sweep = Machines.Log.LastOrDefault(l => l.Site == siteId && l.What == "swept");
        check.Check(swept && (plot.Silo.GetValueOrDefault(plorts.Kind)?.Sum(s => s.Count) ?? 0) == 1 && sweep.At >= due - 1 && sweep.At <= due + Frame * 2,
            $"a plort in the corral was swept into the collector's store at {new WorldClock(sweep.At)} (due {new WorldClock(due)}, every {collector.PeriodHours} h)");
    }

    private RanchPlots.Site? EmptySite(string? avoid) =>
        economy.Plots.Sites.FirstOrDefault(s => s.Placed.Plot.Type == economy.Names.Value(GameEnum.PlotType, "EMPTY") && s.Info.Id != avoid);

    private bool Build(RanchPlots.Site site, string type)
    {
        var plots = economy.Plots;
        var empty = economy.Names.Value(GameEnum.PlotType, "EMPTY");
        var want = economy.Names.Value(GameEnum.PlotType, type);
        economy.PlotMenu.Open(site.Info.Id);
        var pressed = economy.PlotMenu.Press(plots.Catalog.Menu(empty)!.Replacements.First(r => r.Type == want).MenuItem);
        economy.PlotMenu.Close();
        return pressed && plots.Get(site.Info.Id)!.Placed.Plot.Type == want;
    }

    private async Task Silo(string? avoid)
    {
        if (EmptySite(avoid) is not { } site || !Build(site, "SILO"))
        {
            check.Check(false, "built a silo");
            return;
        }
        await check.Frames(2);
        site = economy.Plots.Get(site.Info.Id)!;
        var catchers = Machines.Data.Catchers[site.Placed.Plot.Type].Where(c => site.Placed.Prefab.Tree!.IsOn(c.Trigger.Chain, site.Placed.Upgrades.Switched)).ToList();
        var thrown = catchers.Count > 0 && await Throw(site, catchers[0].Trigger, "CARROT_VEGGIE");
        var store = catchers.FirstOrDefault()?.Store;
        var slots = store is null ? null : site.Placed.Plot.Silo.GetValueOrDefault(store.Kind);
        check.Check(thrown && slots?[catchers[0].SlotFor(site.Placed.Plot.SiloSlotSelections)].Count == 1,
            $"a silo on {site.Info.Id} ({catchers.Count} catchers on, {string.Join(", ", Machines.Data.Stores[site.Placed.Plot.Type].Select(s => $"{s.KindName} {s.Slots}x{s.MaxPerSlot}"))}) " +
            $"took a carrot into slot {catchers.FirstOrDefault()?.Slot}");
        if (!thrown)
            return;

        // The vacpack pulling at the catcher's front gets the carrot back out.
        var catcher = catchers[0];
        var world = catcher.Trigger.Region.ToRoot * site.Info.PlotWorld;
        var center = UnityConvert.Position(N.Vector3.Transform(catcher.Trigger.Region.Center, world));
        var forward = UnityConvert.Position(N.Vector3.Normalize(N.Vector3.TransformNormal(N.Vector3.UnitZ, world)));
        var tool = economy.M2.Tool;
        var area = site.Node!.GetChildren().OfType<Area3D>().FirstOrDefault(a => a.Name.ToString().StartsWith("SiloCatcher", StringComparison.Ordinal));
        var aimed = area is not null && await check.Aim(area, () => tool.InCone(center)
            && Mathf.RadToDeg(forward.AngleTo((tool.GlobalPosition - center).Normalized())) <= CatcherPart.OutputAngleDegrees);
        var gaveBefore = Machines.Log.Count(l => l.What == "gave");
        tool.ScriptControlled = true;
        tool.VacHeld = true;
        var gave = aimed && await check.Until(() => Machines.Log.Count(l => l.What == "gave") > gaveBefore, 5);
        await check.Seconds(1);
        tool.VacHeld = false;
        tool.ScriptControlled = false;
        check.Check(gave && slots![catcher.SlotFor(site.Placed.Plot.SiloSlotSelections)].Count == 0,
            $"the vacpack pulling at the silo's front (aimed {aimed}) got the carrot back out; slot {catcher.Slot} holds {slots?[catcher.SlotFor(site.Placed.Plot.SiloSlotSelections)].Count}");
    }

    private async Task Incinerator(string? avoid)
    {
        if (EmptySite(avoid) is not { } site || !Build(site, "INCINERATOR"))
        {
            check.Check(false, "built an incinerator");
            return;
        }
        await check.Frames(2);
        site = economy.Plots.Get(site.Info.Id)!;
        var ash = Machines.Data.Ash[site.Placed.Plot.Type];
        var fire = site.Placed.Prefab.Tree!.Triggers.FirstOrDefault(t => t.Region.Class == "Incinerate" && site.Placed.Prefab.Tree.IsOn(t.Chain, site.Placed.Upgrades.Switched));
        var full = site.Placed.Plot.AshUnits;
        var burnt = fire is not null && await Throw(site, fire, "CARROT_VEGGIE");
        check.Check(burnt && full == ash.MaxAsh && site.Placed.Plot.AshUnits <= ash.MaxAsh,
            $"an incinerator on {site.Info.Id} burnt a carrot; its trough started full ({full} of {ash.MaxAsh}), {ash.AshPerItem} ash per food");
    }
}
