using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using OpenRanch.Formats.Scene;
using OpenRanch.Ranch;
using N = System.Numerics;

namespace OpenRanch.Game.World;

/// <summary>
/// --day-check: runs the world clock and checks, against the rules in docs/behavior/day-cycle.md,
/// day-and-night.md and world-visibility.md (worked out here again, not with the code under test):
/// the starting time; that the clock moves at the install's pace, normally and sped up; the fog,
/// ambient, sky and rig lights at four hours against the zone settings; and that every object tied to
/// the time of day switches at its window's hours, both when the clock is set and when it runs past.
/// Prints a report and quits with 0 when everything passes.
/// </summary>
public partial class DayCheck : Node
{
    private const int WarmUpFrames = 30, RateFrames = 120;
    private const double SpeedUp = 60;
    // Hours to compare the lighting at: deep night, the morning blend, midday and the evening blend.
    private static readonly float[] LightingHours = [3f, 7f, 12f, 17.5f];

    private readonly WorldTime _clock;
    private readonly WorldLighting _lighting;
    private readonly TimedObjects? _timed;
    private readonly ZoneAmbience _zone;
    private readonly bool _fromSave;
    private readonly double _start;
    private readonly StringBuilder _report = new();
    private int _failures;
    private int _frame;
    private double _rateFrom, _rateDelta;
    private double _runUntil;

    public DayCheck(WorldTime clock, WorldLighting lighting, TimedObjects? timed, ZoneAmbience zone, bool fromSave, float transition)
    {
        Name = "DayCheck";
        _clock = clock;
        _lighting = lighting;
        _timed = timed;
        _zone = zone;
        _fromSave = fromSave;
        _start = clock.Ranch.WorldTime;
        _report.AppendLine("Day check");
        _report.AppendLine($"  install: {clock.Cycle.Length.RealSecondsPerDay} real seconds per game day, " +
                           $"{clock.Cycle.Length.FastForwardRealSecondsPerDay} while sleeping; ambience transition {transition} s");
        if (fromSave)
            Note(true, $"start: the save's world time {_start:F1} ({clock.Clock})");
        else
            Note(_start == WorldClock.NewGameStart, $"start: new game at {_start:F1} ({clock.Clock}), expected {WorldClock.NewGameStart} (9:00 on day 1)");
    }

    private void Note(bool ok, string line)
    {
        if (!ok)
            _failures++;
        _report.AppendLine($"  {(ok ? "ok  " : "FAIL")} {line}");
    }

    public override void _Process(double delta)
    {
        _frame++;
        // The clock has already moved this frame (it is this node's parent), by delta x speed.
        if (_frame == WarmUpFrames)
        {
            _clock.Speed = 1;
            BeginRate();
        }
        else if (_frame > WarmUpFrames && _frame <= WarmUpFrames + RateFrames)
        {
            _rateDelta += delta;
            if (_frame == WarmUpFrames + RateFrames)
            {
                EndRate(1);
                _clock.Speed = SpeedUp;
                BeginRate();
            }
        }
        else if (_frame > WarmUpFrames + RateFrames && _frame <= WarmUpFrames + 2 * RateFrames)
        {
            _rateDelta += delta;
            if (_frame == WarmUpFrames + 2 * RateFrames)
            {
                EndRate(SpeedUp);
                _clock.Speed = 0;
                CheckLighting();
                CheckTimedSet();
                // Let the clock run through 18:00 (60 game minutes a real second at this speed).
                var day = _clock.Clock.Day;
                _clock.Set(WorldClock.At(day, 17.95));
                _runUntil = WorldClock.At(day, 18.05);
                _clock.Speed = 3600 * _clock.Cycle.Length.RealSecondsPerDay / WorldClock.SecondsPerDay;
            }
        }
        else if (_runUntil > 0 && _clock.Ranch.WorldTime >= _runUntil)
        {
            _clock.Speed = 1;
            CheckTimedNow("while running past 18:00");
            _runUntil = 0;
            _report.AppendLine(_failures == 0 ? "Day check: PASS" : $"Day check: FAIL ({_failures} failures)");
            GD.Print(_report.ToString());
            GetTree().Quit(_failures == 0 ? 0 : 1);
            SetProcess(false);
        }
    }

