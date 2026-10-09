using System.Collections.Generic;
using OpenRanch.Formats.Game;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// Picks a slime's behaviour pieces from the components on its prefab. Largos carry both parents'
/// components already combined, so a largo gets both parents' abilities with no special case.
/// </summary>
public static class SlimeBehaviours
{
    public static IEnumerable<SlimeBehaviour> For(ItemCatalog catalog, ItemPrefab prefab, SlimeActor slime)
    {
        if (slime.Feral is not null)
            yield return new FeralClock();
        if (prefab.RootScript("GotoPlayer") is { } gotoPlayer)
            yield return new GotoPlayer(gotoPlayer);
        if (prefab.RootScript("AttackPlayer") is { } attack)
            yield return new AttackPlayer(attack, prefab.RootScript("Chomper"));
        if (prefab.RootScript("FeralSlimeButtstomp") is { } stomp)
            yield return new FeralStompBehaviour(stomp);
        if (prefab.RootScript("DestroyOutsideHoursOfDay") is { } hours)
            yield return new NightOnly(hours);

        // Abilities (docs/behavior/slime-abilities.md).
        if (prefab.RootScript("BoomSlimeExplode") is { } boom)
            yield return new BoomExplode(boom);
        if (prefab.RootScript("RadSlimeExpand") is not null)
            yield return new RadAura(prefab);
        if (prefab.RootScript("CrystalSlimeLaunch") is not null)
            yield return new CrystalLaunch(catalog.Prefabs, prefab.Id);
        if (prefab.RootScript("GenerateQuantumQubit") is not null)
            yield return new QuantumGhosts(prefab);
        if (prefab.RootScript("DervishSlimeSpin") is not null)
            yield return new DervishSpin(catalog.Prefabs, prefab);
        if (prefab.RootScript("PollenCloudController") is not null)
            yield return new PollenCloud(catalog.Prefabs, prefab);
        if (prefab.RootScript("GroundVine") is { } vine)
            yield return new GroundVine(vine);
        if (prefab.RootScript("SlimeStealth") is not null)
            yield return new Stealth();
        if (prefab.RootScript("GlintController") is not null)
            yield return new Glints(catalog.Prefabs, prefab.Id);
    }
}
