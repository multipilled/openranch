using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Scene;
using OpenRanch.Game.World;
using OpenRanch.Ranch;
using OpenRanch.Simulation;
using N = System.Numerics;

namespace OpenRanch.Game.SaveLoad;

/// <summary>
/// Milestone 3's check, run without a window (--save FILE --m3-check): The Ranch opened from a save has
/// to hold what the save reader reports. Every plot site of the zone carries the saved plot type and
/// none of the scene's own plots; each plot shows exactly the upgrade objects its saved upgrades
/// switch on, and the crop the save planted on it; each plot keeps the save's contents (stores,
/// feeder, collector, ash); the slimes the save keeps in the zone's corrals are all there, by type,
/// and still inside a corral after a few seconds of physics; the zone's other actors are all there by
/// type, the produce hanging from crops still hangs and nothing fell through the ground; the wallet
/// holds the saved money. The save file is read again here, on its own, for the comparison.
/// Prints a report and quits with 0 when everything matched, 1 otherwise.
/// </summary>
public partial class M3Check : Node
{
    private const double SettleSeconds = 4;

    private readonly SavedRanch _saved;
    private readonly ZoneExtract _zone;
    private readonly Slimes.M2World _m2;
    private readonly GameEnums _names;
    private readonly Player.PlayerController? _player;
    private double _time;

    public M3Check(SavedRanch saved, ZoneExtract zone, Slimes.M2World m2, GameInstall install, Player.PlayerController? player = null)
    {
        _player = player;
        Name = "M3Check";
        _saved = saved;
        _zone = zone;
        _m2 = m2;
        _names = new GameEnums(install);
    }

    public override void _PhysicsProcess(double delta)
    {
        _time += delta;
        if (_time < SettleSeconds)
            return;
        SetPhysicsProcess(false);
        var (passed, report) = Run();
        GD.Print(report);
        _names.Dispose();
        GetTree().Quit(passed ? 0 : 1);
    }

