namespace OpenRanch.Ranch;

/// <summary>
/// The random draws the produce, crop and coop rules take, so tests can fix them. The original draws
/// from its shared random source (<c>Randoms.SHARED</c>) and Unity's <c>Random</c>; openranch's draws
/// are its own, so the same save doesn't grow the same way twice (UNVERIFIED.md, "Random draws").
/// </summary>
public interface IDraws
{
    /// <summary>A float from <paramref name="low"/> to <paramref name="high"/>; <paramref name="low"/> when they are equal (<c>Randoms.GetInRange(float, float)</c>).</summary>
    float Range(float low, float high);
    /// <summary>A whole number from <paramref name="low"/> up to but not including <paramref name="high"/>; <paramref name="low"/> when they are equal (<c>Randoms.GetInRange(int, int)</c>).</summary>
    int Range(int low, int high);
    /// <summary>True with probability <paramref name="p"/> (<c>Randoms.GetProbability</c>).</summary>
    bool Chance(float p);
}

/// <summary><see cref="IDraws"/> from a <see cref="System.Random"/>.</summary>
public sealed class RandomDraws(Random random) : IDraws
{
    public float Range(float low, float high) => low == high ? low : (float)(low + random.NextDouble() * (high - low));
    public int Range(int low, int high) => low == high ? low : low + random.Next(high - low);
    public bool Chance(float p) => random.NextDouble() < p;
}

/// <summary>The stages of produce (the game's <c>ResourceCycle.State</c>, <see cref="GameEnum.ResourceCycleState"/>), and gone once it has rotted away.</summary>
public enum ProduceStage { Unripe, Ripe, Edible, Rotten, Gone }

/// <summary>
/// How long produce spends in each stage, in game hours, from the <c>ResourceCycle</c> script on its
/// prefab (<c>unripeGameHours</c>, <c>ripeGameHours</c>, <c>edibleGameHours</c>, <c>rottenGameHours</c>), and
/// how long the vacpack must pull at ripe produce before it lets go (<c>releasePrepTime</c>, real seconds).
/// </summary>
public sealed record ProduceTimes(float UnripeHours, float RipeHours, float EdibleHours, float RottenHours, float ReleasePrepSeconds = 1)
{
    /// <summary>Whether produce spawned at <paramref name="spawnTime"/> would be rotten by <paramref name="now"/> (<c>ResourceCycle.WouldProgressToRotten</c>).</summary>
    public bool WouldRotBy(double spawnTime, double now) => now >= spawnTime + (UnripeHours + RipeHours + EdibleHours) * 3600.0;
}

/// <summary>
/// One piece of produce going through its stages on the world clock (static analysis of
/// <c>ResourceCycle</c>, docs/behavior/produce.md): unripe on its crop's joint, ripe (full size,
/// vacuumable), edible (let go of its joint and falls), rotten, then gone. <see cref="ProgressTime"/> is
/// the world time the current stage ends. Each stage after the first lasts its hours from the
/// prefab varied by 0.9 to 1.1 (<c>ResourceCycle.Vary</c>), counted from when the stage before ended,
/// but never from later than now (<c>AdvanceProgressTime</c>).
/// </summary>
public sealed class ProduceCycle(ProduceTimes times, IDraws draws)
{
    /// <summary>A new game's clock starts here; times are never counted from before it (<c>TimeDirector.HoursFromNowOrStart</c>).</summary>
    public const double Start = WorldClock.NewGameStart;

    public ProduceTimes Times { get; } = times;
    public ProduceStage Stage { get; set; } = ProduceStage.Edible;
    public double ProgressTime { get; set; }

    /// <summary>Each stage entered and the world time it began (for checks and reports).</summary>
    public List<(ProduceStage Stage, double At)> History { get; } = [];

    /// <summary>Hours varied as the original varies every stage: times 0.9 to 1.1.</summary>
    public float Vary(float hours) => draws.Range(0.9f, 1.1f) * hours;

