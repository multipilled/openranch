using OpenRanch.Formats.Game;
using Xunit.Abstractions;

namespace OpenRanch.Ranch.Tests;

public class ProduceTests(ITestOutputHelper output)
{
    // Draws that always give the same answers: floats at a fixed point of their range, whole numbers at the bottom.
    private sealed class FixedDraws(float at = 0.5f, bool chance = false) : IDraws
    {
        public float Range(float low, float high) => low + (high - low) * at;
        public int Range(int low, int high) => low;
        public bool Chance(float p) => chance;
    }

    private const double Hour = 3600, Day2 = 86_400 + 6 * Hour;
    // The game's own defaults on ResourceCycle (the install's values are printed by Install_produce_crops_and_hens).
    private static readonly ProduceTimes Carrot = new(6, 6, 12, 6, 1);

    [Fact]
    public void Produce_goes_through_its_stages_on_the_clock()
    {
        var produce = new ProduceCycle(Carrot, new FixedDraws(0.5f)); // Vary = exactly the hours
        produce.Attach(Day2);
        produce.ProgressTo(Day2 + 6 * Hour, Day2);
        Assert.Equal(ProduceStage.Unripe, produce.Stage);
        Assert.Empty(produce.Advance(Day2 + 6 * Hour - 1));
        Assert.Equal([ProduceStage.Ripe], produce.Advance(Day2 + 6 * Hour));
        Assert.Equal(Day2 + 12 * Hour, produce.ProgressTime);
        Assert.Equal([ProduceStage.Edible], produce.Advance(Day2 + 12 * Hour));
        Assert.Equal([ProduceStage.Rotten], produce.Advance(Day2 + 24 * Hour));
        Assert.Equal([ProduceStage.Gone], produce.Advance(Day2 + 30 * Hour));
        Assert.Equal(double.MaxValue, produce.ProgressTime);
    }

    [Fact]
    public void A_late_update_counts_each_stage_from_when_the_last_ended()
    {
        var produce = new ProduceCycle(Carrot, new FixedDraws(0.5f));
        produce.Attach(Day2);
        produce.ProgressTo(Day2 + 6 * Hour, Day2);
        // Looked at again a day later: ripe at 6 h, edible at 12 h, rotten at 24 h, gone at 30 h.
        Assert.Equal([ProduceStage.Ripe, ProduceStage.Edible, ProduceStage.Rotten], produce.Advance(Day2 + 29 * Hour));
        Assert.Equal(Day2 + 30 * Hour, produce.ProgressTime);
    }

    [Fact]
    public void Stages_vary_by_a_tenth_and_ripe_produce_lets_go_when_pulled()
    {
        var shortest = new ProduceCycle(Carrot, new FixedDraws(0f));
        shortest.Attach(Day2);
        Assert.Equal(Day2 + 0.9 * 6 * Hour, shortest.ProgressTime, 1);
        var longest = new ProduceCycle(Carrot, new FixedDraws(1f));
        longest.Attach(Day2);
        Assert.Equal(Day2 + 1.1 * 6 * Hour, longest.ProgressTime, 1);
        Assert.Equal([ProduceStage.Ripe], longest.Advance(Day2 + 7 * Hour));
        Assert.Equal([ProduceStage.Edible], longest.Advance(Day2 + 8 * Hour, release: true));
        // Loose produce is edible for its varied edible hours, counted from no earlier than a new game's start.
        var loose = new ProduceCycle(Carrot, new FixedDraws(0.5f));
        loose.InitLoose(0);
        Assert.Equal((ProduceStage.Edible, ProduceCycle.Start + 12 * Hour), (loose.Stage, loose.ProgressTime));
    }

    private static CropSpawnRules Patch(int maxActive = 0) =>
        new(["CARROT_VEGGIE"], [], 4, 8, 6, 18, 24, 0.01f, 0, maxActive, 0, false);

    [Fact]
    public void A_crop_grows_now_then_every_18_to_24_hours()
    {
        var clock = new CropSpawnClock(Patch(), Carrot, new FixedDraws(0.5f), onPlot: true);
        clock.Init(Day2);
        Assert.Equal(Day2, clock.NextSpawnTime);
        var due = clock.Update(Day2, 1, sprinkler: false, miracleMix: false);
        Assert.Equal([new SpawnRequest(Day2)], due);
        Assert.Equal(Day2 + 21 * Hour, clock.NextSpawnTime);
        Assert.Equal(Day2 + 6 * Hour, CropSpawnClock.UnripeEnds(due[0], Carrot, Day2));
        Assert.Empty(clock.Update(Day2 + 20 * Hour, 20 * Hour, false, false));
        // A sprinkler waters it: the clock runs half again as fast.
        clock.Update(Day2 + 20 * Hour + 1000, 1000, sprinkler: true, miracleMix: false);
        Assert.Equal(Day2 + 21 * Hour - 500, clock.NextSpawnTime);
    }

    [Fact]
    public void A_new_games_crops_have_a_head_start_and_a_crop_away_for_days_catches_up()
    {
        var fresh = new CropSpawnClock(Patch(), Carrot, new FixedDraws(0.5f), onPlot: true);
        fresh.Init(ProduceCycle.Start);
        Assert.Equal(ProduceCycle.Start - 6 * Hour, fresh.NextSpawnTime);

        var away = new CropSpawnClock(Patch(), Carrot, new FixedDraws(0.5f), onPlot: true) { NextSpawnTime = Day2 };
        // Three days later: batches every 21 hours; those 30 hours (6+6+12+6) or more overdue have rotted away.
        Assert.Equal([new SpawnRequest(Day2 + 63 * Hour)], away.Update(Day2 + 72 * Hour, 0, false, false));
        Assert.Equal(Day2 + 84 * Hour, away.NextSpawnTime);
        // A crop off the plots (the scene's own) grows one batch, at the last interval before now.
        var wild = new CropSpawnClock(Patch(), Carrot, new FixedDraws(0.5f), onPlot: false) { NextSpawnTime = Day2 };
        Assert.Equal([new SpawnRequest(Day2 + 63 * Hour)], wild.Update(Day2 + 72 * Hour, 0, false, false));
    }

