using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// A slime in its own pen whose behaviour piece <typeparamref name="T"/> must fire with its data's numbers
/// (zoo part "abilities" or the trial's own group). <see cref="Setup"/> prepares the slime (mood, food,
/// the player); <see cref="Verdict"/> is asked every step once the piece has fired and returns null to
/// keep waiting, or whether it passed and the report line. The slime is removed once the trial is done,
/// so it can't wander into other pens.
/// </summary>
public sealed class AbilityTrial<T> : ZooTrial where T : SlimeBehaviour
{
    private readonly string _slimeId;
    private readonly Vector3? _playerAt;
    public SlimeActor Slime { get; private set; } = null!;
    public T Piece { get; private set; } = null!;
    public string? LastLine { get; private set; }
    public double FiredAt { get; private set; } = -1;
    public Action<AbilityTrial<T>>? Setup { get; init; }
    /// <summary>Called every step before it fires (to make it due once the player is in place, for instance).</summary>
    public Action<AbilityTrial<T>>? Before { get; init; }
    public required Func<AbilityTrial<T>, (bool Ok, string Line)?> Verdict { get; init; }
    public readonly Dictionary<string, object> Notes = [];

    public AbilityTrial(string name, string slimeId, Vector3? playerAt = null, string group = "abilities") : base(name, group)
    {
        _slimeId = slimeId;
        _playerAt = playerAt;
    }

    public new double Time => base.Time;
    public new ItemCatalog Catalog => base.Catalog;
    public new Vector3 Center => base.Center;
    public new Actor Place(string id, Vector3 onFloor) => base.Place(id, onFloor);
    public bool HoldPlayer(Vector3 onFloor) => Zoo.HoldPlayer(this, onFloor);
    public bool HasPlayer { get; private set; }

    protected override void Build()
    {
        Slime = (SlimeActor)Place(_slimeId, Center);
        if (Slime.Behaviour<T>() is not { } piece)
        {
            Fail($"{_slimeId} has no {typeof(T).Name}");
            return;
        }
        Piece = piece;
        Piece.Fired += (_, line) => (LastLine, FiredAt) = (line, Time);
        Setup?.Invoke(this);
        Outcome = $"waiting for {typeof(T).Name}";
    }

    public override void Step(double delta)
    {
        if (Done)
            return;
        if (!GodotObject.IsInstanceValid(Slime) || Slime.Consumed)
        {
            Finish(false, $"{_slimeId} is gone ({LastLine ?? "never fired"})");
            return;
        }
        if (_playerAt is { } at && !HasPlayer)
            HasPlayer = Zoo.HoldPlayer(this, Center + at);
        if (_playerAt is not null && !HasPlayer)
            return;
        if (Piece.Fires == 0)
        {
            Before?.Invoke(this);
            return;
        }
        Outcome = LastLine ?? "";
        if (Verdict(this) is { } v)
            Finish(v.Ok, v.Line);
    }

    private void Finish(bool ok, string line)
    {
        if (ok)
            Pass(line);
        else
            Fail(line);
        Zoo.ReleasePlayer(this);
        if (GodotObject.IsInstanceValid(Slime) && !Slime.Consumed)
            Slime.Consume();
    }
}

/// <summary>The ability trials (docs/behavior/slime-abilities.md).</summary>
public static class AbilityTrials
{
    private static float F(ItemCatalog c, string id, string cls, string field) =>
        c.Prefabs.Get(id).RootScript(cls)?[field] switch { float f => f, int i => i, _ => float.NaN };

