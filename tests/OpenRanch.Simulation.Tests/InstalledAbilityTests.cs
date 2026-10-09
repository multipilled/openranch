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
}
