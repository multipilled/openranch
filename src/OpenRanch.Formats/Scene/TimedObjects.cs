namespace OpenRanch.Formats.Scene;

/// <summary>
/// The hours an object tied to the time of day shows, from the world scene's
/// <c>EnableOnlyDuringTimeWindow</c> scripts (docs/behavior/world-visibility.md).
/// </summary>
public readonly record struct TimeWindow(float StartHour, float EndHour)
{
    /// <summary>
    /// Whether the object shows at <paramref name="hour"/> (0 to 24): between the two hours, both
    /// included, wrapping past midnight when the start is later than the end (static analysis:
    /// EnableOnlyDuringTimeWindow.Update compares TimeDirector.CurrHour with both ends every frame).
    /// </summary>
    public bool IsOpen(float hour) =>
        StartHour <= hour && hour <= EndHour || StartHour > EndHour && (hour >= StartHour || hour <= EndHour);
}

/// <summary>
/// What one object tied to the time of day draws, collides with and lights, with every window it sits
/// under (a window's object can hold another's). It shows only while all of its windows are open.
/// Listed by <see cref="ZoneExtractor.Extract"/> when <see cref="WorldState.RunningClock"/> is set.
/// </summary>
public sealed record TimedGroup(string Path, IReadOnlyList<TimeWindow> Windows, List<RenderItem> Renderers,
    List<ColliderItem> Colliders, List<LightItem> Lights)
{
    public bool IsShown(float hour) => Windows.All(w => w.IsOpen(hour));
}
