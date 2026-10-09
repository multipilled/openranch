using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenRanch.Game.World;
using OpenRanch.Ranch;
using N = System.Numerics;

namespace OpenRanch.Game.RanchEconomy;

/// <summary>
/// Part 3 of <see cref="M6Check"/>: on an empty site, build a garden and plant it by dropping a carrot
/// into its hole; its first batch ripens, falls, rots and goes at the produce's times, and the crop grows
/// again 18 to 24 game hours later. Then build a coop, put a hen and a rooster in it: the hen lays at her
/// laying time and the chick grows up after its delay. Run with --day-speed (720 runs a game day in 2 s).
/// </summary>
public sealed class ProduceCheck(M6Check check, Economy economy)
{
    private const double Hour = 3600;
    private RanchProduce Produce => economy.Produce;
    private double Now => economy.Clock.Ranch.WorldTime;
    // One physics frame of game time at the clock's speed, the slack allowed on times seen frame by frame.
    private double Frame => economy.Clock.Speed * WorldClock.SecondsPerDay / economy.Clock.Cycle.Length.RealSecondsPerDay / Engine.PhysicsTicksPerSecond;

    /// <summary>The garden's site and a hen put in the coop, for the save check.</summary>
    public string? GardenSite { get; private set; }
    public Slimes.Actor? Hen { get; private set; }

    public async Task Run()
    {
        check.Info($"clock speed x{economy.Clock.Speed}: a physics frame is {Frame / 60:F1} game minutes");
        await Garden();
        await Coop();
    }

    private RanchPlots.Site? EmptySite(string avoid = "") =>
        economy.Plots.Sites.FirstOrDefault(s => s.Placed.Plot.Type == economy.Names.Value(GameEnum.PlotType, "EMPTY") && s.Info.Id != avoid && s.Info.Id != check.CorralSite);

    private bool Build(RanchPlots.Site site, string type)
    {
        var plots = economy.Plots;
        var empty = economy.Names.Value(GameEnum.PlotType, "EMPTY");
        var want = economy.Names.Value(GameEnum.PlotType, type);
        var before = economy.M2.Wallet.Coins;
        var price = plots.Catalog.BuildCost(empty, want)!.Value;
        economy.PlotMenu.Open(site.Info.Id);
        var pressed = economy.PlotMenu.Press(plots.Catalog.Menu(empty)!.Replacements.First(r => r.Type == want).MenuItem);
        economy.PlotMenu.Close();
        var ok = pressed && plots.Get(site.Info.Id)!.Placed.Plot.Type == want && before - economy.M2.Wallet.Coins == price;
        check.Check(ok, $"built a {type.ToLowerInvariant()} on {site.Info.Id}: money {before} -> {economy.M2.Wallet.Coins} (the install's price {price})");
        return ok;
    }

    // Where a trigger box of the site's plot is, in Godot coordinates.
    private static Vector3? TriggerCenter(RanchPlots.Site site, string script)
    {
        var tree = site.Placed.Prefab.Tree;
        var t = tree?.Triggers.FirstOrDefault(t => t.Region.Class == script && tree.IsOn(t.Chain, site.Placed.Upgrades.Switched));
        return t is null ? null : UnityConvert.Position(N.Vector3.Transform(t.Region.Center, t.Region.ToRoot * site.Info.PlotWorld));
    }

