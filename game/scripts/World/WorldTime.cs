using System;
using System.Globalization;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Scene;
using OpenRanch.Ranch;
using DayCycle = OpenRanch.Ranch.DayCycle;

namespace OpenRanch.Game.World;

/// <summary>
/// The running world clock (docs/behavior/day-cycle.md): moves the ranch's world time on in real time
/// at the install's pace and shows the hour through the lighting, the objects tied to the time of day
/// and a clock on the screen. It stops while the game is paused. Command-line options after "--":
///   --hour H          start at hour H (0 to 24) of the starting day instead of the save's or 9:00
///   --day-speed X     run the clock X times faster than the install's pace (for testing)
///   --day-check       check the clock, the lighting and the timed objects, print a report, quit
/// </summary>
public partial class WorldTime : Node
{
    private readonly WorldLighting _lighting;
    private readonly TimedObjects? _timed;
    private readonly Label _label;

    private WorldTime(RanchState ranch, DayLength length, WorldLighting lighting, TimedObjects? timed, double speed)
    {
        Name = "WorldTime";
        Ranch = ranch;
        Cycle = new DayCycle(length);
        Speed = speed;
        _lighting = lighting;
        _timed = timed;
        // openranch's own clock readout, top right (the original's HUD clock is not drawn yet).
        var layer = new CanvasLayer { Name = "Clock" };
        _label = new Label
        {
            AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -200, OffsetRight = -24, OffsetTop = 16,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _label.AddThemeFontSizeOverride("font_size", 22);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(_label);
        AddChild(layer);
        Show();
    }

    /// <summary>The ranch whose world time this clock moves (the save's, or a new game's).</summary>
    public RanchState Ranch { get; }

    public DayCycle Cycle { get; }

    /// <summary>How many times faster than the install's pace the clock runs (1 in normal play).</summary>
    public double Speed { get; set; }

    public WorldClock Clock => Ranch.Clock;

    /// <summary>
    /// Starts the clock. <paramref name="scripts"/> is an open reader of the install's scripts if one is
    /// at hand (reading them all is slow); otherwise one is opened for the day length and closed again.
    /// </summary>
    public static WorldTime Create(GameInstall install, GameScripts? scripts, RanchState ranch, WorldLighting lighting,
        TimedObjects? timed, ZoneAmbience? ambience, bool fromSave, string[] args)
    {
        var own = scripts is null ? new GameScripts(install) : null;
        try
        {
            var reader = scripts ?? own!;
            var length = DayLength.Read(reader);
            // How long the ambience takes to change zone, also used for entering and leaving caves
            // (static analysis: AmbianceDirector moves caveDarkness by deltaTime / zoneSettingTransitionTime).
            if (reader.OfClass("AmbianceDirector").Select(a => a.Data.Data!.Get<float>("zoneSettingTransitionTime"))
                    .FirstOrDefault(s => s > 0) is var transition and > 0)
                lighting.TransitionSeconds = transition;
            string? Arg(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            var speed = double.TryParse(Arg("--day-speed"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s >= 0 ? s : 1;
            var clock = new WorldTime(ranch, length, lighting, timed, speed);
            if (Array.IndexOf(args, "--day-check") >= 0 && ambience is not null)
                clock.AddChild(new DayCheck(clock, lighting, timed, ambience, fromSave, lighting.TransitionSeconds));
            GD.Print($"World clock: {ranch.Clock} (world time {ranch.WorldTime:F0}), {length.RealSecondsPerDay} real seconds per game day" +
                     (speed != 1 ? $", running {speed}x" : ""));
            return clock;
        }
        finally
        {
            own?.Dispose();
        }
    }

    /// <summary>Applies --hour H to a ranch about to open: the same day, at hour H.</summary>
    public static void ApplyStartHour(RanchState ranch, string[] args)
    {
        var i = Array.IndexOf(args, "--hour");
        if (i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var hour)
            && hour is >= 0 and < 24)
            ranch.WorldTime = WorldClock.At(ranch.Clock.Day, hour);
    }

    /// <summary>Puts the clock at <paramref name="worldTime"/> and shows it at once.</summary>
    public void Set(double worldTime)
    {
        Ranch.WorldTime = worldTime;
        Show();
    }

    public override void _Process(double delta)
    {
        Cycle.Advance(Ranch, delta * Speed);
        Show();
    }

    private void Show()
    {
        var hour = (float)Clock.Hour;
        _lighting.SetHour(hour);
        _timed?.SetHour(hour);
        _label.Text = Clock.ToString();
    }
}
