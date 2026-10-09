using OpenRanch.Formats.Game;

namespace OpenRanch.Simulation;

/// <summary>Which of the slime's feelings makes it want a food: hunger, agitation, or neither (always wanted).</summary>
public enum FoodDriver
{
    Hunger,
    Agitation,
    None,
}

/// <summary>
/// What one food does for a slime: what comes out, how many of each, and whether it's a favorite.
/// <see cref="Becomes"/> names the slime it turns into instead (a largo or a tarr). <see cref="Rows"/>
/// is how many of the game's eat rules match the food (one per product); each counts as a meal for
/// hunger and agitation. A slime wants the food with a drive of its <see cref="Driver"/> feeling, at
/// least <see cref="MinDrive"/>, plus <see cref="ExtraDrive"/>. See docs/behavior/slimes.md and largos.md.
/// </summary>
public sealed record FoodEffect(string Food, IReadOnlyList<string> Produces, int CountEach, bool IsFavorite, bool IgnoresHunger,
    string? Becomes = null, FoodDriver Driver = FoodDriver.Hunger, float MinDrive = 0, float ExtraDrive = 0, int Rows = 1);

/// <summary>
/// A slime type's rules, built from its definition in the player's install: which foods it eats,
/// what each one produces, and its hunger and eating tuning. See docs/behavior/slimes.md.
/// </summary>
public sealed class SlimeSpecies
{
    /// <summary>A food that slimes who eat it want at any hunger level. Code-only fact; see docs/behavior/slimes.md, "Deciding to eat".</summary>
    public const string AlwaysWantedFood = "SPICY_TOFU";
    /// <summary>The "nothing" item id; a diet that produces it eats without making anything (the lucky slime).</summary>
    public const string NoItem = "NONE";

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

    /// <summary>The plorts that turn it into another slime.</summary>
    public IEnumerable<FoodEffect> Transforms => _foods.Values.Where(f => f.Becomes is not null);

    /// <summary>
    /// Builds a species from the install's definition. <paramref name="allIds"/> is every item id
    /// name (from the game's item id list), used to expand the diet's food groups. With
    /// <paramref name="largos"/>, the plorts that turn the slime into a largo or a tarr are added.
    /// </summary>
    public static SlimeSpecies From(SlimeInfo info, IEnumerable<string> allIds, Largos? largos = null)
    {
        if (info.Eating is null || info.Hunger is null || info.Agitation is null)
            throw new InvalidDataException($"{info.Id} has no slime prefab with eating and emotion settings.");
        var ids = allIds as ICollection<string> ?? allIds.ToList();
        var diet = info.Diet;
        var effects = new List<FoodEffect>();

        // One eat rule per diet food and product, so a diet with no products eats nothing.
        if (diet.Produces.Count > 0)
        {
            var foods = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in diet.FoodGroups)
                foods.UnionWith(Items.FoodGroupMembers(group, ids));
            foods.UnionWith(diet.AdditionalFoods);
            var products = diet.Produces.Where(p => p != NoItem).ToList();
            foreach (var food in foods)
            {
                var favorite = diet.Favorites.Contains(food);
                var always = food == AlwaysWantedFood;
                effects.Add(new FoodEffect(food, products, favorite ? diet.FavoriteProductionCount : 1, favorite, always,
                    Driver: always ? FoodDriver.None : FoodDriver.Hunger, ExtraDrive: ExtraDriveFor(food), Rows: diet.Produces.Count));
            }
        }

        // Plorts that transform it (docs/behavior/largos.md).
        if (largos is not null)
        {
            var eaten = effects.Select(e => e.Food).ToHashSet(StringComparer.Ordinal);
            foreach (var plort in ids.Where(Items.IsPlort))
            {
                if (eaten.Contains(plort) || largos.Becomes(info, plort) is not { } becomes)
                    continue;
                effects.Add(new FoodEffect(plort, [], 0, false, false, becomes, FoodDriver.Agitation, Largos.PlortMinDrive, ExtraDriveFor(plort)));
            }
        }
        return new SlimeSpecies(info.Id, info.Name, effects, info.Eating, info.Hunger, info.Agitation);
    }

    private static float ExtraDriveFor(string food) => food == Largos.EagerPlort ? Largos.EagerPlortExtraDrive : 0f;
}
