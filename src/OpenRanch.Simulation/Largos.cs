using OpenRanch.Formats.Game;

namespace OpenRanch.Simulation;

/// <summary>
/// Which slime a slime turns into when it eats another species' plort: a base slime becomes the
/// largo of the two plorts, a largo becomes a tarr. The pairs come from the install's largo
/// definitions (their two base slimes); the few fixed ids and numbers live only in the game's code.
/// See docs/behavior/largos.md.
/// </summary>
public sealed class Largos
{
    /// <summary>What a largo becomes when it eats a third kind of plort. Code-only fact.</summary>
    public const string TarrSlime = "TARR_SLIME";
    /// <summary>The one plort no slime turns from. Code-only fact.</summary>
    public const string NeverTransformingPlort = "QUICKSILVER_PLORT";
    /// <summary>A transforming plort is wanted at agitation or this floor, whichever is higher. Code-only fact.</summary>
    public const float PlortMinDrive = 0.5f;
    /// <summary>Slimes want this plort more than others (an extra drive of <see cref="EagerPlortExtraDrive"/>). Code-only fact.</summary>
    public const string EagerPlort = "HONEY_PLORT";
    public const float EagerPlortExtraDrive = 0.5f;
    /// <summary>How long a slime takes to grow (or shrink) to its new size after transforming. Code-only fact.</summary>
    public const float TransformScaleSeconds = 0.5f;

    private readonly Dictionary<(string, string), string> _byPlorts = new();

    /// <summary>Indexes every largo in <paramref name="slimes"/> by its two base slimes' first plorts.</summary>
    public Largos(SlimeData slimes)
    {
        foreach (var largo in slimes.Slimes)
        {
            if (!largo.IsLargo || largo.BaseSlimes is not { Count: 2 } bases)
                continue;
            if (!slimes.TryGet(bases[0], out var a) || !slimes.TryGet(bases[1], out var b)
                || a.Diet.Produces.Count == 0 || b.Diet.Produces.Count == 0)
                continue;
            _byPlorts.TryAdd(Key(a.Diet.Produces[0], b.Diet.Produces[0]), largo.Id);
        }
    }

    /// <summary>Every largo by its pair of plorts.</summary>
    public int Count => _byPlorts.Count;

    /// <summary>The largo made from two plorts, in either order, or null when there is none.</summary>
    public string? ForPlorts(string plortA, string plortB) => _byPlorts.GetValueOrDefault(Key(plortA, plortB));

    /// <summary>
    /// What <paramref name="slime"/> becomes when it eats <paramref name="plort"/>, or null if it doesn't
    /// transform from it: only slimes that make plorts and can become largos (or already are), never
    /// from their own plorts or the quicksilver plort.
    /// </summary>
    public string? Becomes(SlimeInfo slime, string plort)
    {
        var produces = slime.Diet.Produces;
        if (!produces.Any(Items.IsPlort) || !(slime.IsLargo || slime.CanLargofy))
            return null;
        if (!Items.InFoodGroup(plort, "PLORTS") || plort == NeverTransformingPlort || produces.Contains(plort))
            return null;
        return slime.IsLargo ? TarrSlime : ForPlorts(plort, produces[0]);
    }

    private static (string, string) Key(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
}