    private async Task Garden()
    {
        if (EmptySite() is not { } site || !Build(site, "GARDEN"))
            return;
        await check.Frames(2);
        site = economy.Plots.Get(site.Info.Id)!;
        GardenSite = site.Info.Id;
        const string seed = "CARROT_VEGGIE";
        var hole = TriggerCenter(site, "GardenCatcher");
        if (hole is null || !economy.Plots.Plantable.TryGetValue(seed, out var crop))
        {
            check.Check(false, $"the garden has a planting hole ({hole}) that takes {seed}");
            return;
        }
        // Thrown in: the carrot falls into the hole and is planted.
        var carrot = economy.M2.Catalog.Spawn(seed, hole.Value + Vector3.Up * 1.5f);
        var planted = await check.Until(() => economy.Plots.Ranch.FindPlot(site.Info.Id)!.AttachedResource == crop.Crop, 10);
        var plantedAt = Now;
        check.Check(planted && carrot.Consumed, $"a carrot dropped into the garden's hole is planted: {economy.Names.Name(GameEnum.SpawnResource, crop.Crop)}");
        if (!planted)
            return;

        var run = Produce.Crops.Values.FirstOrDefault(c => c.SiteId == site.Info.Id);
        if (run is null || !await check.Until(() => run.Batches.Count > 0, 10))
        {
            check.Check(false, "the planted crop grows its first batch");
            return;
        }
        var rules = run.Clock.Rules;
        var (due, batch) = run.Batches[0];
        var times = run.First;
        // A crop planted in play grows at once (SpawnResource.SetModel: no saved spawn time, not the game's first moment).
        check.Check(batch.Count >= (int)rules.MinObjects && batch.Count <= Math.Min((int)rules.MaxObjects, run.Joints.Length) && Math.Abs(due - plantedAt) <= Frame * 2,
            $"first batch: {batch.Count} {string.Join("/", batch.Select(a => a.Id).Distinct())} on {run.Joints.Length} joints at {new WorldClock(due)} " +
            $"(the crop grows {rules.MinObjects}-{rules.MaxObjects}), unripe {times.UnripeHours} h, ripe {times.RipeHours} h, edible {times.EdibleHours} h, rotten {times.RottenHours} h");
        var growing = batch.Where(Produce.Produce.ContainsKey).Select(a => Produce.Produce[a].Cycle).ToList();
        check.Check(batch.All(a => a.Freeze && !a.Vacuumable && !a.Edible), "the batch hangs from the joints, unripe: small, not vacuumable, not edible");

        // Through every stage: wait until the batch is gone (about 54 game hours).
        var life = (times.UnripeHours + 1.1 * (times.RipeHours + times.EdibleHours + times.RottenHours)) * Hour;
        var gone = await check.Until(() => growing.All(c => c.Stage == ProduceStage.Gone), life / (Frame * Engine.PhysicsTicksPerSecond) + 10);
        var slack = Frame * 2 + 1;
        bool Within(ProduceStage from, ProduceStage to, float hours, bool varied, ProduceCycle c)
        {
            var a = c.History.FirstOrDefault(h => h.Stage == from).At;
            var b = c.History.FirstOrDefault(h => h.Stage == to).At;
            var took = (b - a) / Hour;
            return c.History.Any(h => h.Stage == to) && (varied ? took >= 0.9 * hours - slack / Hour && took <= 1.1 * hours + slack / Hour : Math.Abs(took - hours) <= slack / Hour);
        }
        string Range(ProduceStage from, ProduceStage to) =>
            string.Join("-", new[] { growing.Min(c => Took(c, from, to)), growing.Max(c => Took(c, from, to)) }.Select(h => $"{h:F2}").Distinct()) + " h";
        static double Took(ProduceCycle c, ProduceStage from, ProduceStage to) =>
            (c.History.FirstOrDefault(h => h.Stage == to).At - c.History.FirstOrDefault(h => h.Stage == from).At) / Hour;
        check.Check(gone && growing.All(c => Within(ProduceStage.Unripe, ProduceStage.Ripe, times.UnripeHours, false, c)),
            $"ripe after {Range(ProduceStage.Unripe, ProduceStage.Ripe)} (the produce's {times.UnripeHours} h from the batch's time)");
        check.Check(gone && growing.All(c => Within(ProduceStage.Ripe, ProduceStage.Edible, times.RipeHours, true, c)),
            $"fell off ripe after {Range(ProduceStage.Ripe, ProduceStage.Edible)} (its {times.RipeHours} h, varied 0.9-1.1)");
        check.Check(gone && growing.All(c => Within(ProduceStage.Edible, ProduceStage.Rotten, times.EdibleHours, true, c)),
            $"rotten after {Range(ProduceStage.Edible, ProduceStage.Rotten)} edible (its {times.EdibleHours} h, varied)");
        check.Check(gone && growing.All(c => Within(ProduceStage.Rotten, ProduceStage.Gone, times.RottenHours, true, c)) && batch.All(a => !GodotObject.IsInstanceValid(a) || a.Consumed),
            $"gone after {Range(ProduceStage.Rotten, ProduceStage.Gone)} rotten (its {times.RottenHours} h, varied); {batch.Count(a => GodotObject.IsInstanceValid(a) && !a.Consumed)} left");

        // Regrown: the next batch was due 18 to 24 hours after the first.
        var regrown = run.Batches.Count > 1;
        var gap = regrown ? (run.Batches[1].Due - due) / Hour : 0;
        check.Check(regrown && gap >= rules.MinIntervalHours - slack / Hour && gap <= rules.MaxIntervalHours + slack / Hour && run.Batches[1].Produce.Count > 0,
            $"regrown: {run.Batches.Count} batches so far ({string.Join(", ", run.Batches.Select(b => b.Produce.Count))}), the second {gap:F2} h after the first " +
            $"(the crop's {rules.MinIntervalHours}-{rules.MaxIntervalHours} h)");
    }

