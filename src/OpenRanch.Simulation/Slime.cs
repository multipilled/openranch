namespace OpenRanch.Simulation;

/// <summary>What happened when a slime ate: the items it will produce once it has digested.</summary>
public sealed record Meal(string Food, IReadOnlyList<string> Produced, bool WasFavorite)
{
    public static readonly Meal None = new("", [], false);
}

/// <summary>
/// One slime's appetite and mood over game time. Hunger and agitation are 0 to 1. The rules are in
/// docs/behavior/slimes.md; tuning comes from the slime's species (read from the install).
/// </summary>
public sealed class Slime
{
    // Thresholds that live only in the game's code. See docs/behavior/slimes.md, "Hunger".
    public const float HungryCutoff = 0.666f;
    public const float StarvingCutoff = 0.99f;
    public const float AngryCutoff = 0.9f;
    /// <summary>How fast a starving slime's agitation is pushed up, per game hour.</summary>
    public const float StarvingAgitationPerHour = 0.416667f;
    /// <summary>Seconds between swallowing and the plorts popping out.</summary>
    public const float DigestSeconds = 2f;

    private readonly Random _random;

    public Slime(SlimeSpecies species, Random? random = null)
    {
        Species = species;
        _random = random ?? Random.Shared;
        Hunger = species.Hunger.Start;
        Agitation = species.Agitation.Start;
    }

    public SlimeSpecies Species { get; }
    public float Hunger { get; set; }
    public float Agitation { get; set; }

    public bool IsHungry => Hunger >= HungryCutoff;
    public bool IsStarving => Hunger >= StarvingCutoff;
    public bool IsAngry => Agitation >= AngryCutoff;

    /// <summary>Hungry enough to go looking for food.</summary>
    public bool WantsToEat => Species.Foods.Count > 0 && Hunger > Species.Eating.MinDriveToEat;

    /// <summary>Whether the slime would eat <paramref name="food"/> right now.</summary>
    public bool WillEat(string food) =>
        Species.FoodEffect(food) is { } effect && Drive(effect) >= Species.Eating.MinDriveToEat;

    private float Drive(FoodEffect effect) => effect.IgnoresHunger ? 1f : Hunger;

    /// <summary>Lets <paramref name="hours"/> of game time pass.</summary>
    public void Advance(double hours)
    {
        if (hours <= 0)
            return;
        Hunger = Drift(Hunger, Species.Hunger.Rest, Species.Hunger.DriftPerGameHour, hours);
        var agitationRate = Species.Agitation.DriftPerGameHour - (IsStarving ? StarvingAgitationPerHour : 0f);
        Agitation = Drift(Agitation, Species.Agitation.Rest, agitationRate, hours);
    }

    // Moves toward rest at rate per hour; a negative rate pushes away from rest instead.
    private static float Drift(float value, float rest, float rate, double hours)
    {
        var step = (float)(Math.Abs(rate) * hours);
        if (rate >= 0)
            value = value > rest ? Math.Max(rest, value - step) : Math.Min(rest, value + step);
        else
            value = value > rest || rest == 0f ? value + step : value - step;
        return Math.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// Feeds the slime. Returns <see cref="Meal.None"/> if it won't eat that now; otherwise the items
    /// it produces after <see cref="DigestSeconds"/>.
    /// </summary>
    public Meal Feed(string food)
    {
        if (!WillEat(food))
            return Meal.None;
        var effect = Species.FoodEffect(food)!;
        if (!effect.IgnoresHunger)
            Hunger = Math.Clamp(Hunger - Species.Eating.DrivePerEat, 0f, 1f);
        var calm = effect.IsFavorite ? Species.Eating.AgitationPerFavoriteEat : Species.Eating.AgitationPerEat;
        Agitation = Math.Clamp(Agitation - calm, 0f, 1f);

        var skip = Species.Eating.ChanceToSkipProduce;
        if (skip > 0 && _random.NextDouble() < skip)
            return new Meal(food, [], effect.IsFavorite);
        var produced = new List<string>();
        foreach (var item in effect.Produces)
            for (var i = 0; i < effect.CountEach; i++)
                produced.Add(item);
        return new Meal(food, produced, effect.IsFavorite);
    }
}
