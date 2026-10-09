using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Ranch;
using N = System.Numerics;

namespace OpenRanch.Game.SaveLoad;

/// <summary>
/// Saves the live ranch to openranch's own format (.ranch.json; src/OpenRanch.Ranch/RanchWriter.cs):
/// the save it was opened from, with the world's money, clock, plots and actors laid over it. With
/// --save-out FILE it writes once the world has settled and then quits (unless --m3-check is
/// running too, which quits instead); in play, the save key writes to openranch's user folder.
/// Never writes the original's .sav format, and never into the original's save folder.
/// </summary>
public partial class SaveWriter : Node
{
    /// <summary>The same settling time as <see cref="M3Check"/>, so a check and a write see the same world.</summary>
    public const double SettleSeconds = 4;

    /// <summary>The input action that saves the ranch in play.</summary>
    public const string SaveAction = "save_ranch";

    // openranch's own key: the original has no save key (it saves when the player sleeps and
    // between scenes). UNVERIFIED.md, "Save key".
    private const Key SaveKey = Key.F5;

    private readonly SavedRanch _saved;
    private readonly Slimes.M2World _m2;
    private readonly GameEnums _names;
    private readonly string? _outPath;
    private readonly bool _quitAfterWrite;
    private readonly int _hunger, _agitation;
    private int _earned;
    private double _time;
    private bool _wroteOut;

    public SaveWriter(SavedRanch saved, Slimes.M2World m2, GameInstall install, string? outPath, bool quitAfterWrite)
    {
        Name = "SaveWriter";
        _saved = saved;
        _m2 = m2;
        _names = new GameEnums(install);
        _outPath = outPath;
        _quitAfterWrite = quitAfterWrite;
        _hunger = _names.Value(GameEnum.Emotion, "HUNGER");
        _agitation = _names.Value(GameEnum.Emotion, "AGITATION");

        // The save's money went into the wallet before this node; what comes in from now on is earned.
        m2.Wallet.Changed += amount => _earned += System.Math.Max(0, amount);
    }

    /// <summary>Where the save key writes: openranch's user folder, one file per game.</summary>
    public string PlayPath
    {
        get
        {
            var name = string.IsNullOrEmpty(_saved.Ranch.GameName) ? "ranch" : _saved.Ranch.GameName;
            foreach (var bad in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(bad, '_');
            return ProjectSettings.GlobalizePath($"user://saves/{name}{RanchSave.FileExtension}");
        }
    }

    public override void _Ready()
    {
        if (InputMap.HasAction(SaveAction))
            return;
        InputMap.AddAction(SaveAction);
        InputMap.ActionAddEvent(SaveAction, new InputEventKey { PhysicalKeycode = SaveKey });
    }

    public override void _ExitTree() => _names.Dispose();

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(SaveAction))
            Write(PlayPath);
    }

    public override void _PhysicsProcess(double delta)
    {
        _time += delta;
        if (_outPath is null || _wroteOut || _time < SettleSeconds)
            return;
        _wroteOut = true;
        var ok = Write(_outPath);
        if (_quitAfterWrite)
            GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>The ranch as the world holds it now.</summary>
    public RanchState Snapshot(out (int Kept, int Added, int Gone) counts)
    {
        var ranch = _saved.Ranch;
        var actors = new List<LiveActor>();
        foreach (var actor in _m2.Catalog.Live)
        {
            if (!TryType(actor.Id, out var type))
                continue;
            // The loader hands over the save's id of every actor it put into the world.
            long? id = _saved.SpawnedIds.TryGetValue(actor, out var saved) ? saved : null;
            var q = actor.GlobalBasis.GetRotationQuaternion();
            // Mirroring z (UnityConvert) turns a rotation (x, y, z, w) into (-x, -y, z, w).
            var rotation = RanchWriter.EulerDegrees(new N.Quaternion(-q.X, -q.Y, q.Z, q.W));
            var moods = actor is Slimes.SlimeActor slime
                ? new Dictionary<int, float> { [_hunger] = slime.Sim.Hunger, [_agitation] = slime.Sim.Agitation }
                : new Dictionary<int, float>();
            var p = Unity(actor.GlobalPosition);
            actors.Add(new LiveActor(id, type, new Vec3(p.X, p.Y, p.Z), rotation, moods));
        }
        var tracked = _saved.SpawnedIds.Values.ToHashSet();
        var kept = actors.Count(a => a.ActorId is not null);
        counts = (kept, actors.Count - kept, tracked.Count - kept);

        var live = new LiveRanch
        {
            Money = _m2.Wallet.Coins,
            MoneyEarned = _earned,
            // The world clock (World/WorldTime.cs) moves the loaded ranch's world time itself.
            WorldTime = ranch.WorldTime,
            // Plots stand as loaded: nothing in the world builds or upgrades them yet.
            Plots = _saved.Plots.ToDictionary(p => p.Site.Id, p => new LivePlot(p.Plot.Type, p.Plot.Upgrades)),
            // The vacpack of normal play; a slime sucked up left the world and is kept here.
            Ammo = new Dictionary<int, IReadOnlyList<AmmoSlot>> { [_names.Value(GameEnum.AmmoMode, PlayerVacpack.DefaultMode)] = PlayerVacpack.Save(_m2.Pack, _names) },
            TrackedActorIds = tracked,
            Actors = actors,
        };
        return RanchWriter.Merge(ranch, live);
    }

    /// <summary>Writes the live ranch to <paramref name="path"/>; reports and returns whether it worked.</summary>
    public bool Write(string path)
    {
        try
        {
            var ranch = Snapshot(out var counts);
            RanchSave.WriteFile(path, ranch);
            GD.Print($"Saved {path}: {ranch.Player.Money} money, day {ranch.Clock.Day} {ranch.Clock.Hour:F2} h, " +
                     $"{ranch.Actors.Count} actors ({counts.Kept} from the world as they are now, {counts.Added} new, " +
                     $"{counts.Gone} gone, {ranch.Actors.Count - counts.Kept - counts.Added} as saved)");
            return true;
        }
        catch (System.Exception e) when (e is System.IO.IOException or System.InvalidOperationException or System.UnauthorizedAccessException or System.ArgumentException)
        {
            GD.PushError($"Could not save {path}: {e.Message}");
            return false;
        }
    }

    private bool TryType(string id, out int type)
    {
        try
        {
            type = _names.Value(GameEnum.ItemId, id);
            return true;
        }
        catch (KeyNotFoundException)
        {
            type = 0;
            return false;
        }
    }

    private static N.Vector3 Unity(Vector3 v) => new(v.X, v.Y, -v.Z);
}