    private async Task Coop()
    {
        if (EmptySite(GardenSite ?? "") is not { } site || !Build(site, "COOP"))
            return;
        await check.Frames(2);
        site = economy.Plots.Get(site.Info.Id)!;
        if (TriggerCenter(site, "CoopRegion") is not { } inside)
        {
            check.Check(false, "the coop has a CoopRegion inside");
            return;
        }
        var floor = inside;
        var hen = economy.M2.Catalog.Spawn("HEN", floor + new Vector3(0.5f, 0, 0));
        var rooster = economy.M2.Catalog.Spawn("ROOSTER", floor - new Vector3(0.5f, 0, 0));
        Hen = hen;
        var since = Now;
        await check.Frames(2);
        var rules = economy.Produce.Hens.Contains(hen) ? Produce.Data.Reproduce("HEN") : null;
        if (rules is null)
        {
            check.Check(false, "the hen is on the laying clock");
            return;
        }
        var laid = await check.Until(() => Produce.Laid.Any(l => l.Hen == hen), rules.MaxHours * Hour / (Frame * Engine.PhysicsTicksPerSecond) + 10);
        var lay = Produce.Laid.FirstOrDefault(l => l.Hen == hen);
        var after = laid ? (lay.At - since) / Hour : 0;
        var slack = (Frame * 2 + 1) / Hour;
        check.Check(laid && lay.Chick.Id == rules.ChildId && after >= rules.MinHours - slack && after <= rules.MaxHours + slack,
            $"the coop's hen laid a {lay.Chick?.Id} {after:F2} h after she came (her {rules.MinHours}-{rules.MaxHours} h, with the {rules.MateId} near)");
        if (!laid)
            return;
        var grow = Produce.Data.Transform(lay.Chick.Id)!;
        var grew = await check.Until(() => Produce.GrewUp.Any(g => g.At >= lay.At), grow.DelayHours * Hour / (Frame * Engine.PhysicsTicksPerSecond) + 10);
        var up = Produce.GrewUp.FirstOrDefault(g => g.At >= lay.At);
        var took = grew ? (up.At - lay.At) / Hour : 0;
        check.Check(grew && Math.Abs(took - grow.DelayHours) <= slack && grow.Options.Any(o => o.Id == up.Into.Id),
            $"the chick grew into a {up.Into?.Id} after {took:F2} h (its {grow.DelayHours} h; {string.Join(" / ", grow.Options.Select(o => $"{o.Id} {o.Weight}"))})");
    }

    /// <summary>The save holds the crop's clock, the produce's stages and the hen's laying time.</summary>
    public void CheckSave(RanchState read)
    {
        var run = Produce.Crops.Values.FirstOrDefault(c => c.SiteId == GardenSite);
        var saved = run is null ? null : read.World.ResourceSpawners.FirstOrDefault(t =>
            N.Vector3.DistanceSquared(new N.Vector3(t.Position.X, t.Position.Y, t.Position.Z), run.Spawner.Position) < 0.01f);
        check.Check(saved is not null && Math.Abs(saved.NextSpawnTime - run!.Clock.NextSpawnTime) < 1,
            $"the save's garden crop grows next at {(saved is null ? "missing" : new WorldClock(saved.NextSpawnTime).ToString())}");
        var unripe = economy.Names.Value(GameEnum.ResourceCycleState, "UNRIPE");
        var hanging = read.Actors.Count(a => a.CycleState == unripe && a.CycleProgressTime > read.WorldTime);
        var live = Produce.Produce.Values.Count(g => g.Cycle.Stage == ProduceStage.Unripe);
        check.Check(hanging == live, $"the save's unripe produce: {hanging} (the world's {live}), each with its stage's end");
        var hens = read.Actors.Where(a => economy.Names.Name(GameEnum.ItemId, a.TypeId) == "HEN" && a.ReproduceTime > read.WorldTime).ToList();
        check.Check(hens.Count > 0, $"the save's hens lay next at {string.Join(", ", hens.Select(h => new WorldClock(h.ReproduceTime)))}");
    }
}