    /// <summary>Produce made loose (not grown on a crop): edible until its varied edible hours have passed (<c>ResourceCycle.InitModel</c>).</summary>
    public void InitLoose(double now)
    {
        Stage = ProduceStage.Edible;
        ProgressTime = Math.Max(now, Start) + Vary(Times.EdibleHours) * 3600.0;
        History.Add((Stage, now));
    }

    /// <summary>Produce hung on a crop's joint: unripe for its varied unripe hours (<c>ResourceCycle.Attach</c>).</summary>
    public void Attach(double now)
    {
        Stage = ProduceStage.Unripe;
        ProgressTime = Math.Max(now, Start) + Vary(Times.UnripeHours) * 3600.0;
        History.Add((Stage, now));
    }

    /// <summary>Sets when the current stage ends and moves through every stage already due (<c>ResourceCycle.ProgressResource</c>).</summary>
    public List<ProduceStage> ProgressTo(double stageEnd, double now)
    {
        ProgressTime = stageEnd;
        return Advance(now);
    }

    /// <summary>
    /// Moves through every stage that has ended by <paramref name="now"/>; with <paramref name="release"/>,
    /// ripe produce lets go of its joint now (the vacpack pulled at it long enough). Returns the stages entered.
    /// </summary>
    public List<ProduceStage> Advance(double now, bool release = false)
    {
        var entered = new List<ProduceStage>();
        while (now >= ProgressTime || (release && Stage == ProduceStage.Ripe))
        {
            switch (Stage)
            {
                case ProduceStage.Unripe:
                    Next(ProduceStage.Ripe, Times.RipeHours, now);
                    break;
                case ProduceStage.Ripe:
                    release = false;
                    Next(ProduceStage.Edible, Times.EdibleHours, now);
                    break;
                case ProduceStage.Edible:
                    Next(ProduceStage.Rotten, Times.RottenHours, now);
                    break;
                case ProduceStage.Rotten:
                    Stage = ProduceStage.Gone;
                    History.Add((Stage, Math.Min(now, ProgressTime)));
                    ProgressTime = double.MaxValue;
                    break;
                default:
                    return entered;
            }
            entered.Add(Stage);
        }
        return entered;
    }

    /// <summary>
    /// Brings the stage's end closer by <paramref name="perSecond"/> game seconds for every game second of
    /// <paramref name="worldDelta"/>: a watered crop's produce on its joint ripens half again as fast
    /// (<c>ResourceCycle.RegistryUpdate</c> with <c>SpawnResource.AdditionalRipenessPerSecond</c>).
    /// </summary>
    public void Hasten(float perSecond, double worldDelta) => ProgressTime -= perSecond * worldDelta;

    private void Next(ProduceStage stage, float hours, double now)
    {
        Stage = stage;
        var began = Math.Min(now, ProgressTime);
        History.Add((stage, began));
        ProgressTime = began + Vary(hours) * 3600.0;
    }
}

/// <summary>
/// A crop's <c>SpawnResource</c> script: what it grows, how many at a time and how often.
/// <paramref name="Produce"/> and <paramref name="Bonus"/> are item ids (<see cref="GameEnum.ItemId"/>
/// names) of the prefabs in <c>ObjectsToSpawn</c> and <c>BonusObjectsToSpawn</c>; the other fields
/// carry the script's own names.
/// </summary>
public sealed record CropSpawnRules(
    IReadOnlyList<string> Produce,
    IReadOnlyList<string> Bonus,
    float MinObjects,
    float MaxObjects,
    float MinNutrientObjects,
    float MinIntervalHours,
    float MaxIntervalHours,
    float BonusChance,
    int MinBonusSelections,
    int MaxActiveSpawns,
    int MaxTotalSpawns,
    bool ForceDestroyLeftovers,
    bool ForceFirstRipeness = false);

/// <summary>A batch a crop's clock asks for: produce that started growing at <paramref name="SpawnAt"/>, or (with <paramref name="Ripe"/>) produce that is ripe at once.</summary>
public readonly record struct SpawnRequest(double? SpawnAt, bool Ripe = false);

