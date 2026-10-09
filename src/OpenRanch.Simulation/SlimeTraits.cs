namespace OpenRanch.Simulation;

/// <summary>
/// Numbers behind the species traits that need nothing but the slime itself: hovering (phosphor
/// slimes, the prefab's <c>SlimeHover</c>) and rolling (rock slimes, <c>RockSlimeRoll</c>). A slime
/// has a trait when its prefab carries the component; these constants live only in the game's code
/// (static analysis of those two behaviours). Rules in docs/behavior/slime-traits.md.
/// </summary>
public static class SlimeTraits
{
    /// <summary>How much hovering or rolling matters when it is due (more than wandering, less than real hunger).</summary>
    public const float TraitRelevancy = 0.3f;

    /// <summary>A hover lasts this long, unless the slime bumps its top on something solid.</summary>
    public const float HoverSeconds = 6f;
    /// <summary>The hover lifts the slime toward this height above the ground below it.</summary>
    public const float HoverHeight = 5f;
    /// <summary>Upward push per unit mass at ground level, shrinking to nothing at <see cref="HoverHeight"/>.</summary>
    public const float HoverLift = 1200f;
    /// <summary>Sideways drift push per unit mass, in a random flat direction picked when the hover starts.</summary>
    public const float HoverDrift = 100f;
    /// <summary>Hovers start between these many seconds apart: the longer the calmer the slime.</summary>
    public const float HoverMinDelay = 10f, HoverMaxDelay = 25f;
    /// <summary>A touch on something solid (not an item) this far above the centre, times the slime's scale, ends the hover.</summary>
    public const float HoverCeilingHeight = 0.25f;

    /// <summary>A roll first spins in place this long...</summary>
    public const float RollSpinSeconds = 1f;
    /// <summary>...then rolls forward this long.</summary>
    public const float RollSeconds = 2f;
    /// <summary>Spin per unit mass about the slime's flat right-hand axis while rolling.</summary>
    public const float RollTorque = 1200f;
    /// <summary>Forward push per unit mass while rolling.</summary>
    public const float RollForce = 720f;
    /// <summary>Rolls start between these many seconds apart: the longer the calmer the slime.</summary>
    public const float RollMinDelay = 3f, RollMaxDelay = 15f;

    /// <summary>The random spread added to calmness when picking a delay.</summary>
    public const float DelayJitter = 0.1f;

    /// <summary>
    /// Seconds until the next hover or roll: from <paramref name="min"/> for a fully agitated slime to
    /// <paramref name="max"/> for a calm one. <paramref name="roll"/> is a random number in [0, 1) that
    /// adds up to <see cref="DelayJitter"/> either way to the calmness.
    /// </summary>
    public static float Delay(float min, float max, float agitation, double roll)
    {
        var calm = (float)(roll * 2 - 1) * DelayJitter + (1f - agitation);
        return min + (max - min) * Math.Clamp(calm, 0f, 1f);
    }

    /// <summary>Upward hover push per unit mass and per physics step's length, at <paramref name="height"/> above the ground.</summary>
    public static float HoverLiftAt(float height) => HoverLift * (1f - height / HoverHeight);
}