    private (bool Passed, string Report) Run()
    {
        var ranch = _saved.Ranch;
        var report = new StringBuilder("m3-check:\n");
        var passed = true;
        void Check(bool ok, string what)
        {
            passed &= ok;
            report.AppendLine($"  {(ok ? "ok  " : "FAIL")} {what}");
        }

        // What the zone draws: the zone itself and the plots in play, built site by site (RanchEconomy).
        var renderers = _zone.Renderers.Concat(_saved.LiveRenderers?.Invoke() ?? []).ToList();

        // Plots: the save reader's type for each of the zone's sites, against what the built zone holds.
        var siteIds = _saved.Plots.Select(p => p.Site.Id).ToHashSet();
        var expected = ranch.Plots.Where(p => siteIds.Contains(p.Id))
            .GroupBy(p => _names.PlotType(p.Type)).ToDictionary(g => g.Key, g => g.Count());
        var prefabTypes = _saved.Plots.Select(p => p.Prefab).DistinctBy(p => p.Name).ToDictionary(p => p.Name, p => p.Type);
        var built = renderers.Select(r => r.Path.Split('/'))
            .Where(parts => parts.Length > 2 && siteIds.Contains(parts[0]) && prefabTypes.ContainsKey(parts[1]))
            .Select(parts => (Site: parts[0], Type: prefabTypes[parts[1]])).Distinct()
            .GroupBy(s => _names.PlotType(s.Type)).ToDictionary(g => g.Key, g => g.Count());
        Check(siteIds.Count > 0 && Same(expected, built), $"plots on the zone's {siteIds.Count} sites: built {Describe(built)}; save {Describe(expected)}");
        var leftovers = renderers.Count(r => r.Path.Contains("/landPlot/"));
        Check(leftovers == 0, $"scene plots left standing: {leftovers} meshes");

        // Each plot has ground under its middle, at the site's height. Some plots stand something on
        // that middle (a crop, the incinerator's pit), so the ray, which starts 3 m up, goes on past
        // what it hits more than 1.5 m above the site.
        var space = _m2.GetWorld3D().DirectSpaceState;
        var floorless = new List<string>();
        foreach (var plot in _saved.Plots)
        {
            var at = UnityConvert.Position(plot.Site.PlotWorld.Translation);
            var from = at + Vector3.Up * 3;
            Godot.Collections.Dictionary hit;
            do
            {
                hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, at + Vector3.Down * 30, Slimes.Actor.WorldLayer));
                if (hit.Count > 0)
                    from = hit["position"].AsVector3() + Vector3.Down * 0.01f;
            } while (hit.Count > 0 && hit["position"].AsVector3().Y - at.Y > 1.5f);
            if (hit.Count == 0 || Mathf.Abs(hit["position"].AsVector3().Y - at.Y) > 1.5f)
                floorless.Add($"{plot.Site.Id} ({_names.PlotType(plot.Plot.Type)}) at {plot.Site.PlotWorld.Translation}: " +
                              (hit.Count == 0 ? "nothing below" : $"ground at {hit["position"].AsVector3().Y:F2}, {(hit["collider"].AsGodotObject() is Node n ? n.GetPath().ToString() : "?")}"));
        }
        Check(floorless.Count == 0, $"plots without ground under them: {floorless.Count}" + string.Concat(floorless.Take(5).Select(f => "\n         " + f)));

        // A second, independent read of the save for the plots' upgrades, crops and contents and the loose actors.
        var reader = RanchFiles.Read(_saved.FilePath);
        Plot SavedPlot(PlacedPlot p) => reader.FindPlot(p.Site.Id) ?? p.Plot;
        var pathsBySite = renderers.Select(r => r.Path).GroupBy(p => p.Split('/')[0]).ToDictionary(g => g.Key, g => g.ToList());

        // Upgrades: every object an upgrader of the plot points at is drawn exactly as the save's upgrades say.
        var upgradeWrong = new List<string>();
        int objectsOn = 0, objectsOff = 0;
        var applied = new Dictionary<string, int>();
        var ignored = new Dictionary<string, int>();
        foreach (var plot in _saved.Plots)
        {
            var tree = plot.Prefab.Tree!;
            var wanted = PlotUpgrades.Apply(plot.Prefab.Upgraders, SavedPlot(plot).Upgrades, _names);
            foreach (var u in wanted.Applied)
                applied[u] = applied.GetValueOrDefault(u) + 1;
            foreach (var u in wanted.Ignored)
                ignored[u] = ignored.GetValueOrDefault(u) + 1;
            var paths = pathsBySite.GetValueOrDefault(plot.Site.Id) ?? [];
            foreach (var target in plot.Prefab.Upgraders.SelectMany(u => u.Targets.Values.SelectMany(t => t)).Distinct())
            {
                var obj = tree.Objects[target];
                bool Under(string path, string root) => path == root || path.StartsWith(root + "/", System.StringComparison.Ordinal);
                var meshes = tree.Renderers.Where(r => Under(r.Item.Path, obj.Path)).ToList();
                if (meshes.Count == 0)
                    continue;
                var want = meshes.Count(r => tree.IsOn(r.Chain, wanted.Switched));
                var have = paths.Count(p => Under(p, $"{plot.Site.Id}/{plot.Prefab.Name}/{obj.Path}"));
                if (want != have)
                    upgradeWrong.Add($"{plot.Site.Id} {obj.Path}: {have} meshes drawn, the save's upgrades want {want}");
                if (want > 0)
                    objectsOn++;
                else
                    objectsOff++;
            }
        }
        Check(upgradeWrong.Count == 0 && objectsOn + objectsOff > 0,
            $"plot upgrades: {objectsOn} upgrade objects drawn and {objectsOff} not, as the save says; applied {Describe(applied)}" +
            (ignored.Count > 0 ? $"; no upgrader for {Describe(ignored)}" : "") + string.Concat(upgradeWrong.Take(5).Select(w => "\n         " + w)));

        // Crops: the crop each plot's save names stands on it.
        var none = _names.Value(GameEnum.SpawnResource, "NONE");
        var cropByPrefab = _saved.Crops.Where(c => c.Prefab is not null).Select(c => c.Prefab!).DistinctBy(c => c.Name)
            .ToDictionary(c => c.Name, c => _names.Name(GameEnum.SpawnResource, c.Id));
        var cropsWanted = _saved.Plots.Where(p => SavedPlot(p).AttachedResource != none)
            .Select(p => $"{p.Site.Id} {_names.Name(GameEnum.SpawnResource, SavedPlot(p).AttachedResource)}").ToHashSet();
        var cropsBuilt = renderers.Select(r => r.Path.Split('/'))
            .Where(parts => parts.Length > 2 && siteIds.Contains(parts[0]) && cropByPrefab.ContainsKey(parts[1]))
            .Select(parts => $"{parts[0]} {cropByPrefab[parts[1]]}").ToHashSet();
        Check(cropsWanted.SetEquals(cropsBuilt),
            $"crops on plots: {Describe(Count(cropsBuilt.Select(c => c.Split(' ')[1])))}; save {Describe(Count(cropsWanted.Select(c => c.Split(' ')[1])))}" +
            string.Concat(cropsWanted.Except(cropsBuilt).Concat(cropsBuilt.Except(cropsWanted)).Take(5).Select(c => "\n         differs: " + c)));

        // Contents: each plot keeps what the save stores for it.
        var contentsWrong = new List<string>();
        var stored = new Dictionary<string, int>();
        foreach (var plot in _saved.Plots)
        {
            var want = PlotContents.Of(SavedPlot(plot), _names);
            var have = _saved.Contents.GetValueOrDefault(plot.Site.Id);
            if (have is null || !SameContents(want, have))
                contentsWrong.Add($"{plot.Site.Id} {want.Type}");
            foreach (var (item, n) in have?.StoredByItem ?? new Dictionary<string, int>())
                stored[item] = stored.GetValueOrDefault(item) + n;
        }
        var feeders = _saved.Contents.Values.Count(c => c.Feeder.PendingCount > 0);
        var ash = _saved.Contents.Values.Sum(c => c.Ash);
        Check(contentsWrong.Count == 0,
            $"plot contents on {_saved.Plots.Count} plots: stored {Describe(stored)}; {feeders} feeders with drops queued; ash {ash:F1}" +
            string.Concat(contentsWrong.Take(5).Select(w => "\n         differs: " + w)));

        // Loose actors: everything but slimes inside the zone's cells, by type.
        var looseWanted = Count(reader.Actors.Where(_saved.Zone.Contains).Select(a => _names.Item(a.TypeId))
            .Where(id => Items.KindOf(id) is not (ItemKind.Slime or ItemKind.Largo)));
        var looseSpawned = Count(_saved.SpawnedLoose.Select(s => s.Saved.Id));
        // A new game saved before anything appeared has none.
        Check((looseWanted.Count > 0 || reader.Actors.Count == 0) && Same(looseWanted, looseSpawned),
            $"loose actors in the zone: {looseSpawned.Values.Sum()} spawned ({Describe(looseSpawned)}); save {looseWanted.Values.Sum()}");

        // Produce on crops: the produce the save left growing hangs from its crop's joints, and stays there.
        var hasCycle = (string id) => _m2.Catalog.Prefabs.Has(id) && _m2.Catalog.Prefabs.Get(id).RootScript("ResourceCycle") is not null;
        var onJoints = ZoneActors.Of(reader, _saved.Zone, _names, _saved.Crops, hasCycle).Where(a => a.Joint is not null).ToList();
        var hold = _saved.Hold!;
        var hanging = hold.Hanging.ToHashSet();
        var heldSpawns = _saved.SpawnedLoose.Where(s => s.Saved.Joint is not null).ToList();
        var picked = heldSpawns.Count(s => !GodotObject.IsInstanceValid(s.Actor) || s.Actor.Consumed);
        var moved = heldSpawns.Where(s => hanging.Contains(s.Actor) && N.Vector3.Distance(Unity(s.Actor.GlobalPosition), s.Saved.Position) > 0.01f).ToList();
        // Slimes leave hanging produce alone (CropHold), so nothing is picked without the vacpack.
        var edibleHanging = hanging.Count(a => a.Edible);
        Check(hold.Count == onJoints.Count && hold.UnripeCount == onJoints.Count(a => a.Unripe) && hanging.Count == onJoints.Count && picked == 0
              && edibleHanging == 0 && moved.Count == 0,
            $"produce on crops: {hold.Count} hung ({hold.UnripeCount} unripe), {hanging.Count} still hanging ({edibleHanging} edible), {picked} eaten; " +
            $"save {onJoints.Count} ({onJoints.Count(a => a.Unripe)} unripe); {moved.Count} moved");

        // Nothing loose fell through the ground; what slimes ate is reported.
        var looseLive = _saved.SpawnedLoose.Where(s => s.Saved.Joint is null && GodotObject.IsInstanceValid(s.Actor) && !s.Actor.Consumed).ToList();
        var fell = looseLive.Where(s => s.Actor.GlobalPosition.Y < s.Saved.Position.Y - 5).ToList();
        var eaten = _saved.SpawnedLoose.Count(s => s.Saved.Joint is null) - looseLive.Count;
        Check(fell.Count == 0, $"loose actors fallen more than 5 m after {SettleSeconds} s: {fell.Count} ({looseLive.Count} still there, {eaten} eaten or sold)" +
                               string.Concat(fell.Take(5).Select(s => $"\n         {s.Saved.Id} saved at {s.Saved.Position}, now {Unity(s.Actor.GlobalPosition)}")));

        // Corral slimes: everything the save keeps in a corral, by type, and still in a corral now.
        var saved = _saved.CorralSlimes.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.Count());
        var live = _saved.Spawned.Where(GodotObject.IsInstanceValid).Where(a => !a.Consumed).ToList();
        var spawned = live.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.Count());
        var allSlimes = RanchCensus.Of(ranch).Slimes(_names).Values.Sum();
        // The independent read decides whether the corrals should hold any slimes at all.
        var corralPlots = _saved.Plots.Where(p => p.Prefab.Regions.Count > 0).ToList();
        var inCorrals = reader.Actors.Count(a => Items.KindOf(_names.Item(a.TypeId)) is ItemKind.Slime or ItemKind.Largo
                                                 && corralPlots.Any(p => p.Contains(new N.Vector3(a.Position.X, a.Position.Y, a.Position.Z))));
        Check(saved.Values.Sum() == inCorrals && Same(saved, spawned),
            $"corral slimes: {Describe(spawned)}; save {Describe(saved)} ({allSlimes} slimes in the whole save)");
        var corrals = _saved.Plots.Where(p => p.Prefab.Regions.Count > 0).ToList();
        var escaped = live.Where(a => !corrals.Any(p => p.Contains(Unity(a.GlobalPosition)))).ToList();
        Check(escaped.Count == 0, $"slimes outside a corral after {SettleSeconds} s: {escaped.Count}" +
                                  string.Concat(escaped.Take(5).Select(a => $"\n         {a.Id} at {Unity(a.GlobalPosition)}")));

        // Expansion barriers the save opened (stand-in rule; reported, not checked).
        report.AppendLine($"  info {_saved.OpenedBarriers.Count} expansion barriers opened" +
                          string.Concat(_saved.OpenedBarriers.Select(p => "\n         " + p)));

        // Money.
        Check(_m2.Wallet.Coins == ranch.Player.Money, $"money: wallet {_m2.Wallet.Coins}, save {ranch.Player.Money}");

        // Vacpack: the slots of normal play hold what the save's player carried, moods and all.
        var savedSlots = reader.Player.Ammo.GetValueOrDefault(_names.Value(GameEnum.AmmoMode, PlayerVacpack.DefaultMode)) ?? [];
        var packWrong = new List<string>();
        for (var i = 0; i < Simulation.Vacpack.TotalSlots; i++)
        {
            var want = i < savedSlots.Count ? PlayerVacpack.Slot(savedSlots[i], _names) : null;
            var have = _m2.Pack[i];
            if (want?.Id != have?.Id || want?.Count != have?.Count
                || (want is not null && have is not null && !want.Moods.OrderBy(m => m.Key).SequenceEqual(have.Moods.OrderBy(m => m.Key))))
                packWrong.Add($"slot {i + 1}: {Slot(have)}, save {Slot(want)}");
        }
        Check(packWrong.Count == 0, $"vacpack: {string.Join(", ", Enumerable.Range(0, Simulation.Vacpack.TotalSlots).Select(i => Slot(_m2.Pack[i])))}; " +
                                    $"{_m2.Pack.UsableSlots} slots usable, {_m2.Pack.MaxPerSlot} per slot" + string.Concat(packWrong.Select(w => "\n         differs: " + w)));

        // Market: every plort the market buys has the save's saturation, and today's prices are set.
        var marketWrong = new List<string>();
        var savedSaturation = reader.World.MarketSaturation.ToDictionary(kv => _names.Item(kv.Key), kv => kv.Value);
        foreach (var (id, saturation) in _m2.Market.Saturations)
            if (savedSaturation.TryGetValue(id, out var want) && want != saturation)
                marketWrong.Add($"{id}: {saturation:F3}, save {want:F3}");
        var marketKnown = savedSaturation.Keys.Count(_m2.Market.Accepts);
        var pink = _m2.Market.Accepts("PINK_PLORT") ? $"; pink plort {_m2.Market.Price("PINK_PLORT")} today" : "";
        Check(marketWrong.Count == 0 && marketKnown > 0,
            $"market: {marketKnown} of the market's {_m2.Market.Accepted.Count()} plorts carry the save's saturation " +
            $"({savedSaturation.Count} in the save){pink}" + string.Concat(marketWrong.Take(5).Select(w => "\n         differs: " + w)));

        // Player: standing where the save left them (feet; the body settles a little in 4 s) and looking the same way.
        var savedPlayer = reader.Player;
        if (_saved.Player is null || _player is null)
            report.AppendLine($"  info player saved outside the zone (region set {savedPlayer.RegionSetId} at {savedPlayer.Position}); starts at the zone's spawn");
        else
        {
            var at = Unity(_player.GlobalPosition);
            var off = N.Vector3.Distance(at, new N.Vector3(savedPlayer.Position.X, savedPlayer.Position.Y, savedPlayer.Position.Z));
            float Turn(float a, float b) => System.Math.Abs(((a - b) % 360 + 540) % 360 - 180);
            var pitch = -Mathf.RadToDeg(_player.Camera.Rotation.X);
            var yaw = -Mathf.RadToDeg(_player.Rotation.Y);
            var pitchOff = Turn(pitch, savedPlayer.Rotation.X);
            var yawOff = Turn(yaw, savedPlayer.Rotation.Y);
            Check(off < 0.5f && pitchOff < 0.01f && yawOff < 0.01f,
                $"player: at {at.X:F2}, {at.Y:F2}, {at.Z:F2} ({off:F3} m from the save), pitch {pitch:F2}, yaw {yaw:F2} " +
                $"(save {savedPlayer.Rotation.X:F2}, {savedPlayer.Rotation.Y:F2})");
        }

        // One clock: milestone 2's game time is the world clock's.
        var worldHours = ranch.WorldTime / WorldClock.SecondsPerHour;
        Check(System.Math.Abs(_m2.Clock.TotalHours - worldHours) < 1e-4 && _m2.Clock.Day == ranch.Clock.Day - 1 && _m2.Market.Day == _m2.Clock.Day,
            $"game clock: {_m2.Clock.TotalHours:F4} h, day {_m2.Clock.Day + 1}, market day {_m2.Market.Day + 1}; world clock {worldHours:F4} h ({ranch.Clock})");

        report.Append(passed ? "m3-check PASS" : "m3-check FAIL");
        return (passed, report.ToString());
    }

    private static N.Vector3 Unity(Vector3 v) => new(v.X, v.Y, -v.Z);

    private static string Slot(Simulation.VacSlot? slot) =>
        slot is null ? "empty" : $"{slot.Count} {slot.Id}" + (slot.Moods.Count > 0 ? $" ({string.Join(" ", slot.Moods.Select(m => $"{m.Key.ToLowerInvariant()} {m.Value:F2}"))})" : "");

    private static Dictionary<string, int> Count(IEnumerable<string> ids) => ids.GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());

    private static bool SameContents(PlotContents a, PlotContents b) =>
        a.Type == b.Type && a.Crop == b.Crop && a.CropDeathTime == b.CropDeathTime && a.Feeder == b.Feeder
        && a.CollectorNextTime == b.CollectorNextTime && a.Ash == b.Ash && a.SlotSelections.SequenceEqual(b.SlotSelections)
        && a.Storage.Count == b.Storage.Count
        && a.Storage.All(kv => b.Storage.TryGetValue(kv.Key, out var slots) && slots.SequenceEqual(kv.Value));

    private static bool Same(Dictionary<string, int> a, Dictionary<string, int> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var n) && n == kv.Value);

    private static string Describe(Dictionary<string, int> counts) =>
        counts.Count == 0 ? "none" : string.Join(", ", counts.OrderBy(kv => kv.Key, System.StringComparer.Ordinal).Select(kv => $"{kv.Value} {kv.Key}"));
}