/// <summary>
/// When a crop grows its next batch of produce, and how many (static analysis of <c>SpawnResource</c>,
/// docs/behavior/produce.md). The clock counts down to <see cref="NextSpawnTime"/>; a watered crop (a
/// sprinkler, or stored water) counts half again as fast. When it is reached the crop grows a batch on
/// its free joints and the next batch is due 18 to 24 game hours later (the script's interval). A crop
/// on a land plot that was away for a while (a quarter of an hour or more overdue) catches up, batch
/// by batch, leaving out batches that would have rotted already.
/// </summary>
public sealed class CropSpawnClock(CropSpawnRules rules, ProduceTimes first, IDraws draws, bool onPlot)
{
    /// <summary>Water a crop uses per game hour (<c>SpawnResource.WATER_USED_PER_HOUR</c>, 3/23: a full store lasts 23 hours).</summary>
    public const float WaterUsedPerHour = 0.13043478f;
    /// <summary>The most water a crop stores (<c>SpawnResource.MAX_WATER_STORED</c>).</summary>
    public const float MaxWater = 3f;
    /// <summary>How much faster a watered crop's clock and its hanging produce run: half a second more per second.</summary>
    public const float WateredRipenessPerSecond = 0.5f;
    /// <summary>How overdue (game hours) a crop on a plot must be to catch up batch by batch (the code's 0.25).</summary>
    public const float CatchUpHours = 0.25f;
    /// <summary>A new game's crops start between 3 and 9 game hours into their first interval (<c>SpawnResource.MIN/MAX_BONUS_RIPENESS</c>).</summary>
    public const float MinHeadStartHours = 3f, MaxHeadStartHours = 9f;

    public CropSpawnRules Rules { get; } = rules;
    /// <summary>The world time the next batch grows; 0 until <see cref="Init"/>.</summary>
    public double NextSpawnTime { get; set; }
    public bool NextSpawnRipens { get; set; }
    /// <summary>Water stored, 0 to <see cref="MaxWater"/>.</summary>
    public float StoredWater { get; set; }
    /// <summary>How many more batches the crop may grow before it is used up, when its rules limit it (<c>MaxTotalSpawns</c>).</summary>
    public int TotalSpawnsRemaining { get; set; } = rules.MaxTotalSpawns;

    /// <summary>
    /// A crop seen for the first time (<c>SpawnResource.SetModel</c> with no saved spawn time): one that
    /// grows ripe the first time does so at once; on a new game's first moment, the crop is 3 to 9 hours
    /// into its interval; otherwise it grows now.
    /// </summary>
    public void Init(double now)
    {
        if (NextSpawnTime != 0)
            return;
        if (Rules.ForceFirstRipeness)
            NextSpawnRipens = true;
        else if (now == ProduceCycle.Start)
            NextSpawnTime = Math.Max(now, ProduceCycle.Start) - draws.Range(MinHeadStartHours, MaxHeadStartHours) * 3600.0;
        else
            NextSpawnTime = now;
    }

    public static bool IsWatered(bool sprinkler, float storedWater) => sprinkler || storedWater > 0;

    /// <summary>How much faster the crop's clock runs now (<c>SpawnResource.AdditionalRipenessPerSecond</c>).</summary>
    public float RipenessPerSecond(bool sprinkler) => IsWatered(sprinkler, StoredWater) ? WateredRipenessPerSecond : 0;

