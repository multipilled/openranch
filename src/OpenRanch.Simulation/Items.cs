namespace OpenRanch.Simulation;

/// <summary>What sort of thing an item id names. See docs/behavior/slimes.md, "Item kinds".</summary>
public enum ItemKind
{
    Other,
    Veggie,
    Fruit,
    Meat,
    Tofu,
    Plort,
    Slime,
    Largo,
    Gordo,
    Chick,
    Liquid,
    Craft,
}

/// <summary>
/// Sorts the game's item ids (names such as "CARROT_VEGGIE" read from the install) into kinds and
/// diet food groups. The rules are written in docs/behavior/slimes.md.
/// </summary>
public static class Items
{
    // The few exceptions to the name rules live only in the game's code, so they are named here.
    // See docs/behavior/slimes.md, "Food groups".
    public const string GingerVeggie = "GINGER_VEGGIE";
    public static readonly IReadOnlySet<string> TarrSlimes = new HashSet<string> { "TARR_SLIME", "GLITCH_TARR_SLIME" };
    public static readonly IReadOnlySet<string> SlimesNeverEatenAsFood = new HashSet<string> { "GOLD_SLIME", "LUCKY_SLIME" };
    public static readonly IReadOnlySet<string> PlortsNeverEatenAsFood = new HashSet<string> { "PUDDLE_PLORT", "GOLD_PLORT", "FIRE_PLORT" };

    public static ItemKind KindOf(string id)
    {
        if (id.EndsWith("_VEGGIE", StringComparison.Ordinal)) return ItemKind.Veggie;
        if (id.EndsWith("_FRUIT", StringComparison.Ordinal)) return ItemKind.Fruit;
        if (id.EndsWith("_TOFU", StringComparison.Ordinal)) return ItemKind.Tofu;
        if (id.EndsWith("_SLIME", StringComparison.Ordinal)) return ItemKind.Slime;
        if (id.EndsWith("_LARGO", StringComparison.Ordinal)) return ItemKind.Largo;
        if (id.EndsWith("_GORDO", StringComparison.Ordinal)) return ItemKind.Gordo;
        if (id.EndsWith("_PLORT", StringComparison.Ordinal)) return ItemKind.Plort;
        if (id.EndsWith("HEN", StringComparison.Ordinal) || id.EndsWith("ROOSTER", StringComparison.Ordinal)) return ItemKind.Meat;
        if (id == "CHICK" || id.EndsWith("_CHICK", StringComparison.Ordinal)) return ItemKind.Chick;
        if (id.EndsWith("_LIQUID", StringComparison.Ordinal)) return ItemKind.Liquid;
        if (id.EndsWith("_CRAFT", StringComparison.Ordinal)) return ItemKind.Craft;
        return ItemKind.Other;
    }

    public static bool IsPlort(string id) => KindOf(id) == ItemKind.Plort;
    public static bool IsLiquid(string id) => KindOf(id) == ItemKind.Liquid;
    /// <summary>A slime of any kind: ordinary slimes and largos both count (the game's own "is a slime" test).</summary>
    public static bool IsSlime(string id) => KindOf(id) is ItemKind.Slime or ItemKind.Largo;

    /// <summary>Whether <paramref name="id"/> belongs to a diet food group ("FRUIT", "VEGGIES", "MEAT", "PLORTS", "NONTARRGOLD_SLIMES", "GINGER").</summary>
    public static bool InFoodGroup(string id, string group) => group switch
    {
        "FRUIT" => KindOf(id) == ItemKind.Fruit,
        "VEGGIES" => KindOf(id) == ItemKind.Veggie,
        "MEAT" => KindOf(id) == ItemKind.Meat,
        "GINGER" => id == GingerVeggie,
        "PLORTS" => KindOf(id) == ItemKind.Plort && !PlortsNeverEatenAsFood.Contains(id),
        "NONTARRGOLD_SLIMES" => IsSlime(id) && !TarrSlimes.Contains(id) && !SlimesNeverEatenAsFood.Contains(id),
        _ => false,
    };

    public static IEnumerable<string> FoodGroupMembers(string group, IEnumerable<string> allIds) =>
        allIds.Where(id => InFoodGroup(id, group));
}
