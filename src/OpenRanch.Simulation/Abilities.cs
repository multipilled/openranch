namespace OpenRanch.Simulation;

// The code-only numbers behind the slime abilities (docs/behavior/slime-abilities.md). Each class
// names the component it comes from (static analysis); values a prefab carries are read from the
// install instead and aren't repeated here.

/// <summary>Picking the wait before an ability fires again: from <paramref name="max"/> for a calm slime to <paramref name="min"/> for a fully agitated one, ±0.1 calmness (the same rule as hovering and rolling).</summary>
public static class AbilityDelay
{
    public static float Pick(float min, float max, float agitation, double roll) => SlimeTraits.Delay(min, max, agitation, roll);
}

/// <summary>Boom slimes' explosion (<c>BoomSlimeExplode</c>). Power, radius and damage are on the prefab.</summary>
public static class BoomSlime
{
    /// <summary>Seconds between explosions: 10 (agitated) to 45 (calm).</summary>
    public const float MinDelay = 10f, MaxDelay = 45f;
    /// <summary>The first wait is a random 25-100% of a picked delay.</summary>
    public const float FirstDelayMinFraction = 0.25f;
    /// <summary>It grimaces this long before it blows...</summary>
    public const float PrepSeconds = 1.5f;
    /// <summary>...and is frazzled this long after (no rethinking either time).</summary>
    public const float RecoverySeconds = 5f;
    /// <summary>When due, exploding matters this much (it beats anything but another must-do).</summary>
    public const float Relevancy = 1f;
}

/// <summary>Rad slimes' aura (<c>RadSlimeExpand</c>; the radiation per second is on the prefab's <c>RadSource</c>).</summary>
public static class RadSlime
{
    /// <summary>Seconds between expansions: 30 (agitated) to 180 (calm).</summary>
    public const float MinDelay = 30f, MaxDelay = 180f;
    /// <summary>The aura swells for 3 s (no rethinking), stays big 10 s, then shrinks back.</summary>
    public const float ExpandingSeconds = 3f, ExpandedSeconds = 10f;
    /// <summary>The aura's size while swelling or swollen, times its normal size.</summary>
    public const float ExpandFactor = 1.5f;
    /// <summary>Growing toward a bigger size takes this long per unit of scale; shrinking (or anything below normal) takes <see cref="ShrinkSeconds"/>.</summary>
    public const float GrowSeconds = 3f, ShrinkSeconds = 0.2f;
    public const float Relevancy = 1f;

    /// <summary>One physics step of the aura's scale moving toward <paramref name="target"/>.</summary>
    public static float StepScale(float current, float target, float delta)
    {
        var rate = target >= 1f && current >= 1f ? delta / GrowSeconds : delta / ShrinkSeconds;
        return target > current ? Math.Min(current + rate, target) : target < current ? Math.Max(current - rate, target) : current;
    }
}

/// <summary>Crystal slimes' launch and spikes (<c>CrystalSlimeLaunch</c>; spikes' lifetime and damage are on the spike prefabs).</summary>
public static class CrystalSlime
{
    /// <summary>Game hours between launches: 0.05 (agitated) to 0.25 (calm).</summary>
    public const float MinDelayHours = 0.05f, MaxDelayHours = 0.25f;
    /// <summary>It curls up for a game minute before launching...</summary>
    public const float PrepHours = 1f / 60f;
    /// <summary>...and rolls at least this long after (6 game seconds).</summary>
    public const float LaunchedHours = 0.0016666668f;
    /// <summary>The launch: a one-step force of 200 straight up (not scaled by mass) plus 40 × mass forward.</summary>
    public const float LaunchUp = 200f, LaunchForward = 40f;
    /// <summary>While launched it spins like a rolling rock: 1200 × mass about its flat right-hand axis.</summary>
    public const float RollTorque = 1200f;
    /// <summary>Small spikes come 0.2 s after the big one, in a ring this wide.</summary>
    public const float SmallSpikeDelay = 0.2f, SpikeRing = 1.5f;
    /// <summary>How many small spikes: ⌈4 × mass⌉ up to (not including) ⌈7 × mass⌉.</summary>
    public const float MinSmallPerMass = 4f, MaxSmallPerMass = 7f;
    /// <summary>A spike stands on the ground found within this far below its spot.</summary>
    public const float SpikeDropRay = 2f;
    public const float Relevancy = 0.3f;

    public static (int Min, int MaxExclusive) SmallSpikes(float mass) => ((int)MathF.Ceiling(MinSmallPerMass * mass), (int)MathF.Ceiling(MaxSmallPerMass * mass));
}

