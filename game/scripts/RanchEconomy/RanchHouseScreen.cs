using System;
using OpenRanch.Game.Home;

namespace OpenRanch.Game.RanchEconomy;

/// <summary>
/// A minimal ranch house screen (docs/behavior/day-cycle.md, "Sleeping"): using the house's door opens
/// it, and its sleep button sleeps until the next 6:00. The original's screen also has mail, the 7Zee
/// partner, DLC and slime appearance buttons (static analysis of <c>RanchHouseUI</c>; not built yet).
/// The screen can't be closed while sleeping. The game saves when the player wakes (static analysis:
/// the player lock that sleeping uses saves everything once it lets go), which <see cref="Economy"/>
/// does on <see cref="RanchHouse.Slept"/>. "Save" is openranch's own button, like the F5 key: the
/// original has none.
/// </summary>
public partial class RanchHouseScreen : MenuScreen, IHouseScreen
{
    private readonly RanchHouse _house;
    private readonly Action? _save;

    /// <param name="save">Saves the game; null when there is nothing to save to (no save or new game was opened), which also hides the save button.</param>
    public RanchHouseScreen(RanchHouse house, Action? save) : base("RanchHouseScreen")
    {
        _house = house;
        _save = save;
        // Waking keeps the screen open; the game pauses again until it is closed (static analysis:
        // the sleep button unpauses the time director while sleeping and pauses it on waking).
        house.Slept += (_, _) =>
        {
            if (IsOpen)
            {
                GetTree().Paused = true;
                Refresh();
            }
        };
    }

    protected override bool CanClose => !_house.Sleeping;

    public void Open() => Refresh();

    private void Refresh()
    {
        ShowScreen($"Ranch house, {_house.Clock}");
        ClearItems();
        AddItem("sleep", "Sleep until morning", !_house.Sleeping, PressSleep);
        if (_save is { } save)
            AddItem("save", "Save", !_house.Sleeping, () =>
            {
                save();
                SetStatus("Saved.");
            });
    }

    public void PressSleep()
    {
        if (_house.Sleeping)
            return;
        // The clock runs while sleeping.
        GetTree().Paused = false;
        _house.Sleep();
        Refresh();
        SetStatus("Sleeping...");
    }
}
