namespace OpenRanch.Simulation;

/// <summary>What a wandering slime is up to for a while.</summary>
public enum WanderMood
{
    Rest,
    Scoot,
    Hop,
}

/// <summary>
/// The choices and numbers behind how a slime moves and eats by itself: when it bothers going for
/// food, how hard it jumps, how it wanders and how its products come out. The rules are in
/// docs/behavior/slimes.md, "In the world". Tuning that lives on the slime's prefab (search radius,
/// jump strength, speed) is passed in; these constants live only in the game's code and come from
/// static analysis of its slime behaviours (SlimeSubbehaviourPlexer, SlimeRandomMove, GotoConsumable,
/// FindConsumable, SlimeEat).
/// </summary>
public static class SlimeMotion
{
    /// <summary>A slime reconsiders what to do this often.</summary>
    public const float RethinkSeconds = 1f;
    /// <summary>A new slime does nothing for this long.</summary>
    public const float StartDelaySeconds = 3f;
    /// <summary>How often a slime checks whether it stands on something.</summary>
    public const float GroundCheckSeconds = 0.25f;
    /// <summary>How much wandering matters; anything that matters more wins.</summary>
    public const float WanderRelevancy = 0.2f;
    /// <summary>Food seeking matters as much as the drive squared times this.</summary>
    public const float FoodRelevancyFactor = 0.95f;
    /// <summary>A wandering slime picks a new mood and heading this often.</summary>
    public const float WanderMoodSeconds = 10f;
    /// <summary>The new heading is within this many radians of where it faces, either side.</summary>
    public const float WanderTurnRadians = 0.5f;
    /// <summary>A wandering hop's full strength, before mass and the prefab's vertical factor.</summary>
    public const float WanderJump = 6f;
    /// <summary>A wandering slime only hops while slower than this, in m/s.</summary>
    public const float MaxSpeedToHop = 5f;
    /// <summary>The least time between two jumps.</summary>
    public const float SecondsBetweenJumps = 1f;
    /// <summary>A jump toward food leans fully toward it at this distance and more upward when closer.</summary>
    public const float FullLeanDistance = 30f;
    /// <summary>Within this distance a slime slides steadily toward its food; farther out it slides in pulses.</summary>
    public const float SteadyPursuitDistance = 3f;
    /// <summary>The steady slide's push per unit mass (and per unit of the prefab's pursuit speed factor).</summary>
    public const float SteadyPursuitForce = 480f;
    /// <summary>The pulsing slide's push per unit mass, at the middle of a pulse.</summary>
    public const float PulsePursuitForce = 150f;
    /// <summary>The extra push at the bottom of the body that rolls a pulsing slime forward, per unit mass.</summary>
    public const float PulseRollForce = 270f;
    /// <summary>Giving up on food that can't be reached raises agitation by this much.</summary>
    public const float AgitationPerGiveUp = 0.1f;
    /// <summary>The jump strength for going after food when the prefab doesn't set one (GotoConsumable's default).</summary>
    public const float DefaultFoodJump = 12f;
    /// <summary>How long a slime tries to reach food, when the prefab doesn't say (GotoConsumable's default).</summary>
    public const float DefaultAttemptSeconds = 10f;
    /// <summary>How long a slime ignores food after giving up, when the prefab doesn't say (GotoConsumable's default).</summary>
    public const float DefaultGiveUpSeconds = 10f;
    /// <summary>A bite started by touching food takes this long; then the food is gone.</summary>
    public const float BiteSeconds = 0.25f;
    /// <summary>Products appear this far above the slime's centre, along its own up direction.</summary>
    public const float ProduceHeight = 0.5f;
    /// <summary>Products start moving up at this speed, in m/s.</summary>
    public const float ProduceSpeed = 1f;
    /// <summary>Products grow from this fraction of their size...</summary>
    public const float ProduceStartScale = 0.01f;
    /// <summary>...to full size over this long.</summary>
    public const float ProduceGrowSeconds = 0.5f;
    /// <summary>Jumps toward food are at least this fraction of the prefab's jump strength.</summary>
    public const float MinFoodJumpFraction = 0.4f;

    /// <summary>The wander moods and how likely each is when a new one is picked.</summary>
    public static readonly IReadOnlyList<(WanderMood Mood, float Weight)> MoodWeights =
        [(WanderMood.Rest, 0.2f), (WanderMood.Scoot, 0.3f), (WanderMood.Hop, 0.5f)];

    /// <summary>How much going for food matters, for a slime with this drive (its hunger, for ordinary food).</summary>
    public static float FoodRelevancy(float drive) => drive * drive * FoodRelevancyFactor;

    /// <summary>Whether going for food matters more than wandering.</summary>
    public static bool PrefersFood(float drive) => FoodRelevancy(drive) > WanderRelevancy;

    /// <summary>
    /// How hard a slime jumps toward food: <see cref="MinFoodJumpFraction"/> of <paramref name="maxJump"/>
    /// up to the hungry cutoff, rising with the square of the way from there to full hunger.
    /// </summary>
    public static float FoodJumpStrength(float drive, float maxJump)
    {
        var t = Math.Max(0f, drive - Slime.HungryCutoff) / (1f - Slime.HungryCutoff);
        t = Math.Min(1f, t);
        return maxJump * (MinFoodJumpFraction + (1f - MinFoodJumpFraction) * t * t);
    }

    /// <summary>Which food a slime prefers: the higher this score (drive over squared distance), the better.</summary>
    public static float FoodScore(float drive, float distanceSquared) => drive / Math.Max(distanceSquared, 1e-4f);

    /// <summary>How far a jump toward food leans toward it (0 straight up, 1 half up and half toward it).</summary>
    public static float LeanTowardTarget(float distance) => Math.Min(1f, distance / FullLeanDistance);

    /// <summary>The pulse of a pulsing slide, from 0 to 2 and back once a second.</summary>
    public static float Pulse(double seconds) => (float)(Math.Sin(seconds * 2 * Math.PI) + 1);

    /// <summary>Picks a wander mood from a roll in [0, 1).</summary>
    public static WanderMood PickMood(double roll)
    {
        var total = MoodWeights.Sum(m => m.Weight);
        var at = roll * total;
        foreach (var (mood, weight) in MoodWeights)
        {
            if (at < weight)
                return mood;
            at -= weight;
        }
        return MoodWeights[^1].Mood;
    }
}