    [Fact]
    public void A_batch_draws_its_size_from_the_soil_and_skips_rotten_produce()
    {
        var clock = new CropSpawnClock(Patch(maxActive: 10), Carrot, new FixedDraws(0.5f), onPlot: true);
        ProduceTimes? Times(string _) => Carrot;
        Assert.Equal(6, clock.Batch(new SpawnRequest(Day2), Day2, richSoil: false, active: 0, Times).Count); // floor(4 + 0.5 * 4)
        Assert.Equal(7, clock.Batch(new SpawnRequest(Day2), Day2, richSoil: true, active: 0, Times).Count);  // floor(6 + 0.5 * 2)
        Assert.Empty(clock.Batch(new SpawnRequest(Day2), Day2, false, active: 10, Times));
        Assert.Empty(clock.Batch(new SpawnRequest(Day2), Day2 + 24 * Hour, false, 0, Times));
    }

    private static readonly ReproduceRules Hen = new("ROOSTER", 10, ["HEN", "ROOSTER", "CHICK"], 10, 10, "CHICK", 6, 12, 2);

    [Fact]
    public void Hens_lay_with_a_mate_near_and_room_and_always_in_a_coop()
    {
        Assert.Equal(9, Hen.Period(new FixedDraws(0.5f)));
        Assert.Equal(1, Hen.Lays(mateNear: true, crowd: 10, inCoop: true, deluxeCoop: false, inVitamizer: false, new FixedDraws()));
        Assert.Equal(0, Hen.Lays(true, 11, true, false, false, new FixedDraws()));
        Assert.Equal(1, Hen.Lays(true, 20, true, deluxeCoop: true, false, new FixedDraws()));
        Assert.Equal(0, Hen.Lays(mateNear: false, 0, true, false, false, new FixedDraws()));
        Assert.Equal(0, Hen.Lays(true, 0, inCoop: false, false, false, new FixedDraws(chance: false)));
        Assert.Equal(2, Hen.Lays(true, 0, true, false, inVitamizer: true, new FixedDraws(chance: true)));
    }

    [Fact]
    public void Chicks_turn_into_an_option_picked_by_weight()
    {
        var grow = new TransformRules(6, [("HEN", 3), ("ROOSTER", 1)]);
        // The hen is picked first; the rooster replaces her when a draw up to the running total (4) falls under its weight (1).
        Assert.Equal("HEN", grow.Pick(new FixedDraws(0.5f)));
        Assert.Equal("ROOSTER", grow.Pick(new FixedDraws(0.1f)));
    }

    [GameFact]
    public void Install_produce_crops_and_hens()
    {
        using var scripts = new GameScripts(GameFactAttribute.Install!);
        var data = ProduceData.Read(scripts);
        var produce = data.Ids.Select(id => (id, t: data.Times(id))).Where(p => p.t is not null).ToList();
        foreach (var (id, t) in produce)
            output.WriteLine($"{id}: unripe {t!.UnripeHours} h, ripe {t.RipeHours} h, edible {t.EdibleHours} h, rotten {t.RottenHours} h, release {t.ReleasePrepSeconds} s");
        Assert.Contains(produce, p => p.id == "CARROT_VEGGIE");

        var scene = scripts.Assets.File("level3")!;
        var crops = PlotCrops.Read(scripts, scene);
        foreach (var crop in crops.Prefabs.Values)
        {
            var rules = data.SpawnRules(crop.Script!.File, crop.Script.Data, crop.Script.ForceFirstRipeness);
            output.WriteLine($"{crop.Name}: {string.Join("/", rules.Produce)} (+{string.Join("/", rules.Bonus)}), {rules.MinObjects}-{rules.MaxObjects} " +
                             $"(rich soil {rules.MinNutrientObjects}), every {rules.MinIntervalHours}-{rules.MaxIntervalHours} h, {crop.JointsToRoot.Count} joints, " +
                             $"max active {rules.MaxActiveSpawns}");
            Assert.NotEmpty(rules.Produce);
            Assert.All(rules.Produce, id => Assert.NotNull(data.Times(id)));
        }

        var hens = data.Ids.Select(id => (id, r: data.Reproduce(id))).Where(h => h.r is not null).ToList();
        foreach (var (id, r) in hens)
            output.WriteLine($"{id} lays {r!.ChildId} every {r.MinHours}-{r.MaxHours} h with {r.MateId} within {r.MaxDistToMate} m, " +
                             $"crowd {r.MaxDensity} (x{r.DeluxeDensityFactor} deluxe) of {string.Join("/", r.DensityIds)} within {r.DensityDist} m");
        Assert.NotEmpty(hens);
        foreach (var (id, t) in data.Ids.Select(id => (id, t: data.Transform(id))).Where(c => c.t is not null))
            output.WriteLine($"{id} grows into {string.Join(" / ", t!.Options.Select(o => $"{o.Id} ({o.Weight})"))} after {t.DelayHours} h");
    }
}
