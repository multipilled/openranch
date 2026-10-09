using System;
using System.Collections.Generic;
using System.Linq;
using OpenRanch.Formats.Game;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// Picks a slime's behaviour pieces from the components on its prefab. Largos carry both parents'
/// components already combined, so a largo gets both parents' abilities with no special case.
/// </summary>
public static class SlimeBehaviours
{
    /// <summary>The position of the first of <paramref name="classes"/> among the prefab root's components (int.MaxValue if none).</summary>
    public static int OrderOf(ItemPrefab prefab, params string[] classes)
    {
        var roots = prefab.Scripts.Where(s => s.Path == prefab.Name).ToList();
        var i = roots.FindIndex(s => classes.Contains(s.Class));
        return i < 0 ? int.MaxValue : i;
    }

    public static IEnumerable<SlimeBehaviour> For(ItemCatalog catalog, ItemPrefab prefab, SlimeActor slime)
    {
        foreach (var (component, piece) in Pieces(catalog, prefab, slime))
        {
            piece.Order = OrderOf(prefab, component);
            yield return piece;
        }
    }

    // (the component a piece comes from, the piece)
    private static IEnumerable<(string, SlimeBehaviour)> Pieces(ItemCatalog catalog, ItemPrefab prefab, SlimeActor slime)
    {
        var prefabs = catalog.Prefabs;
        bool Has(string cls) => prefab.RootScript(cls) is not null;

        // Feral slimes and going for the player (docs/behavior/feral-slimes.md).
        if (slime.Feral is not null)
            yield return ("SlimeFeral", new FeralClock());
        if (prefab.RootScript("GotoPlayer") is { } gotoPlayer)
            yield return ("GotoPlayer", new GotoPlayer(gotoPlayer));
        if (prefab.RootScript("AttackPlayer") is { } attack)
            yield return ("AttackPlayer", new AttackPlayer(attack, prefab.RootScript("Chomper")));
        if (prefab.RootScript("FeralSlimeButtstomp") is { } stomp)
            yield return ("FeralSlimeButtstomp", new FeralStompBehaviour(stomp));
        if (prefab.RootScript("DestroyOutsideHoursOfDay") is { } hours)
            yield return ("DestroyOutsideHoursOfDay", new NightOnly(hours));

        // Abilities (docs/behavior/slime-abilities.md).
        if (prefab.RootScript("BoomSlimeExplode") is { } boom)
            yield return ("BoomSlimeExplode", new BoomExplode(boom));
        if (Has("RadSlimeExpand"))
            yield return ("RadSlimeExpand", new RadAura(prefab));
        if (Has("CrystalSlimeLaunch"))
            yield return ("CrystalSlimeLaunch", new CrystalLaunch(prefabs, prefab.Id));
        if (Has("GenerateQuantumQubit"))
            yield return ("QuantumSlimeSuperposition", new QuantumGhosts(prefab));
        if (Has("DervishSlimeSpin"))
            yield return ("DervishSlimeSpin", new DervishSpin(prefabs, prefab));
        if (Has("PollenCloudController"))
            yield return ("PollenCloudController", new PollenCloud(prefabs, prefab));
        if (prefab.RootScript("GroundVine") is { } vine)
            yield return ("GroundVine", new GroundVine(vine));
        if (Has("SlimeStealth"))
            yield return ("SlimeStealth", new Stealth());
        if (Has("GlintController"))
            yield return ("GlintController", new Glints(prefabs, prefab.Id));

        // Feeding habits (docs/behavior/feeding-habits.md).
        if (prefab.RootScript("StalkConsumable") is { } stalk)
            yield return ("StalkConsumable", new StalkPounce(stalk));
        if (prefab.RootScript("GatherIdentifiableItems") is { } gather)
            yield return ("GatherIdentifiableItems", new Gather(gather));
        if (Has("GoldSlimeProducePlorts"))
            yield return ("GoldSlimeFlee", new GoldRunner(catalog, prefab));
        if (Has("LuckySlimeProduceCoins"))
            yield return ("LuckySlimeFlee", new LuckyCoins(catalog, prefab));
        if (Has("SlimeEatAsh"))
            yield return ("GotoAsh", new Grazer(catalog, prefab, FeedingPatch.Ash));
        if (Has("SlimeEatWater"))
            yield return ("GotoWater", new Grazer(catalog, prefab, FeedingPatch.Water));
        if (prefab.RootScript("DamagePlayerOnTouch") is { } touch)
            yield return ("DamagePlayerOnTouch", new DamageOnTouch(touch));
    }
}
