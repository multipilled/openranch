namespace OpenRanch.Ranch;

/// <summary>
/// A hen's <c>Reproduce</c> script: the mate it needs near (<paramref name="MateId"/>, within
/// <paramref name="MaxDistToMate"/> m), the animals that crowd it (<paramref name="DensityIds"/>, counted
/// within <paramref name="DensityDist"/> m, at most <paramref name="MaxDensity"/>, times
/// <paramref name="DeluxeDensityFactor"/> in a deluxe coop), what it lays (<paramref name="ChildId"/>)
/// and how often (<paramref name="MinHours"/> to <paramref name="MaxHours"/> game hours). Ids are
/// <see cref="GameEnum.ItemId"/> names. See docs/behavior/coops.md.
/// </summary>
public sealed record ReproduceRules(
    string? MateId,
    float MaxDistToMate,
    IReadOnlyList<string> DensityIds,
    float DensityDist,
    int MaxDensity,
    string ChildId,
    float MinHours,
    float MaxHours,
    float DeluxeDensityFactor)
{
    /// <summary>The chance a hen outside a coop lays when her time comes, and the vitamizer's chance of a second chick (the code's 0.5).</summary>
    public const float OutsideCoopChance = 0.5f, VitamizerTwinChance = 0.5f;

    /// <summary>Game hours to her next laying (<c>Reproduce.ReproducePeriod</c>).</summary>
    public float Period(IDraws draws) => draws.Range(MinHours, MaxHours);

    /// <summary>The crowd a hen tolerates: more in a deluxe coop, rounded half to even as Unity's <c>Mathf.RoundToInt</c> does.</summary>
    public int MaxDensityIn(bool deluxeCoop) => deluxeCoop ? (int)Math.Round(MaxDensity * DeluxeDensityFactor, MidpointRounding.ToEven) : MaxDensity;

    /// <summary>
    /// How many chicks a hen lays when her time comes (<c>Reproduce.RegistryUpdate</c>): none without a
    /// mate near or with more than her tolerated crowd around; in a coop always one, outside it one half
    /// the time; in a vitamizer's reach, half the time one more.
    /// </summary>
    public int Lays(bool mateNear, int crowd, bool inCoop, bool deluxeCoop, bool inVitamizer, IDraws draws)
    {
        if (!mateNear || crowd > MaxDensityIn(deluxeCoop))
            return 0;
        if (!inCoop && !draws.Chance(OutsideCoopChance))
            return 0;
        return inVitamizer && draws.Chance(VitamizerTwinChance) ? 2 : 1;
    }
}

/// <summary>
/// A <c>TransformAfterTime</c> script (a chick growing up): after <paramref name="DelayHours"/> game hours
/// it turns into one of <paramref name="Options"/> (item id names), picked by weight. Near a feeder the
/// time runs twice as fast. See docs/behavior/coops.md.
/// </summary>
public sealed record TransformRules(float DelayHours, IReadOnlyList<(string Id, float Weight)> Options)
{
    /// <summary>
    /// What it turns into (<c>Randoms.Pick</c> over a weight map: each option with a positive weight
    /// replaces the pick so far with probability weight / running total). A later option for the same
    /// prefab replaces the earlier one's weight, as the original's dictionary does.
    /// </summary>
    public string? Pick(IDraws draws)
    {
        var weights = new List<(string Id, float Weight)>();
        foreach (var (id, weight) in Options)
        {
            var i = weights.FindIndex(w => w.Id == id);
            if (i >= 0)
                weights[i] = (id, weight);
            else
                weights.Add((id, weight));
        }
        string? result = null;
        var total = 0f;
        foreach (var (id, weight) in weights)
        {
            if (weight <= 0)
                continue;
            total += weight;
            if (total == weight || draws.Range(0f, total) < weight)
                result = id;
        }
        return result;
    }
}
