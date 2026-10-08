using OpenRanch.Formats.Game;

namespace OpenRanch.Simulation;

/// <summary>What one food does for a slime: what comes out, how many of each, and whether it's a favorite.</summary>
public sealed record FoodEffect(string Food, IReadOnlyList<string> Produces, int CountEach, bool IsFavorite, bool IgnoresHunger);

/// <summary>
/// A slime type's rules, built from its definition in the player's install: which foods it eats,
/// what each one produces, and its hunger and eating tuning. See docs/behavior/slimes.md.
/// </summary>
public sealed class SlimeSpecies
{
    /// <summary>A food that slimes who eat it want at any hunger level. Code-only fact; see docs/behavior/slimes.md, "Deciding to eat".</summary>
    public const string AlwaysWantedFood = "SPICY_TOFU";

    private readonly Dictionary<string, FoodEffect> _foods;

    public SlimeSpecies(
        string id,
        string name,
        IEnumerable<FoodEffect> foods,
        SlimeEatingData eating,
        EmotionTuning hunger,
        EmotionTuning agitation)
    {
        Id = id;
        Name = name;
        _foods = foods.ToDictionary(f => f.Food, StringComparer.Ordinal);
        Eating = eating;
        Hunger = hunger;
        Agitation = agitation;
    }

    public string Id { get; }
    public string Name { get; }
    public SlimeEatingData Eating { get; }
    public EmotionTuning Hunger { get; }
    public EmotionTuning Agitation { get; }
    public IReadOnlyCollection<FoodEffect> Foods => _foods.Values;

    public bool Eats(string food) => _foods.ContainsKey(food);

    public FoodEffect? FoodEffect(string food) => _foods.GetValueOrDefault(food);

    /// <summary>
    /// Builds a species from the install's definition. <paramref name="allIds"/> is every item id
    /// name (from the game's item id list), used to expand the diet's food groups.
    /// </summary>
    public static SlimeSpecies From(SlimeInfo info, IEnumerable<string> allIds)
    {
        if (info.Eating is null || info.Hunger is null || info.Agitation is null)
            throw new InvalidDataException($"{info.Id} has no slime prefab with eating and emotion settings.");
        var ids = allIds as ICollection<string> ?? allIds.ToList();
        var diet = info.Diet;
        var foods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in diet.FoodGroups)
            foods.UnionWith(Items.FoodGroupMembers(group, ids));
        foods.UnionWith(diet.AdditionalFoods);

        var effects = foods.Select(food =>
        {
            var favorite = diet.Favorites.Contains(food);
            return new FoodEffect(food, diet.Produces, favorite ? diet.FavoriteProductionCount : 1, favorite, food == AlwaysWantedFood);
        });
        return new SlimeSpecies(info.Id, info.Name, effects, info.Eating, info.Hunger, info.Agitation);
    }
}