    private void BeginRate()
    {
        _rateFrom = _clock.Ranch.WorldTime;
        _rateDelta = 0;
    }

    // The clock should have moved delta x speed x (86,400 / secsPerGameDay) game seconds.
    private void EndRate(double speed)
    {
        var moved = _clock.Ranch.WorldTime - _rateFrom;
        var expected = _rateDelta * speed * 86400 / _clock.Cycle.Length.RealSecondsPerDay;
        Note(Math.Abs(moved - expected) <= 1e-6 * Math.Max(1, expected),
            $"rate x{speed}: {moved:F4} game s in {_rateDelta:F4} real s over {RateFrames} frames, expected {expected:F4}");
    }

    private void CheckLighting()
    {
        var day = _clock.Clock.Day;
        _report.AppendLine("        rig lights: " + string.Join(", ", _lighting.Now().Rigs.Select(r =>
            $"{r.Rig.Path} ({(r.Rig.IsNight ? "night" : "sun")}, intensity {r.Rig.Intensity})")));
        foreach (var hour in LightingHours)
        {
            _clock.Set(WorldClock.At(day, hour));
            var now = _lighting.Now();
            if (now.CaveDarkness != 0)
            {
                Note(false, $"lighting {hour:00.00}h: the camera is in a cave (darkness {now.CaveDarkness}), can't compare");
                continue;
            }
            // docs/behavior/day-and-night.md, "Blending through the day" and "Sun and night light".
            var f = hour / 24f;
            var n = Math.Clamp((MathF.Abs(0.5f - f) - 0.15f) * 5f, 0f, 1f);
            var n6 = MathF.Pow(n, 6);
            var fog = N.Vector4.Lerp(_zone.DayFogColor, _zone.NightFogColor, n);
            var density = (1 - n6) * _zone.DayFogDensity + n6 * _zone.NightFogDensity;
            var ambient = N.Vector4.Lerp(_zone.DayAmbient, _zone.NightAmbient, n);
            var sky = N.Vector4.Lerp(_zone.DaySky, _zone.NightSky, n);
            var horizon = N.Vector4.Lerp(_zone.DayHorizon, _zone.NightHorizon, n);
            float sun = 0, night = 0;
            if (f < 0.25f)
                night = Math.Clamp((0.25f - f) * 288f, 0.01f, 1f);
            else if (f > 0.75f)
                night = Math.Clamp((f - 0.75f) * 288f, 0.01f, 1f);
            else
                sun = Math.Clamp(MathF.Min(f - 0.25f, 0.75f - f) * 288f, 0.01f, 1f);

            var problems = new List<string>();
            void Same(string what, Color actual, N.Vector4 expected)
            {
                if (Math.Abs(actual.R - expected.X) > 1e-4 || Math.Abs(actual.G - expected.Y) > 1e-4 || Math.Abs(actual.B - expected.Z) > 1e-4)
                    problems.Add($"{what} {actual} vs {expected}");
            }
            Same("ambient", now.Ambient, ambient);
            Same("fog colour", now.FogColor, fog);
            Same("sky", now.SkyTop, sky);
            Same("horizon", now.SkyHorizon, horizon);
            if (Math.Abs(now.FogDensity - density) > 1e-6)
                problems.Add($"fog density {now.FogDensity} vs {density}");
            var rigs = new List<string>();
            foreach (var (rig, visible, color, basis) in now.Rigs)
            {
                var strength = rig.IsNight ? night : sun;
                var intensity = Math.Clamp(rig.Intensity * strength, strength == 0 ? 0 : 0.001f, 1f);
                if (visible != intensity > 0)
                    problems.Add($"{rig.Path} shown {visible}, intensity {intensity}");
                var want = UnityConvert.GammaMatchedLight(ambient, rig.Color, intensity);
                if (visible && (Math.Abs(color.R - want.R) > 1e-4 || Math.Abs(color.G - want.G) > 1e-4 || Math.Abs(color.B - want.B) > 1e-4))
                    problems.Add($"{rig.Path} colour {color} vs {want}");
                // The rig turns 360 x (f - 0.5) degrees about its own X axis; Unity's forward is +Z, and
                // Godot's directional lights shine along -Z, with Z mirrored between the two.
                var q = rig.RigRotation * N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, 360f * (f - 0.5f) * MathF.PI / 180f) * rig.LocalRotation;
                var forward = N.Vector3.Transform(N.Vector3.UnitZ, q);
                var shines = -basis.Z;
                if (shines.DistanceTo(new Vector3(forward.X, forward.Y, -forward.Z)) > 1e-3)
                    problems.Add($"{rig.Path} points {shines} vs {forward}");
                rigs.Add($"{(rig.IsNight ? "night" : "sun")} {(visible ? $"{intensity:F3}" : "off")}");
            }
            Note(problems.Count == 0 && now.Rigs.Count > 0,
                $"lighting {hour:00.00}h: night {n:F3}, fog {density:F5}, ambient ({ambient.X:F3}, {ambient.Y:F3}, {ambient.Z:F3}), " +
                $"{string.Join(", ", rigs)}" + (problems.Count > 0 ? "; " + string.Join("; ", problems) : ""));
        }
    }

    // Every window's edges, a game minute either side: hidden before the start, shown after it,
    // shown before the end, hidden after it.
    private void CheckTimedSet()
    {
        var groups = _timed?.Groups.ToList() ?? [];
        Note(groups.Count > 0, $"timed objects: {groups.Count} in the zone, windows " +
            string.Join(", ", groups.SelectMany(g => g.Group.Windows).GroupBy(w => w).Select(w => $"{w.Key.StartHour:0}-{w.Key.EndHour:0} x{w.Count()}")) +
            $"; {groups.Sum(g => g.Group.Renderers.Count)} renderers, {groups.Sum(g => g.Group.Lights.Count)} lamps, {groups.Sum(g => g.Group.Colliders.Count)} colliders");
        var day = _clock.Clock.Day;
        var checks = 0;
        var wrong = new List<string>();
        var minute = 1f / 60;
        foreach (var hour in groups.SelectMany(g => g.Group.Windows).SelectMany(w => new[] { w.StartHour, w.EndHour }).Distinct())
        {
            foreach (var h in new[] { hour - minute, hour + minute })
            {
                var at = (h % 24 + 24) % 24;
                _clock.Set(WorldClock.At(day, at));
                foreach (var (group, node, body) in groups)
                {
                    checks++;
                    if (TimedObjects.IsOn(node, body) != Expected(group, at))
                        wrong.Add($"{group.Path} at {at:F3}h");
                }
            }
        }
        Note(wrong.Count == 0, $"timed objects at every window edge: {checks - wrong.Count}/{checks} right" +
            (wrong.Count > 0 ? ": " + string.Join(", ", wrong.Take(5)) : ""));
        var day12 = groups.Count(g => Expected(g.Group, 12));
        var night0 = groups.Count(g => Expected(g.Group, 0));
        _report.AppendLine($"        {day12} shown at noon, {night0} at midnight");
    }

    private void CheckTimedNow(string when)
    {
        var groups = _timed?.Groups.ToList() ?? [];
        var hour = (float)_clock.Clock.Hour;
        var wrong = groups.Where(g => TimedObjects.IsOn(g.Node, g.Body) != Expected(g.Group, hour)).ToList();
        Note(groups.Count > 0 && wrong.Count == 0,
            $"timed objects {when}: at {_clock.Clock}, {groups.Count(g => TimedObjects.IsOn(g.Node, g.Body))} of {groups.Count} shown, {wrong.Count} wrong");
    }

    // docs/behavior/world-visibility.md: shown between start and end, both included, wrapping past
    // midnight when the start is later; an object under several windows needs all of them open.
    private static bool Expected(TimedGroup group, float hour) =>
        group.Windows.All(w => w.StartHour <= w.EndHour ? w.StartHour <= hour && hour <= w.EndHour : hour >= w.StartHour || hour <= w.EndHour);
}
