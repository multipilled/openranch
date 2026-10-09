namespace OpenRanch.Simulation;

/// <summary>
/// What happened when a slime ate: the items it will produce once it has digested, or the slime it
/// turns into at once (<see cref="Becomes"/>, a largo or a tarr; docs/behavior/largos.md).
/// </summary>
public sealed record Meal(string Food, IReadOnlyList<string> Produced, bool WasFavorite, string? Becomes = null)
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

    /// <summary>
    /// Feral (docs/behavior/feral-slimes.md): it eats anything in its diet that touches it, however
    /// full or calm it is (static analysis of SlimeEat).
    /// </summary>
    public bool IsFeral { get; set; }

    /// <summary>Hungry enough to go looking for food (always, when feral).</summary>
    public bool WantsToEat => IsFeral || (Species.Foods.Count > 0 && Hunger > Species.Eating.MinDriveToEat);

    /// <summary>Whether the slime would eat <paramref name="food"/> right now.</summary>
    public bool WillEat(string food) =>
        Species.FoodEffect(food) is { } effect && (IsFeral || Drive(effect) >= Species.Eating.MinDriveToEat);

    /// <summary>
    /// How much the slime wants a food right now: the food's feeling (hunger, agitation, or 1 for
    /// neither), raised to the food's floor, plus its extra drive (docs/behavior/largos.md).
    /// </summary>
    public float Drive(FoodEffect effect)
    {
        var feeling = effect.Driver switch
        {
            FoodDriver.Agitation => Agitation,
            FoodDriver.None => 1f,
            _ => effect.IgnoresHunger ? 1f : Hunger,
        };
        return Math.Max(0f, Math.Max(effect.MinDrive, feeling) + effect.ExtraDrive);
    }

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
    /// it produces after <see cref="DigestSeconds"/>. <paramref name="swallowed"/> is false when the bite
    /// only hurt the food (a slime with health left, bitten by a tarr): the bite still counts for hunger
    /// and agitation, but nothing comes out (static analysis of SlimeEat). <paramref name="ignoreMood"/>
    /// feeds it anything in its diet however full or calm (a chomp that ignores emotions).
    /// </summary>
    public Meal Feed(string food, bool swallowed = true, bool ignoreMood = false)
    {
        if (ignoreMood ? Species.FoodEffect(food) is null : !WillEat(food))
            return Meal.None;
        var effect = Species.FoodEffect(food)!;
        // Every eat rule that matches the food counts as a meal: a largo's food matches one rule per
        // plort, so it is fed (and calmed) twice. Static analysis of SlimeEat; docs/behavior/largos.md.
        var calm = effect.IsFavorite ? Species.Eating.AgitationPerFavoriteEat : Species.Eating.AgitationPerEat;
        for (var row = 0; row < Math.Max(1, effect.Rows); row++)
        {
            if (effect.Driver == FoodDriver.Agitation)
                Agitation = Math.Clamp(Agitation - Species.Eating.DrivePerEat, 0f, 1f);
            else if (effect.Driver == FoodDriver.Hunger && !effect.IgnoresHunger)
                Hunger = Math.Clamp(Hunger - Species.Eating.DrivePerEat, 0f, 1f);
            Agitation = Math.Clamp(Agitation - calm, 0f, 1f);
        }
        if (effect.Becomes is { } becomes)
            return new Meal(food, [], false, becomes);
        if (!swallowed)
            return new Meal(food, [], effect.IsFavorite);

        // Each product is its own rule, so each may be skipped on its own.
        var skip = Species.Eating.ChanceToSkipProduce;
        var produced = new List<string>();
        foreach (var item in effect.Produces)
        {
            if (skip > 0 && _random.NextDouble() < skip)
                continue;
            for (var i = 0; i < effect.CountEach; i++)
                produced.Add(item);
        }
        return new Meal(food, produced, effect.IsFavorite);
    }
}
