using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Simulation.Tests;

// The ability, feral and feeding-habit components on the install's slime prefabs: the values the
// game scripts read (docs/behavior/feral-slimes.md, slime-abilities.md, feeding-habits.md).
public class InstalledAbilityTests
{
    private static readonly Lazy<ItemPrefabs> Prefabs = new(() => ItemPrefabs.Read(new GameScripts(GameFactAttribute.Install!)));

    private static SerializedObject Script(string id, string cls) =>
        Prefabs.Value.Get(id).RootScript(cls) ?? throw new Xunit.Sdk.XunitException($"{id} has no {cls}");

    private static IEnumerable<string> Largos => Prefabs.Value.Ids.Where(i => i.EndsWith("_LARGO", StringComparison.Ordinal));

    [GameFact]
    public void Every_largo_can_go_feral_and_stomp_while_base_slimes_cannot()
    {
        foreach (var id in Largos)
        {
            var prefab = Prefabs.Value.Get(id);
            Assert.NotEqual(0, prefab.VacuumSize);
            var feral = Script(id, "SlimeFeral");
            Assert.Equal(3f, feral["feralLifetimeHours"]);
            Assert.Equal(true, feral["dynamicFromFeral"]);
            var stomp = Script(id, "FeralSlimeButtstomp");
            Assert.Equal(500f, stomp["explodePower"]);
            Assert.Equal(7f, stomp["explodeRadius"]);
            Assert.Equal(10f, stomp["minPlayerDamage"]);
            Assert.Equal(20f, stomp["maxPlayerDamage"]);
            Assert.Equal(false, Script(id, "AttackPlayer")["shouldAttackPlayer"]);
            Assert.Equal(20, Script(id, "AttackPlayer")["damagePerAttack"]);
            Assert.Equal(false, Script(id, "GotoPlayer")["shouldGotoPlayer"]);
            Assert.Equal(1, Script(id, "GotoPlayer")["driver"]); // agitation
            Assert.Equal(3f, Script(id, "Chomper")["timePerAttack"]);
        }
        // Base slimes carry SlimeFeral too, but at normal vacuum size it removes itself.
        Assert.Equal(0, Prefabs.Value.Get("PINK_SLIME").VacuumSize);
        Assert.Null(Prefabs.Value.Get("PINK_SLIME").RootScript("FeralSlimeButtstomp"));
    }

    [GameFact]
    public void Hunters_turn_feral_by_themselves_and_their_largos_form_feral()
    {
        Assert.Equal(true, Script("HUNTER_SLIME", "SlimeFeral")["dynamicToFeral"]);
        Assert.Equal(false, Script("PINK_SLIME", "SlimeFeral")["dynamicToFeral"]);
        Assert.NotNull(Prefabs.Value.Get("PINK_HUNTER_LARGO").RootScript("FeralizeOnLargoTransformed"));
        Assert.Null(Prefabs.Value.Get("PINK_ROCK_LARGO").RootScript("FeralizeOnLargoTransformed"));
    }

    [GameFact]
    public void Tarrs_always_go_for_and_bite_the_player()
    {
        foreach (var id in new[] { "TARR_SLIME", "GLITCH_TARR_SLIME" })
        {
            Assert.Equal(true, Script(id, "GotoPlayer")["shouldGotoPlayer"]);
            Assert.Equal(12f, Script(id, "GotoPlayer")["maxJump"]);
            Assert.Equal(true, Script(id, "AttackPlayer")["shouldAttackPlayer"]);
            Assert.Equal(20, Script(id, "AttackPlayer")["damagePerAttack"]);
        }
    }

    [GameFact]
    public void Phosphors_and_their_largos_live_only_from_six_at_night_to_six_in_the_morning()
    {
        var withWindow = Prefabs.Value.Ids.Where(i => Prefabs.Value.Get(i).RootScript("DestroyOutsideHoursOfDay") is not null).ToList();
        Assert.Contains("PHOSPHOR_SLIME", withWindow);
        Assert.All(withWindow, id => Assert.Contains("PHOSPHOR", id));
        foreach (var id in withWindow)
        {
            var d = Script(id, "DestroyOutsideHoursOfDay");
            Assert.Equal(18f, d["startHour"]);
            Assert.Equal(6f, d["endHour"]);
            Assert.Equal(0.5f, d["minEndureHoursOutsideWindow"]);
            Assert.Equal(0.55f, d["maxEndureHoursOutsideWindow"]);
            Assert.Equal(true, d["cavesPreventShutdown"]);
        }
    }