/// <summary>Quantum slimes' ghosts (qubits) and jumping into them (<c>GenerateQuantumQubit</c>, <c>QuantumSlimeSuperposition</c>, <c>QuantumVibration</c>).</summary>
public static class QuantumSlime
{
    /// <summary>A qubit's radius; its spot must be clear of anything this big.</summary>
    public const float QubitRadius = 0.61f, ClearRadius = 0.6f;
    /// <summary>Placing tries this many spots, then waits for the next generation time.</summary>
    public const int MaxGenerationAttempts = 5;
    public const float Relevancy = 1f;

    /// <summary>How strongly it vibrates (0-1): nothing at or below the cutoff, then rising to 1 at full agitation.</summary>
    public static float Vibration(float agitation, float cutoff) =>
        agitation <= cutoff ? 0f : Math.Clamp(agitation / (1f - cutoff) - cutoff / (1f - cutoff), 0f, 1f);

    /// <summary>A delay that shrinks from <paramref name="max"/> (calm) to <paramref name="min"/> (agitated), straight by agitation.</summary>
    public static float Delay(float min, float max, float agitation) => max + (min - max) * Math.Clamp(agitation, 0f, 1f);
}

/// <summary>Dervish slimes' spin (<c>DervishSlimeSpin</c>, a stronger hover) and whirlwinds.</summary>
public static class DervishSlime
{
    /// <summary>Lift per unit mass at ground level (ordinary hovering uses 1200).</summary>
    public const float HoverLift = 600f;
    /// <summary>It spins up toward this height (a largo toward <see cref="LargoHoverHeight"/>).</summary>
    public const float HoverHeight = 5f, LargoHoverHeight = 9f;
    /// <summary>At or above this agitation a spin also sets a whirlwind loose.</summary>
    public const float WhirlwindAgitation = 0.95f;
    /// <summary>At most this many whirlwinds from one slime; a seventh ends the oldest.</summary>
    public const int MaxWhirlwinds = 6;

    public static float LiftAt(float height, bool largo)
    {
        var top = largo ? LargoHoverHeight : HoverHeight;
        return HoverLift * (1f - height / top);
    }
}

/// <summary>Tangle slimes' vines and pollen (<c>GroundVine</c>, <c>PollenCloudController</c>; tuning on the prefab).</summary>
public static class TangleSlime
{
    /// <summary>A full-height vine is this tall; it reaches 3-4 m above the food it grabs...</summary>
    public const float FullVineHeight = 4f, MinExtraHeight = 3f, MaxExtraHeight = 4f;
    /// <summary>...grows at this many seconds per full height, and lifts the food 2-2.5 m in front of the slime's mouth.</summary>
    public const float FullVineSeconds = 0.75f, MinEatHeight = 2f, MaxEatHeight = 2.5f;
    /// <summary>A pollen cloud is let go once it reaches 95% of full size; it drifts forward at 1 m/s.</summary>
    public const float ReleaseFraction = 0.95f, CloudSpeed = 1f;

    /// <summary>How big the pollen cloud wants to be (0-1): nothing until the start agitation, then rising to 1 at full agitation.</summary>
    public static float CloudTarget(float agitation, float startAgitation) =>
        Math.Max(0f, (agitation - startAgitation) / (1f - startAgitation));
}

/// <summary>Hunter slimes' cloaking (<c>SlimeStealth</c>).</summary>
public static class HunterSlime
{
    /// <summary>Freshly appeared hunters stay invisible this long.</summary>
    public const float InitialStealthSeconds = 5f;
    /// <summary>Opacity changes this much per second toward its target (0 cloaked, 1 seen).</summary>
    public const float OpacityPerSecond = 2f;
}

/// <summary>Mosaic slimes' glints (<c>GlintController</c>).</summary>
public static class MosaicSlime
{
    /// <summary>It checks its glints every 10 game minutes (1/6 h).</summary>
    public const float UpdateHours = 1f / 6f;
    /// <summary>A new glint every half game hour when calm, down to a fifth of that at full agitation.</summary>
    public const float SpawnHours = 0.5f;
    /// <summary>Glints appear within 7.5 (calm) to 30 m (agitated) of it, never below it.</summary>
    public const float MinSpawnRadius = 7.5f, MaxSpawnRadius = 30f;

    /// <summary>Agitation shortens times: hours × (1 − 0.8 × agitation).</summary>
    public static float AdjustHours(float hours, float agitation) => hours * (1f - 0.8f * agitation);
    public static float SpawnRadius(float agitation) => MinSpawnRadius + (MaxSpawnRadius - MinSpawnRadius) * Math.Clamp(agitation, 0f, 1f);
}
