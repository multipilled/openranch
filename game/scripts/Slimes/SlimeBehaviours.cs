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
    }
}
