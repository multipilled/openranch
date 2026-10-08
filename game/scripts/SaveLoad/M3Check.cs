using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Scene;
using OpenRanch.Game.World;
using OpenRanch.Ranch;
using N = System.Numerics;

namespace OpenRanch.Game.SaveLoad;

/// <summary>
/// Milestone 3's check, run without a window (--save FILE --m3-check): The Ranch opened from a save has
/// to hold what the save reader reports. Every plot site of the zone carries the saved plot type and
/// none of the scene's own plots; the slimes the save keeps in the zone's corrals are all there, by
/// type, and still inside a corral after a few seconds of physics; the wallet holds the saved money.
/// Prints a report and quits with 0 when everything matched, 1 otherwise.
/// </summary>
public partial class M3Check : Node
{
    private const double SettleSeconds = 4;

    private readonly SavedRanch _saved;
    private readonly ZoneExtract _zone;
    private readonly Slimes.M2World _m2;
    private readonly GameEnums _names;
    private double _time;

    public M3Check(SavedRanch saved, ZoneExtract zone, Slimes.M2World m2, GameInstall install)
    {
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

        // Plots: the save reader's type for each of the zone's sites, against what the built zone holds.
        var siteIds = _saved.Plots.Select(p => p.Site.Id).ToHashSet();
        var expected = ranch.Plots.Where(p => siteIds.Contains(p.Id))
            .GroupBy(p => _names.PlotType(p.Type)).ToDictionary(g => g.Key, g => g.Count());
        var prefabTypes = _saved.Plots.Select(p => p.Prefab).DistinctBy(p => p.Name).ToDictionary(p => p.Name, p => p.Type);
        var built = _zone.Renderers.Select(r => r.Path.Split('/'))
            .Where(parts => parts.Length > 2 && siteIds.Contains(parts[0]) && prefabTypes.ContainsKey(parts[1]))
            .Select(parts => (Site: parts[0], Type: prefabTypes[parts[1]])).Distinct()
            .GroupBy(s => _names.PlotType(s.Type)).ToDictionary(g => g.Key, g => g.Count());
        Check(siteIds.Count > 0 && Same(expected, built), $"plots on the zone's {siteIds.Count} sites: built {Describe(built)}; save {Describe(expected)}");
        var leftovers = _zone.Renderers.Count(r => r.Path.Contains("/landPlot/"));
        Check(leftovers == 0, $"scene plots left standing: {leftovers} meshes");

        // Each plot has ground under its middle, at the site's height.
        var space = _m2.GetWorld3D().DirectSpaceState;
        var floorless = new List<string>();
        foreach (var plot in _saved.Plots)
        {
            var at = UnityConvert.Position(plot.Site.PlotWorld.Translation);
            var ray = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 3, at + Vector3.Down * 30, Slimes.Actor.WorldLayer);
            var hit = space.IntersectRay(ray);
            if (hit.Count == 0 || Mathf.Abs(hit["position"].AsVector3().Y - at.Y) > 1.5f)
                floorless.Add($"{plot.Site.Id} ({_names.PlotType(plot.Plot.Type)}) at {plot.Site.PlotWorld.Translation}: " +
                              (hit.Count == 0 ? "nothing below" : $"ground at {hit["position"].AsVector3().Y:F2}, {(hit["collider"].AsGodotObject() is Node n ? n.GetPath().ToString() : "?")}"));
        }
        Check(floorless.Count == 0, $"plots without ground under them: {floorless.Count}" + string.Concat(floorless.Take(5).Select(f => "\n         " + f)));

        // Corral slimes: everything the save keeps in a corral, by type, and still in a corral now.
        var saved = _saved.CorralSlimes.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.Count());
        var live = _saved.Spawned.Where(GodotObject.IsInstanceValid).Where(a => !a.Consumed).ToList();
        var spawned = live.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.Count());
        var allSlimes = RanchCensus.Of(ranch).Slimes(_names).Values.Sum();
        Check(saved.Count > 0 && Same(saved, spawned),
            $"corral slimes: {Describe(spawned)}; save {Describe(saved)} ({allSlimes} slimes in the whole save)");
        var corrals = _saved.Plots.Where(p => p.Prefab.Regions.Count > 0).ToList();
        var escaped = live.Where(a => !corrals.Any(p => p.Contains(Unity(a.GlobalPosition)))).ToList();
        Check(escaped.Count == 0, $"slimes outside a corral after {SettleSeconds} s: {escaped.Count}" +
                                  string.Concat(escaped.Take(5).Select(a => $"\n         {a.Id} at {Unity(a.GlobalPosition)}")));

        // Money.
        Check(_m2.Wallet.Coins == ranch.Player.Money, $"money: wallet {_m2.Wallet.Coins}, save {ranch.Player.Money}");

        report.Append(passed ? "m3-check PASS" : "m3-check FAIL");
        return (passed, report.ToString());
    }

    private static N.Vector3 Unity(Vector3 v) => new(v.X, v.Y, -v.Z);

    private static bool Same(Dictionary<string, int> a, Dictionary<string, int> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var n) && n == kv.Value);

    private static string Describe(Dictionary<string, int> counts) =>
        counts.Count == 0 ? "none" : string.Join(", ", counts.OrderBy(kv => kv.Key, System.StringComparer.Ordinal).Select(kv => $"{kv.Value} {kv.Key}"));
}