    /// <summary>
    /// Moves the clock to <paramref name="now"/>, <paramref name="worldDelta"/> game seconds after the last
    /// update, and returns the batches due (<c>SpawnResource.UpdateToTime</c>). <paramref name="precipitation"/>
    /// is the rain falling on it (water per hour).
    /// </summary>
    public List<SpawnRequest> Update(double now, double worldDelta, bool sprinkler, bool miracleMix, float precipitation = 0, bool blocked = false)
    {
        var due = new List<SpawnRequest>();
        StoredWater = Math.Clamp(StoredWater + (float)((precipitation - WaterUsedPerHour) * worldDelta / 3600.0), 0, MaxWater);
        if (blocked || (Rules.MaxTotalSpawns != 0 && TotalSpawnsRemaining <= 0))
            return due;
        NextSpawnTime -= RipenessPerSecond(sprinkler) * worldDelta;
        if (now < NextSpawnTime)
            return due;
        var watered = IsWatered(sprinkler, StoredWater);
        var overdue = (float)((now - NextSpawnTime) / 3600.0);
        if (onPlot && overdue >= CatchUpHours)
        {
            // Batches older than a whole life on the crop and off it would have rotted away: left out.
            var onCrop = first.UnripeHours + first.RipeHours;
            var offCrop = first.EdibleHours + first.RottenHours;
            if (miracleMix)
                offCrop *= 2;
            while (overdue >= 0)
            {
                if (overdue < onCrop + offCrop)
                    due.Add(new SpawnRequest(NextSpawnTime));
                var step = Math.Max(1f, Interval() * (watered ? 0.5f : 1f));
                NextSpawnTime += step * 3600.0;
                overdue -= step;
            }
        }
        else if (NextSpawnRipens)
        {
            due.Add(new SpawnRequest(null, Ripe: true));
            NextSpawnTime = now + Interval() * 3600.0;
            NextSpawnRipens = false;
        }
        else
        {
            // The batch started growing at the last interval boundary before now.
            var at = NextSpawnTime;
            var step = Interval() * (watered ? 0.5f : 1f) * 3600.0;
            while (at + step < now)
                at += step;
            due.Add(new SpawnRequest(at));
            NextSpawnTime = now + Interval() * 3600.0;
        }
        return due;
    }

    private float Interval() => draws.Range(Rules.MinIntervalHours, Rules.MaxIntervalHours);

    /// <summary>
    /// The produce (item ids) one batch grows (<c>SpawnResource.GetSpawnMetadatas</c>): a whole number
    /// drawn from the minimum (the rich soil's minimum on mineral soil) to the maximum, each a bonus item
    /// for the first <c>minBonusSelections</c> or by <c>BonusChance</c>; none while <paramref name="active"/>
    /// spawns already reach the crop's cap, and none of a batch that would already have rotted.
    /// </summary>
    public List<string> Batch(SpawnRequest request, double now, bool richSoil, int active, Func<string, ProduceTimes?> timesOf)
    {
        var batch = new List<string>();
        if (Rules.MaxActiveSpawns != 0 && active >= Rules.MaxActiveSpawns)
            return batch;
        var count = (int)draws.Range(richSoil ? Rules.MinNutrientObjects : Rules.MinObjects, Rules.MaxObjects);
        for (var i = 0; i < count; i++)
        {
            var bonus = Rules.Bonus.Count >= 1 && (i < Rules.MinBonusSelections || draws.Chance(Rules.BonusChance));
            var from = bonus ? Rules.Bonus : Rules.Produce;
            if (from.Count == 0)
                continue;
            // The original picks with an upper bound one short of the list's length, so with two or more
            // choices the last is never picked (Randoms.GetInRange(0, Length - 1) excludes its top).
            var item = from[draws.Range(0, from.Count - 1)];
            if (request.SpawnAt is { } at && timesOf(item) is { } t && t.WouldRotBy(at, now))
                continue;
            batch.Add(item);
        }
        if (Rules.MaxTotalSpawns != 0)
            TotalSpawnsRemaining -= batch.Count;
        return batch;
    }

    /// <summary>
    /// When a batch's produce ends its unripe stage: grown at the request's time, unripe for its
    /// prefab's unripe hours unvaried (<c>SpawnResource.Spawn</c>); a ripe batch ripens now. Null for a
    /// batch the crop grows as it happens (ripening on its own varied clock from now).
    /// </summary>
    public static double? UnripeEnds(SpawnRequest request, ProduceTimes times, double now) =>
        request.Ripe ? now : request.SpawnAt is { } at ? at + times.UnripeHours * 3600.0 : null;
}
