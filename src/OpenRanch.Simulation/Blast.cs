namespace OpenRanch.Simulation;

/// <summary>
/// An explosion (a boom slime's, a boom gordo's burst, a feral largo's stomp): every body within
/// the radius is pushed away from the centre, and the player also takes damage. Power, radius and the
/// damage range come from the exploding thing's prefab; how they combine lives in the game's code
/// (static analysis of PhysicsUtil.Explode). Rules in docs/behavior/slime-abilities.md, "Explosions".
/// </summary>
public static class Blast
{
    /// <summary>A body this close to the centre is pushed as hard as one this far out. Code-only.</summary>
    public const float NearestPush = 2f;
    /// <summary>The player's push is scaled down by this much (the player controller takes it as a velocity change). Code-only.</summary>
    public const float PlayerPushFactor = 0.001f;

    /// <summary>
    /// The push on a body (not the player) at <paramref name="distance"/> from the centre: a force of
    /// power × (1 − max(2, distance) ÷ radius)², applied once, away from the centre. Negative beyond
    /// the radius's reach of the formula, so callers only use it inside the radius.
    /// </summary>
    public static float BodyPush(float power, float radius, float distance)
    {
        var f = 1f - Math.Max(NearestPush, distance) / radius;
        return power * f * f;
    }

    /// <summary>The push on the player: power × (1 − distance ÷ radius) × 0.001.</summary>
    public static float PlayerPush(float power, float radius, float distance) =>
        power * (1f - distance / radius) * PlayerPushFactor;

    /// <summary>The player's health loss: from the maximum at the centre to the minimum at the edge, rounded half to even.</summary>
    public static int PlayerDamage(float minDamage, float maxDamage, float radius, float distance)
    {
        var t = Math.Clamp(1f - distance / radius, 0f, 1f);
        return (int)MathF.Round(minDamage + (maxDamage - minDamage) * t, MidpointRounding.ToEven);
    }
}

/// <summary>
/// The player's health and radiation, as far as slimes need them: a stand-in until openranch has a
/// player with a health bar (UNVERIFIED.md). Starting values are the original's new-game defaults
/// (static analysis of PlayerModel); damage and radiation follow PlayerState and PlayerModel.
/// </summary>
public sealed class PlayerVitals
{
    /// <summary>A new game's maximum health and radiation. Code-only (PlayerModel).</summary>
    public const int StartMaxHealth = 100, StartMaxRads = 100;
    /// <summary>Radiation over the maximum turns into health loss in steps of this many. Code-only.</summary>
    public const int RadOverflowStep = 10;

    public int MaxHealth { get; set; } = StartMaxHealth;
    public float Health { get; set; } = StartMaxHealth;
    public int MaxRads { get; set; } = StartMaxRads;
    public float Rads { get; set; }

    /// <summary>Raised for every hit: the health lost and what dealt it.</summary>
    public event Action<int, string>? Damaged;

    /// <summary>Takes <paramref name="loss"/> health; returns whether that killed the player (health at 0).</summary>
    public bool Damage(int loss, string source)
    {
        Health -= loss;
        Damaged?.Invoke(loss, source);
        if (Health > 0)
            return false;
        Health = 0;
        return true;
    }

    /// <summary>
    /// Adds radiation; whatever goes over the maximum is taken off in whole steps of 10 and returned
    /// as health to lose (PlayerModel.AddRads).
    /// </summary>
    public int AddRads(float rads)
    {
        Rads += rads;
        if (Rads <= MaxRads)
            return 0;
        var steps = (int)MathF.Floor((Rads - MaxRads) / RadOverflowStep);
        if (steps <= 0)
            return 0;
        Rads -= steps * RadOverflowStep;
        return steps * RadOverflowStep;
    }
}
