using System;
using Godot;
using OpenRanch.Formats.Unity.Managed;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// Night-only slimes (<c>DestroyOutsideHoursOfDay</c>, phosphors): a clock started when the slime
/// appears, stopped while it is in a cave (or under anything else with a cave trigger), restarted
/// when it leaves; when the clock runs out the slime vanishes (docs/behavior/slime-traits.md, "Night only").
/// </summary>
public sealed class NightOnly : SlimeBehaviour
{
    private readonly HoursWindow _window;
    private bool _started, _inCave;

    public NightOnly(SerializedObject data)
    {
        float F(string field) => data[field] is float v ? v : 0f;
        _window = new HoursWindow(F("startHour"), F("endHour"), F("minEndureHoursOutsideWindow"), F("maxEndureHoursOutsideWindow"));
    }

    public HoursWindow Window => _window;
    /// <summary>The total game hour it vanishes at (infinity while in a cave).</summary>
    public double ShutdownAt { get; private set; } = double.PositiveInfinity;
    /// <summary>Raised just before it vanishes, with the game hour.</summary>
    public event Action<double>? Vanishing;

    public override void Tick(float delta)
    {
        var now = Catalog.Clock.TotalHours;
        var cave = Catalog.InCave?.Invoke(Slime.GlobalPosition) ?? false;
        if (!_started || cave != _inCave)
        {
            _started = true;
            _inCave = cave;
            ShutdownAt = cave ? double.PositiveInfinity : OutsideHours.ShutdownAt(now, _window, Slime.Random.NextDouble());
        }
        if (now < ShutdownAt)
            return;
        Vanishing?.Invoke(now);
        Slime.Consume(); // openranch draws no vanishing effect yet
    }
}