    [GameFact]
    public void Ability_components_carry_their_tuning()
    {
        var boom = Script("BOOM_SLIME", "BoomSlimeExplode");
        Assert.Equal(600f, boom["explodePower"]);
        Assert.Equal(7f, boom["explodeRadius"]);
        Assert.Equal(15f, boom["minPlayerDamage"]);
        Assert.Equal(45f, boom["maxPlayerDamage"]);

        var rad = Prefabs.Value.Get("RAD_SLIME");
        var source = Assert.Single(rad.Scripts, s => s.Class == "RadSource");
        Assert.Equal(5f, source.Data["radPerSecond"]);
        Assert.Contains(rad.Colliders, c => c.Path == source.Path && c.Collider.IsTrigger && c.Collider.Radius == 1f);

        var gen = Script("QUANTUM_SLIME", "GenerateQuantumQubit");
        Assert.Equal(20f, gen["QubitSearchRadius"]);
        Assert.Equal(5, gen["MaxQubits"]);
        Assert.Equal(5f, gen["MinGenerationDelay"]);
        Assert.Equal(20f, gen["MaxGenerationDelay"]);
        Assert.Equal(5f, Script("QUANTUM_SLIME", "QuantumSlimeSuperposition")["MinSuperposeDelay"]);
        Assert.Equal(30f, Script("QUANTUM_SLIME", "QuantumSlimeSuperposition")["MaxSuperposeDelay"]);
        Assert.Equal(0.2f, Script("QUANTUM_SLIME", "QuantumVibration")["AgitationCutoff"]);

        var pollen = Script("TANGLE_SLIME", "PollenCloudController");
        Assert.Equal(1f, pollen["pctGrowthPerGameHour"]);
        Assert.Equal(0.75f, pollen["startGrowthAgitation"]);
        Assert.Equal(5f, pollen["maxCloudScale"]);
        var vine = Script("TANGLE_SLIME", "GroundVine");
        Assert.Equal(10f, vine["maxSearchRad"]);
        Assert.Equal(2f, vine["cooldown"]);
        Assert.NotNull(Prefabs.Value.Get("HUNTER_SLIME").RootScript("SlimeStealth"));
        Assert.NotNull(Prefabs.Value.Get("MOSAIC_SLIME").RootScript("GlintController"));
    }

    [GameFact]
    public void Appearance_extras_give_the_spike_whirlwind_glint_and_pollen_prefabs()
    {
        var p = Prefabs.Value;
        var large = p.AppearancePrefab("CRYSTAL_SLIME", "CrystalAppearance", "largeCrystalPrefab")!;
        Assert.Equal(0.5f, large.RootScript("CrystalSpikesLifecycle")!["lifetime"]);
        Assert.Equal(10, large.RootScript("CrystalSpikesLifecycle")!["damagePerHit"]);
        Assert.NotNull(p.AppearancePrefab("CRYSTAL_SLIME", "CrystalAppearance", "smallCrystalPrefab"));
        // A largo's appearance carries its parent's extras.
        Assert.NotNull(p.AppearancePrefab("PINK_CRYSTAL_LARGO", "CrystalAppearance", "smallCrystalPrefab"));
        var whirl = p.AppearancePrefab("DERVISH_SLIME", "TornadoAppearance", "fullWhirlwindPrefab")!;
        Assert.Equal(0.5f, whirl.RootScript("DestroyAfterTime")!["lifeTimeHours"]);
        Assert.Equal(20f, whirl.RootScript("ActorVortexer")!["tornadoHeight"]);
        Assert.NotNull(p.AppearancePrefab("MOSAIC_SLIME", "GlintAppearance", "readyGlintPrefab"));
        var cloud = p.Referenced("TANGLE_SLIME", "PollenCloudController", "cloudActorPrefab")!;
        Assert.Equal(0.5f, cloud.RootScript("PollenCloudDestructor")!["gameHrsToLive"]);
    }

    [GameFact]
    public void Feeding_habit_components_carry_their_tuning()
    {
        var tabby = Script("TABBY_SLIME", "StalkConsumable");
        Assert.Equal(45f, tabby["maxSearchRad"]);
        Assert.Equal(false, tabby["doesParkour"]);
        Assert.Equal(true, Script("SABER_SLIME", "StalkConsumable")["doesParkour"]);
        Assert.Equal(55f, Script("SABER_SLIME", "StalkConsumable")["feintMinAngle"]);
        Assert.Equal(80f, Script("SABER_SLIME", "StalkConsumable")["feintMaxAngle"]);
        Assert.Equal(60f, Script("HUNTER_SLIME", "StalkConsumable")["maxSearchRad"]);
        Assert.Null(Prefabs.Value.Get("TABBY_SLIME").RootScript("GotoConsumable"));
        var gather = Script("TABBY_SLIME", "GatherIdentifiableItems");
        Assert.Equal(6f, gather["maxJump"]);
        Assert.Equal(10f, gather["pauseBetweenGathers"]);
        Assert.Equal(5f, gather["minGatherDist"]);
        Assert.Equal(3f, Script("FIRE_SLIME", "SlimeEatAsh")["eatRate"]);
        var water = Script("PUDDLE_SLIME", "SlimeEatWater");
        Assert.Equal(3f, water["eatRate"]);
        Assert.Equal(4, water["maxSlimeDensity"]);
        Assert.Equal(8, water["maxPlortDensity"]);
        Assert.Equal(10f, water["slimeDensityDistance"]);
        var coins = Prefabs.Value.Referenced("LUCKY_SLIME", "LuckySlimeProduceCoins", "coinsPrefab")!.RootScript("ConvertToCurrency")!;
        Assert.Equal(50, coins["amount"]);
        Assert.Equal(10, Script("ROCK_SLIME", "DamagePlayerOnTouch")["damagePerTouch"]);
        Assert.Equal(1f, Script("ROCK_SLIME", "DamagePlayerOnTouch")["repeatTime"]);
    }
}
