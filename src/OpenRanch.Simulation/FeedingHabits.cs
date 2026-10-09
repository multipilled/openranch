namespace OpenRanch.Simulation;

// Code-only numbers behind the slimes' feeding habits (docs/behavior/feeding-habits.md), each from
// static analysis of the component named. Tuning a prefab carries is read from it instead.

/// <summary>Tabbies, sabers and hunters stalking and pouncing (<c>StalkConsumable</c>).</summary>
public static class Stalking
{
    /// <summary>Within this distance it stops creeping up and gets ready to pounce.</summary>
    public const float PounceDistance = 8f;
    /// <summary>Phase lengths in seconds: creeping up, waiting after a failed approach, the butt wiggle, the pounce, a feint, the pivot after it.</summary>
    public const float ApproachSeconds = 3f, WaitSeconds = 3f, PrepSeconds = 2f, PounceSeconds = 1f, FeintSeconds = 1f, PivotSeconds = 1.5f;
    /// <summary>After a pounce it doesn't stalk again for this long.</summary>
    public const float ResetSeconds = 15f;
    /// <summary>The player as prey: a drive of 1 (no feeling) plus this.</summary>
    public const float PlayerExtraDrive = -0.1f;
    /// <summary>Creeping up: aim = (direction × 400 + up × (650 if slower than √10 m/s, else 400)) normalised; push 250 × mass × pursuit at the centre and 450 × mass at its base.</summary>
    public const float CreepAhead = 400f, CreepUpSlow = 650f, CreepUpFast = 400f, SlowSpeedSquared = 10f, CreepPush = 250f, CreepBasePush = 450f;
    /// <summary>A leap's speed change: √(distance × gravity) × 1.2, along (direction + up) normalised.</summary>
    public const float LeapFactor = 1.2f;

    public static float LeapSpeed(float distance, float gravity) => MathF.Sqrt(distance * gravity) * LeapFactor;

    /// <summary>How much stalking matters: drive² (no 0.95 factor, unlike going for food).</summary>
    public static float Relevancy(float drive) => drive * drive;
}

/// <summary>Tabbies and sabers carrying food (<c>GatherIdentifiableItems</c>).</summary>
public static class Gathering
{
    /// <summary>It gives up on a carry after this long without progress.</summary>
    public const float GiveUpSeconds = 10f;
    /// <summary>It picks the item up when this close (times its scale)...</summary>
    public const float PickUpDistance = 1f;
    /// <summary>...and drops it this close to the food it carries it to.</summary>
    public const float DropDistance = 3f;
    /// <summary>When it has something to carry, carrying matters a random 0.3-0.5.</summary>
    public const float MinRelevancy = 0.3f, MaxRelevancy = 0.5f;

    /// <summary>A jump while carrying: the jump strength scaled by (item mass + its mass) ÷ its mass.</summary>
    public static float CarryJump(float maxJump, float itemMass, float mass) => maxJump * (itemMass + mass) / mass;
}

/// <summary>Running away (<c>SlimeFlee</c>, used by gold slimes).</summary>
public static class Fleeing
{
    /// <summary>Push per unit mass at the centre (times the flee speed factor) and at its base.</summary>
    public const float Push = 300f, BasePush = 540f;
    public const float Relevancy = 1f;
}

/// <summary>Gold slimes (<c>GoldSlimeProducePlorts</c>, <c>GoldSlimeFlee</c>).</summary>
public static class GoldSlime
{
    /// <summary>A hit counts when the bodies come together faster than this.</summary>
    public const float HitThreshold = 0.02f;
    /// <summary>A one-step upward force when it makes a plort.</summary>
    public const float JumpOnHit = 400f;
    /// <summary>Foods, chicks and plorts make it drop a gold plort, except these.</summary>
    public static bool CausesPlort(string id) =>
        id != "GINGER_VEGGIE" && id != "GOLD_PLORT"
        && Items.KindOf(id) is ItemKind.Veggie or ItemKind.Fruit or ItemKind.Meat or ItemKind.Tofu or ItemKind.Chick or ItemKind.Plort;
}

/// <summary>Lucky slimes (<c>LuckySlimeProduceCoins</c>, <c>LuckySlimeFlee</c>).</summary>
public static class LuckySlime
{
    public const float HitThreshold = 0.02f;
    /// <summary>After the hit it waits this long, then hops (up 450, sideways up to ±225, one step's force)...</summary>
    public const float HopDelay = 0.35f, HopUp = 450f, HopSideways = 225f;
    /// <summary>...and drops coin bundles this far apart in time.</summary>
    public const float SecondsBetweenCoins = 0.1f;
    /// <summary>The first hit gives two bundles; each later hit doubles that, up to six.</summary>
    public const int FirstBundles = 2, MaxBundles = 6;
    /// <summary>Seen by the player, or hit, it vanishes 600 world seconds (10 game minutes) later.</summary>
    public const double VanishHours = 600.0 / 3600.0;

    public static int Bundles(int last) => last == 0 ? FirstBundles : Math.Min(MaxBundles, last * 2);
}

/// <summary>Fire slimes eating ash and puddle slimes drinking water (<c>SlimeEatAsh</c>, <c>SlimeEatWater</c>, <c>GotoAsh</c>, <c>GotoWater</c>).</summary>
public static class Grazing
{
    /// <summary>A plort comes this long after a bite...</summary>
    public const float ProduceDelay = 2f;
    /// <summary>...half a metre above its centre, moving up at 1 m/s, growing in over half a second.</summary>
    public const float ProduceHeight = 0.5f, ProduceSpeed = 1f, ProduceGrowSeconds = 0.5f;
    /// <summary>It looks for ash or water within this far.</summary>
    public const float SearchRadius = 30f;
    /// <summary>A puddle slime checks how crowded it is every 2 s.</summary>
    public const float DensityCheckSeconds = 2f;

    /// <summary>Too crowded to make plorts: more slimes within reach than allowed, or else more puddle plorts than allowed.</summary>
    public static bool TooDense(int slimesNear, int maxSlimes, int plortsNear, int maxPlorts) => slimesNear > maxSlimes || plortsNear > maxPlorts;

    /// <summary>How much heading for ash or water matters: 1 − the share of its time away still left (0 while safe, 1 when about to poof).</summary>
    public static float GotoRelevancy(double hoursLeft, float hoursAllowed) =>
        hoursAllowed <= 0 ? 0f : 1f - Math.Clamp((float)(hoursLeft / hoursAllowed), 0f, 1f);
}
