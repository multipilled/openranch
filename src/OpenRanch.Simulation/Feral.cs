namespace OpenRanch.Simulation;

/// <summary>
/// A slime's feral settings, read from its prefab's <c>SlimeFeral</c> component. Only slimes that
/// aren't of normal vacuum size (largos) can be feral: on a normal-sized slime the component removes
/// itself (static analysis of SlimeFeral).
/// </summary>
/// <param name="DynamicToFeral">Turns feral by itself once fully agitated (<c>dynamicToFeral</c>).</param>
/// <param name="DynamicFromFeral">Calms down when it eats (<c>dynamicFromFeral</c>).</param>
/// <param name="LifetimeHours">Game hours a feral slime lasts before it poofs (<c>feralLifetimeHours</c>).</param>
public sealed record FeralSettings(bool DynamicToFeral, bool DynamicFromFeral, float LifetimeHours);

/// <summary>
/// Whether a slime is feral, and its feral clock. Rules in docs/behavior/feral-slimes.md; the settings
/// come from the prefab (<see cref="FeralSettings"/>), the few fixed numbers from static analysis of
/// SlimeFeral.
/// </summary>
public sealed class Feral
{
    /// <summary>Agitation at or above this turns a slime with <see cref="FeralSettings.DynamicToFeral"/> feral. Code-only.</summary>
    public const float AgitationTrigger = 0.999f;
    /// <summary>Agitation change when feralness is cleared with calming. Code-only.</summary>
    public const float CalmingAgitationChange = -0.5f;
    /// <summary>On The Ranch or in the Wilds a feral slime that runs out of time gets this many more game hours instead of poofing. Code-only.</summary>
    public const double ReprieveHours = 1;

    private readonly Slime _slime;

    public Feral(FeralSettings settings, Slime slime)
    {
        Settings = settings;
        _slime = slime;
    }

    public FeralSettings Settings { get; }
    public bool IsFeral => _slime.IsFeral;
    /// <summary>The game hour (total, see <see cref="GameClock.TotalHours"/>) it poofs at; infinity when not feral.</summary>
    public double ExpiresAt { get; private set; } = double.PositiveInfinity;

    /// <summary>Raised when it turns feral or stops being feral.</summary>
    public event Action<bool>? Changed;

    /// <summary>Makes it feral now (a feral spawner, a hunter largo forming, full agitation); its clock starts at <paramref name="nowHours"/>.</summary>
    public void SetFeral(double nowHours)
    {
        if (_slime.IsFeral)
            return;
        _slime.IsFeral = true;
        ExpiresAt = nowHours + Settings.LifetimeHours;
        Changed?.Invoke(true);
    }

    /// <summary>Stops it being feral; with <paramref name="calm"/> its agitation also drops by half.</summary>
    public void Clear(bool calm = false)
    {
        if (!_slime.IsFeral)
            return;
        _slime.IsFeral = false;
        if (calm)
            _slime.Agitation = Math.Clamp(_slime.Agitation + CalmingAgitationChange, 0f, 1f);
        ExpiresAt = double.PositiveInfinity;
        Changed?.Invoke(false);
    }

    /// <summary>Eating anything but the player calms a slime with <see cref="FeralSettings.DynamicFromFeral"/>.</summary>
    public void DidEat()
    {
        if (Settings.DynamicFromFeral)
            Clear();
    }

    /// <summary>
    /// Checks the clock at game hour <paramref name="nowHours"/>: turns feral when fully agitated (if it
    /// can), and returns true when it has run out of time and poofs. <paramref name="sheltered"/> is
    /// whether it is on The Ranch or in the Wilds, where it gets another hour instead.
    /// </summary>
    public bool Update(double nowHours, bool sheltered)
    {
        if (Settings.DynamicToFeral && !_slime.IsFeral && _slime.Agitation >= AgitationTrigger)
            SetFeral(nowHours);
        if (nowHours < ExpiresAt)
            return false;
        if (sheltered)
        {
            ExpiresAt += ReprieveHours;
            return false;
        }
        return true;
    }
}

/// <summary>
/// A feral largo's leap-and-stomp at the player (<c>FeralSlimeButtstomp</c>): code-only numbers.
/// The explosion's power, radius and damage come from the prefab.
/// </summary>
public static class FeralStomp
{
    /// <summary>It only stomps at a player between these distances (metres). Code-only.</summary>
    public const float MinDistance = 5f, MaxDistance = 20f;
    /// <summary>Seconds after a stomp before the next can start. Code-only.</summary>
    public const float ResetSeconds = 5f;
    /// <summary>When it wants to stomp, how much that matters is random in this range. Code-only.</summary>
    public const float MinRelevancy = 0.3f, MaxRelevancy = 1f;
    /// <summary>It aims this far in front of the player. Code-only.</summary>
    public const float AimAheadOfPlayer = 2f;
    /// <summary>The leap's speed multipliers. Code-only.</summary>
    public const float LeapFactorA = 1.2f, LeapFactorB = 1.4f;
    /// <summary>The landing counts when the touch is at least this far below its centre. Code-only.</summary>
    public const float UnderneathThreshold = 0.5f;

    /// <summary>Whether it would stomp at a player <paramref name="distance"/> metres away (grounded and feral).</summary>
    public static bool InRange(float distance) => distance >= MinDistance && distance <= MaxDistance;

    /// <summary>
    /// The leap's speed change, along (flat direction + up) normalised: √(distance × gravity) × 1.2 × 1.4.
    /// </summary>
    public static float LeapSpeed(float distance, float gravity) => MathF.Sqrt(distance * gravity) * LeapFactorA * LeapFactorB;
}

/// <summary>Biting the player (<c>AttackPlayer</c> with its <c>Chomper</c>): code-only numbers.</summary>
public static class PlayerAttack
{
    /// <summary>How much going for the player matters: drive² × 0.95, like food (GotoPlayer). Code-only.</summary>
    public static float GotoRelevancy(float drive) => drive * drive * SlimeMotion.FoodRelevancyFactor;
}