    public static IEnumerable<ZooTrial> All(ItemCatalog catalog)
    {
        // Boom: explodes with its prefab's power and radius; the player 3.5 m away is hurt min..max; a plort is pushed.
        yield return new AbilityTrial<BoomExplode>("BOOM", "BOOM_SLIME", new Vector3(1.5f, 0, 0))
        {
            Setup = t => t.Place("PINK_PLORT", t.Center + new Vector3(-2f, 0, 2f)),
            Before = t => { if (!t.Notes.ContainsKey("due")) { t.Notes["due"] = true; t.Piece.DueNow(); } },
            Verdict = t =>
            {
                var r = t.Piece.Last!.Value;
                var (min, max) = (F(t.Catalog, "BOOM_SLIME", "BoomSlimeExplode", "minPlayerDamage"), F(t.Catalog, "BOOM_SLIME", "BoomSlimeExplode", "maxPlayerDamage"));
                var ok = r.Power == F(t.Catalog, "BOOM_SLIME", "BoomSlimeExplode", "explodePower") && r.Radius == F(t.Catalog, "BOOM_SLIME", "BoomSlimeExplode", "explodeRadius")
                         && r.PlayerDamage >= min && r.PlayerDamage <= max && r.Pushed >= 1;
                return (ok, $"{t.LastLine} (damage range {min}-{max})");
            },
        };

        // Rad: the player beside it soaks up radiation; the aura swells to 1.5x and back.
        yield return new AbilityTrial<RadAura>("RAD", "RAD_SLIME", new Vector3(1.3f, 0, 0))
        {
            Before = t =>
            {
                if (!t.Notes.ContainsKey("due")) { t.Notes["due"] = t.Time; t.Piece.DueNow(); }
            },
            Verdict = t =>
            {
                var seconds = t.Time - (double)t.Notes["due"];
                var rate = t.Piece.RadPerSecond;
                var ok = Math.Abs(t.Piece.Scale - RadSlime.ExpandFactor) < 0.01f && t.Piece.RadsGiven > 0 && t.Piece.RadsGiven <= rate * seconds + 0.01 && rate > 0;
                return (ok, $"{t.LastLine}; gave the player {t.Piece.RadsGiven:F1} rads in {seconds:F0} s at {rate}/s");
            },
        };

        // Crystal: launches and leaves one big spike and a ring of small ones; a spike hurts the player for its damage.
        yield return new AbilityTrial<CrystalLaunch>("CRYSTAL", "CRYSTAL_SLIME")
        {
            Before = t => { if (!t.Notes.ContainsKey("due") && t.Slime.Age > SlimeMotion.StartDelaySeconds) { t.Notes["due"] = true; t.Piece.DueNow(); } },
            Verdict = t =>
            {
                var spikes = t.Piece.Spikes.Where(GodotObject.IsInstanceValid).ToList();
                if (spikes.Count == 0)
                    return (false, $"{t.LastLine}; no spikes left");
                var big = spikes[0];
                if (!t.HoldPlayer(big.GlobalPosition))
                    return null;
                if (big.Hits == 0)
                    return t.Time - t.FiredAt > 5 ? (false, $"{t.LastLine}; the player on the big spike wasn't hurt") : null;
                var (min, max) = CrystalSlime.SmallSpikes(t.Slime.Mass);
                var small = spikes.Count - 1;
                var life = big.DiesAt - Math.Max(t.Catalog.Clock.TotalHours, 0);
                var data = t.Catalog.Prefabs.AppearancePrefab("CRYSTAL_SLIME", "CrystalAppearance", "largeCrystalPrefab")?.RootScript("CrystalSpikesLifecycle");
                var ok = small >= min && small < Math.Max(min + 1, max) && data?["damagePerHit"] is int d && big.Damage == d
                         && data["lifetime"] is float h && life > 0 && life <= h;
                return (ok, $"{t.LastLine}; small spikes allowed {min}-{max - 1}; standing on the big spike hurt {big.Damage} ({big.Hits} hit), it crumbles in {life:F2} h");
            },
        };

        // Quantum: agitated past its cutoff it vibrates, leaves qubits, and jumps into one.
        yield return new AbilityTrial<QuantumGhosts>("QUANTUM", "QUANTUM_SLIME")
        {
            Setup = t => t.Slime.Sim.Agitation = 1,
            Before = t =>
            {
                t.Slime.Sim.Agitation = 1;
                if (t.Piece.Qubits.Count > 0 && !t.Notes.ContainsKey("due")) { t.Notes["due"] = true; t.Notes["from"] = t.Slime.GlobalPosition; t.Piece.DueNow(); }
            },
            Verdict = t =>
            {
                var moved = ((Vector3)t.Notes["from"]).DistanceTo(t.Slime.GlobalPosition);
                var ok = t.Piece.Vibrating && t.Piece.Qubits.Count == 0 && moved <= t.Piece.SearchRadius + 1.5f && t.Piece.QubitsMade <= t.Piece.MaxQubits + 5
                         && t.Piece.SearchRadius == F(t.Catalog, "QUANTUM_SLIME", "GenerateQuantumQubit", "QubitSearchRadius");
                return (ok, $"{t.LastLine}; {t.Piece.QubitsMade} qubit(s) made first");
            },
        };

        // Dervish: agitated, its spin lifts it and sets a whirlwind loose that lasts the prefab's time.
        yield return new AbilityTrial<DervishSpin>("DERVISH", "DERVISH_SLIME")
        {
            Setup = t => { t.Slime.Sim.Agitation = 1; t.Place("PINK_PLORT", t.Center + new Vector3(1.5f, 0, 0)); },
            Before = t =>
            {
                t.Slime.Sim.Agitation = 1;
                if (!t.Notes.ContainsKey("due") && t.Slime.Age > SlimeMotion.StartDelaySeconds) { t.Notes["due"] = true; t.Notes["y"] = t.Slime.GlobalPosition.Y; t.Notes["top"] = t.Slime.GlobalPosition.Y; t.Piece.DueNow(); }
            },
            Verdict = t =>
            {
                t.Notes["top"] = Math.Max((float)t.Notes["top"], t.Slime.GlobalPosition.Y);
                if (t.Time - t.FiredAt < 3)
                    return null;
                var rose = (float)t.Notes["top"] - (float)t.Notes["y"];
                var data = t.Catalog.Prefabs.AppearancePrefab("DERVISH_SLIME", "TornadoAppearance", "fullWhirlwindPrefab")?.RootScript("DestroyAfterTime")?["lifeTimeHours"];
                var w = t.Piece.LastWhirlwind;
                var ok = w is not null && data is float h && w.LifetimeHours == h && rose > 1f;
                return (ok, $"{t.LastLine}; rose {rose:F1} m in 3 s, the whirlwind caught {w?.Caught ?? 0} item(s)");
            },
        };

        // Tangle pollen: fully agitated, a pollen cloud grows on it and is let go.
        yield return new AbilityTrial<PollenCloud>("TANGLE_POLLEN", "TANGLE_SLIME")
        {
            Setup = t => t.Slime.Sim.Agitation = 1,
            Before = t => t.Slime.Sim.Agitation = 1,
            Verdict = t =>
            {
                var ok = t.Piece.StartAgitation == F(t.Catalog, "TANGLE_SLIME", "PollenCloudController", "startGrowthAgitation");
                return (ok, $"{t.LastLine} after {t.FiredAt:F0} s");
            },
        };

        // Tangle vines: hungry, it vines a food it wants from a few metres away and eats it.
        var vineFood = BehaviourTrials.DietFood(catalog, "TANGLE_SLIME");
        yield return new AbilityTrial<GroundVine>("TANGLE_VINE", "TANGLE_SLIME")
        {
            Setup = t =>
            {
                t.Slime.Sim.Hunger = 1;
                t.Slime.Ate += (_, food) => t.Notes["ate"] = food;
                if (vineFood is not null)
                    t.Place(vineFood, t.Center + new Vector3(4f, 0, 0));
            },
            Verdict = t =>
            {
                if (!t.Notes.ContainsKey("ate"))
                    return t.Time - t.FiredAt > 6 ? (false, $"{t.LastLine}; but it never ate it") : null;
                return ((string)t.Notes["ate"] == vineFood && t.Piece.LastFood == vineFood, $"{t.LastLine}; then ate {t.Notes["ate"]}");
            },
        };

        // Hunter: invisible for its first 5 s, then seen.
        yield return new AbilityTrial<Stealth>("HUNTER_CLOAK", "HUNTER_SLIME")
        {
            Verdict = t =>
            {
                if (t.Piece.Cloaked)
                    return t.Time > 30 ? (false, "never decloaked") : null;
                var age = t.Slime.Age;
                return (age >= HunterSlime.InitialStealthSeconds && age <= HunterSlime.InitialStealthSeconds + 1f, $"{t.LastLine} (cloaked from the start, {HunterSlime.InitialStealthSeconds} s then fading at {HunterSlime.OpacityPerSecond}/s)");
            },
        };

        // Mosaic: glints appear around it within its spawn radius, never below it.
        yield return new AbilityTrial<Glints>("MOSAIC", "MOSAIC_SLIME")
        {
            Verdict = t =>
            {
                var glints = t.Piece.Live.ToList();
                var r = MosaicSlime.SpawnRadius(t.Slime.Sim.Agitation);
                var ok = glints.Count > 0 && glints.All(g => g.GlobalPosition.DistanceTo(t.Slime.GlobalPosition) <= r + 3f && g.GlobalPosition.Y >= t.Slime.GlobalPosition.Y - 1.5f);
                return (ok, t.LastLine!);
            },
        };

        // Feeding habits (docs/behavior/feeding-habits.md).
        var tabbyFood = BehaviourTrials.DietFood(catalog, "TABBY_SLIME");
        yield return new AbilityTrial<StalkPounce>("TABBY_POUNCE", "TABBY_SLIME", group: "feeding")
        {
            Setup = t =>
            {
                t.Slime.Sim.Hunger = 1;
                t.Slime.Ate += (_, food) => t.Notes["ate"] = food;
                if (tabbyFood is not null)
                    t.Place(tabbyFood, t.Center + new Vector3(5.5f, 0, 0));
            },
            Before = t => t.Slime.Sim.Hunger = 1,
            Verdict = t =>
            {
                if (!t.Notes.ContainsKey("ate"))
                    return t.Time - t.FiredAt > 20 ? (false, $"{t.LastLine}; but it never ate {tabbyFood}") : null;
                return (t.Piece.LastTarget == tabbyFood && !t.Piece.DoesParkour, $"{t.LastLine}; then ate {t.Notes["ate"]}");
            },
        };

        var saberFood = BehaviourTrials.DietFood(catalog, "SABER_SLIME");
        yield return new AbilityTrial<StalkPounce>("SABER_FEINT", "SABER_SLIME", group: "feeding")
        {
            Setup = t =>
            {
                t.Slime.Sim.Hunger = 1;
                if (saberFood is not null)
                    t.Place(saberFood, t.Center + new Vector3(5.5f, 0, 0));
            },
            Before = t =>
            {
                t.Slime.Sim.Hunger = 1;
                if (t.Piece.Feints > 0 && !t.Notes.ContainsKey("feint"))
                    t.Notes["feint"] = t.Time;
            },
            Verdict = t => (t.Piece.DoesParkour && t.Piece.Feints > 0, $"{t.LastLine}; feinted {t.Piece.Feints} time(s) first (55-80 degrees)"),
        };

        yield return new AbilityTrial<Gather>("TABBY_CARRY", "TABBY_SLIME", group: "feeding")
        {
            Setup = t =>
            {
                t.Slime.Sim.Hunger = 0;
                t.Place("POGO_FRUIT", t.Center + new Vector3(-2.5f, 0, 0));
                t.Place("CARROT_VEGGIE", t.Center + new Vector3(4.5f, 0, 3f));
            },
            Before = t => t.Slime.Sim.Hunger = 0,
            Verdict = t => (t.Piece.LastCarried is not null && t.Piece.LastCarryDistance > 1f, t.LastLine!),
        };

        yield return new AbilityTrial<GoldRunner>("GOLD", "GOLD_SLIME", new Vector3(-4f, 0, 0), group: "feeding")
        {
            Before = t =>
            {
                if (t.Notes.ContainsKey("thrown") || t.Slime.Age < SlimeMotion.StartDelaySeconds)
                    return;
                t.Notes["thrown"] = true;
                var food = t.Place("CARROT_VEGGIE", t.Slime.GlobalPosition + Vector3.Up * 2.5f);
                food.LinearVelocity = Vector3.Down * 4f;
            },
            Verdict = t => (t.Piece.PlortsMade == 1 && t.Piece.Plort == "GOLD_PLORT" && (t.Piece.Fleeing || t.Piece.Vanished), $"{t.LastLine}; made {t.Piece.PlortsMade} {t.Piece.Plort}"),
        };

        yield return new AbilityTrial<LuckyCoins>("LUCKY", "LUCKY_SLIME", group: "feeding")
        {
            Before = t =>
            {
                if (t.Notes.ContainsKey("thrown") || t.Slime.Age < SlimeMotion.StartDelaySeconds)
                    return;
                t.Notes["thrown"] = true;
                t.Notes["coins"] = t.Catalog.Wallet?.Coins ?? 0;
                var hen = t.Place("HEN", t.Slime.GlobalPosition + Vector3.Up * 2.5f);
                hen.LinearVelocity = Vector3.Down * 4f;
            },
            Verdict = t =>
            {
                var expected = LuckySlime.FirstBundles * t.Piece.PerBundle;
                if (t.Piece.CoinsGiven < expected)
                    return t.Time - t.FiredAt > 5 ? (false, $"{t.LastLine}; only {t.Piece.CoinsGiven} coins") : null;
                var wallet = t.Catalog.Wallet is { } w ? w.Coins - (int)t.Notes["coins"] : -1;
                var vanish = (t.Piece.VanishAt ?? 0) - t.Catalog.Clock.TotalHours;
                return (t.Piece.PerBundle > 0 && t.Piece.CoinsGiven == expected && wallet == expected && vanish is > 0 and <= LuckySlime.VanishHours,
                    $"{t.LastLine}; {t.Piece.CoinsGiven} coins into the wallet, vanishes in {vanish * 60:F1} game min");
            },
        };

        foreach (var (name, slime, kind) in new[] { ("FIRE_ASH", "FIRE_SLIME", FeedingPatch.Ash), ("PUDDLE_WATER", "PUDDLE_SLIME", FeedingPatch.Water) })
            yield return new AbilityTrial<Grazer>(name, slime, group: "feeding")
            {
                Setup = t =>
                {
                    t.Slime.Sim.Hunger = 1;
                    t.Catalog.Patches.Add(new FeedingPatch(kind, t.Center, new Vector3(3f, 1f, 3f)));
                },
                Before = t => t.Slime.Sim.Hunger = 1,
                Verdict = t =>
                {
                    if (t.Piece.PlortsMade == 0)
                        return t.Time - t.FiredAt > 10 ? (false, $"{t.LastLine}; no plort") : null;
                    var data = t.Catalog.Prefabs.Get(slime).RootScript(kind == FeedingPatch.Ash ? "SlimeEatAsh" : "SlimeEatWater")?["eatRate"];
                    return (data is float r && t.Piece.EatRate == r && t.Piece.Plort == (kind == FeedingPatch.Ash ? "FIRE_PLORT" : "PUDDLE_PLORT"),
                        $"{t.LastLine}; {t.Piece.Bites} bite(s); away from {kind} it would poof after {t.Piece.HoursAllowed} h");
                },
            };
    }
}
