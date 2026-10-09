using System;
using Godot;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// One piece of a slime's behaviour that comes from a component on its prefab (feral, abilities,
/// feeding habits). <see cref="SlimeBehaviours.For"/> picks the pieces a prefab has. A piece can
/// compete for the slime's attention like the original's sub-behaviours (<see cref="Relevancy"/>: the
/// slime does whichever matters most when it rethinks, and keeps doing it until
/// <see cref="CanRethink"/>), and can also run every physics step on its own (<see cref="Tick"/>)
/// and react to touches (<see cref="Touched"/>).
/// </summary>
public abstract class SlimeBehaviour
{
    public SlimeActor Slime { get; private set; } = null!;
    protected ItemCatalog Catalog => Slime.Catalog;

    internal void Attach(SlimeActor slime)
    {
        Slime = slime;
        Ready();
    }

    /// <summary>Called once the slime exists.</summary>
    protected virtual void Ready() { }

    /// <summary>How much doing this matters now, 0 to 1 (0: not at all). Asked when the slime rethinks.</summary>
    public virtual float Relevancy(bool grounded) => 0f;
    /// <summary>Whether the slime may switch to something else while this is going on.</summary>
    public virtual bool CanRethink => true;
    /// <summary>The slime has started doing this.</summary>
    public virtual void Selected() { }
    /// <summary>The slime has switched to something else.</summary>
    public virtual void Deselected() { }
    /// <summary>Every physics step while the slime is doing this (grounded or not).</summary>
    public virtual void Action(float delta) { }
    /// <summary>Every physics step, whatever the slime is doing (not while it is being taken out of the game).</summary>
    public virtual void Tick(float delta) { }
    /// <summary>The slime has started touching <paramref name="body"/> (an item, the world, the player).</summary>
    public virtual void Touched(Node body) { }

    /// <summary>How many times it has done its thing (exploded, launched, spun...).</summary>
    public int Fires { get; private set; }
    /// <summary>Raised each time it does its thing, with a line saying what happened and with which numbers.</summary>
    public event Action<SlimeBehaviour, string>? Fired;

    protected void Report(string what)
    {
        Fires++;
        Fired?.Invoke(this, what);
    }

    /// <summary>The prefab component's number <paramref name="field"/> (0 when missing).</summary>
    protected static float F(OpenRanch.Formats.Unity.Managed.SerializedObject? data, string field, float fallback = 0f) =>
        data?[field] switch { float f => f, int i => i, _ => fallback };
}
