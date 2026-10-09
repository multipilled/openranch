using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using OpenRanch.Ranch;

namespace OpenRanch.Game.RanchEconomy;

/// <summary>
/// Milestone 6's check, run without a window (--new-game --money N --m6-check --sleep-save FILE, with
/// --day-speed to run produce quickly). Everything goes through what the player uses: the check stands
/// the player in front of an activator, looks at it, uses it and presses the menu's buttons.
///   1. Plots: on an empty site, build a corral and buy two upgrades; the money drops by the install's
///      prices, the corral and its upgrade pieces are drawn and its walls stand.
///   2. Expansions: buy one at its door; the money drops by its price, the door is open, the progress
///      is counted and the barrier is gone.
///   3. Produce and coops (<see cref="ProduceCheck"/>).
///   4. Sleep through the ranch house screen; waking saves, and the save holds what was bought.
/// Prints a report and quits with 0 when everything passed, 1 otherwise.
/// </summary>
public partial class M6Check : Node
{
    private readonly Economy _economy;
    private readonly string[] _args;
    private readonly StringBuilder _report = new("m6-check:\n");
    private bool _passed = true;
    private ProduceCheck? _produce;

    public M6Check(Economy economy, string[] args)
    {
        Name = "M6Check";
        _economy = economy;
        _args = args;
        // Menus pause the game; the check goes on.
        ProcessMode = ProcessModeEnum.Always;
    }

    public void Check(bool ok, string what)
    {
        _passed &= ok;
        _report.AppendLine($"  {(ok ? "ok  " : "FAIL")} {what}");
    }

    public void Info(string what) => _report.AppendLine($"  info {what}");

    public override void _Ready() => Callable.From(() => { _ = Run(); }).CallDeferred();

    private async Task Run()
    {
        try
        {
            await Seconds(1);
            await Plots();
            await Expansion();
            _produce = new ProduceCheck(this, _economy);
            await _produce.Run();
            await new MachinesCheck(this, _economy).Run(CorralSite, _produce.GardenSite);
            await Sleep();
        }
        catch (Exception e)
        {
            Check(false, $"the check stopped: {e}");
        }
        _report.Append(_passed ? "m6-check PASS" : "m6-check FAIL");
        GD.Print(_report.ToString());
        GetTree().Quit(_passed ? 0 : 1);
    }

    public async Task Frames(int n = 1)
    {
        for (var i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    public async Task Seconds(double seconds)
    {
        var end = Time.GetTicksMsec() + seconds * 1000;
        while (Time.GetTicksMsec() < end)
            await Frames();
    }

    /// <summary>Waits (real seconds, at most <paramref name="timeout"/>) until <paramref name="done"/> holds; returns whether it did.</summary>
    public async Task<bool> Until(Func<bool> done, double timeout)
    {
        var end = Time.GetTicksMsec() + timeout * 1000;
        while (!done())
        {
            if (Time.GetTicksMsec() > end)
                return false;
            await Frames();
        }
        return true;
    }

    private int Money => _economy.M2.Wallet.Coins;

    // Stands the player in front of the activator, a little inside reach, looking at its middle; tries
    // eight sides and a few heights until the interact ray meets it first.
    public async Task<bool> Aim(Area3D area, Func<bool>? lookingAt = null)
    {
        lookingAt ??= () => _economy.Interactor.LookedAt() == area;
        var player = _economy.Player;
        player.SetPhysicsProcess(false);
        await Frames(2);
        var middle = area.GlobalTransform * ((CollisionShape3D)area.GetChild(0)).Position;
        var eye = player.Camera.Position.Y;
        var reach = _economy.Interactor.Reach;
        foreach (var rise in new[] { 0f, 0.6f, -0.4f, 1.2f })
            for (var k = 0; k < 8; k++)
            {
                var angle = k * Mathf.Pi / 4;
                var cameraAt = middle + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (reach * 0.7f) + Vector3.Up * rise;
                player.Position = cameraAt - Vector3.Up * eye;
                var dir = (middle - cameraAt).Normalized();
                player.Look(Mathf.RadToDeg(Mathf.Atan2(-dir.X, -dir.Z)), Mathf.RadToDeg(Mathf.Asin(dir.Y)));
                await Frames();
                if (lookingAt())
                    return true;
            }
        return false;
    }

    private async Task Plots()
    {
        var plots = _economy.Plots;
        var names = _economy.Names;
        var empty = names.Value(GameEnum.PlotType, "EMPTY");
        var corral = names.Value(GameEnum.PlotType, "CORRAL");
        var site = plots.Sites.FirstOrDefault(s => s.Placed.Plot.Type == empty);
        if (site is null)
        {
            Check(false, "an empty plot site in the zone");
            return;
        }
        var area = site.Node!.GetChildren().OfType<Area3D>().FirstOrDefault();
        var aimed = area is not null && await Aim(area);
        Info($"plot activators on {site.Info.Id}: {site.Node!.GetChildren().OfType<Area3D>().Count()}, aimed {aimed}, looking at {_economy.Interactor.LookedAt()?.GetPath()}; triggers in prefab: " +
             string.Join(", ", site.Placed.Prefab.Tree!.Triggers.Select(t => $"{t.Region.Class} on={site.Placed.Prefab.Tree.IsOn(t.Chain)}")));
        var opened = aimed && _economy.Interactor.Use() == "plot" && _economy.PlotMenu.IsOpen && _economy.PlotMenu.SiteId == site.Info.Id;
        Check(opened, $"plot {site.Info.Id} (EMPTY): its activator opens its menu from {_economy.Interactor.Reach * 0.7f:F2} m ({string.Join(", ", _economy.PlotMenu.Offered)})");
        if (!opened)
            _economy.PlotMenu.Open(site.Info.Id);

        var price = plots.Catalog.BuildCost(empty, corral)!.Value;
        var before = Money;
        var pressed = _economy.PlotMenu.Press(plots.Catalog.Menu(empty)!.Replacements.First(r => r.Type == corral).MenuItem);
        await Frames(2);
        site = plots.Get(site.Info.Id)!;
        var prefab = plots.Layout.Prefabs[corral].Name;
        var walls = site.Node!.GetNodeOrNull<StaticBody3D>("PenWalls")?.GetChildCount() ?? 0;
        Check(pressed && site.Placed.Plot.Type == corral && before - Money == price && site.Renderers.Count > 0
              && site.Renderers.All(r => r.Path.StartsWith($"{site.Info.Id}/{prefab}/", StringComparison.Ordinal)) && walls > 0,
            $"built a corral: money {before} -> {Money} (the install's price {price}); {site.Renderers.Count} meshes of {prefab}, {walls} pen wall shapes");

        // Two upgrades the corral's menu sells now, in menu order.
        var bought = new List<string>();
        foreach (var offer in plots.Catalog.Menu(corral)!.Upgrades.Where(u => plots.Rules.CanUpgrade(plots.Ranch, site.Info.Id, u.Upgrade) == PlotPurchase.Done).Take(2))
        {
            before = Money;
            var meshes = site.Renderers.Count;
            pressed = _economy.PlotMenu.Press(offer.MenuItem);
            await Frames(2);
            site = plots.Get(site.Info.Id)!;
            var switched = site.Placed.Upgrades.Applied.Contains(names.PlotUpgrade(offer.Upgrade));
            Check(pressed && site.Placed.Plot.HasUpgrade(offer.Upgrade) && before - Money == offer.Cost && switched,
                $"upgrade {names.PlotUpgrade(offer.Upgrade)}: money {before} -> {Money} (price {offer.Cost}); meshes {meshes} -> {site.Renderers.Count}, " +
                $"pieces switched: {string.Join(", ", site.Placed.Upgrades.Applied)}");
            bought.Add(offer.MenuItem);
        }
        Check(bought.Count == 2, $"two upgrades bought: {string.Join(", ", bought)}");
        Check(_economy.PlotMenu.Press(plots.Catalog.Menu(corral)!.Upgrades.First(u => bought.Contains(u.MenuItem)).MenuItem) == false,
            "an upgrade already owned can't be bought again (its button is disabled)");
        _economy.PlotMenu.Close();
        CorralSite = site.Info.Id;
        await Frames(2);
    }

    /// <summary>The site the check built its corral on.</summary>
    public string? CorralSite { get; private set; }

    private string? _boughtDoor;

    private async Task Expansion()
    {
        var shop = _economy.Shop;
        var names = _economy.Names;
        var door = shop.DoorsForSale.Select(id => shop.Doors.Doors[id])
            .Where(d => Expansions.StateOf(shop.Ranch, d.Id, names) == names.Value(GameEnum.AccessDoorState, "LOCKED") && d.Cost <= Money)
            .OrderBy(d => d.Cost).FirstOrDefault();
        if (door is null)
        {
            Check(false, $"a locked expansion door in the zone affordable with {Money}");
            return;
        }
        var area = shop.GetChildren().OfType<Area3D>().FirstOrDefault(a => a.GetMeta(ExpansionShop.DoorMeta).AsString() == door.Id);
        var aimed = area is not null && await Aim(area);
        var opened = aimed && _economy.Interactor.Use() == "door" && _economy.DoorMenu.IsOpen;
        Check(opened, $"door {door.Id}: its activator opens its purchase screen");
        if (!opened)
            _economy.DoorMenu.Open(door.Id);
        var barrier = _economy.Saved.Barriers.FirstOrDefault(b => b.DoorId == door.Id);
        var standingBefore = shop.Standing.ToList();
        var before = Money;
        var saves = _economy.Saves;
        var pressed = _economy.DoorMenu.Press("buy");
        await Frames(2);
        var progress = string.Join(", ", door.Progress.Select(p => $"{names.Name(GameEnum.Progress, p)} {shop.Ranch.Player.ProgressOf(p)}"));
        Check(pressed && before - Money == door.Cost && Expansions.StateOf(shop.Ranch, door.Id, names) == names.Value(GameEnum.AccessDoorState, "OPEN")
              && door.Progress.All(p => shop.Ranch.Player.ProgressOf(p) > 0) && !_economy.DoorMenu.IsOpen,
            $"bought {door.Id}: money {before} -> {Money} (price {door.Cost}), door OPEN, progress {progress}");
        var lifted = standingBefore.Except(shop.Standing).ToList();
        Check(barrier is not null && lifted.Contains(barrier.Path),
            $"barriers lifted: {string.Join(", ", lifted)}; {shop.Standing.Count()} still standing");
        Check(_economy.Saves == saves + 1, $"the game saved after the purchase ({_economy.SavePath})");
        _boughtDoor = door.Id;
    }

    private async Task Sleep()
    {
        var house = _economy.House;
        if (house is null || _economy.HouseScreen is null)
        {
            Check(false, "the ranch house's door in the zone");
            return;
        }
        var door = house.GetChildren().OfType<Area3D>().First();
        var aimed = await Aim(door, house.LookingAtDoor);
        var used = aimed && house.Interact() && _economy.HouseScreen.IsOpen && GetTree().Paused;
        Check(used, "the ranch house's door opens its screen, and the game pauses");
        if (!used)
            _economy.HouseScreen.Open();
        var saves = _economy.Saves;
        var from = _economy.Clock.Ranch.WorldTime;
        var wake = _economy.Clock.Clock.WakeTime;
        var pressed = _economy.HouseScreen.Press("sleep");
        var slept = pressed && await Until(() => !house.Sleeping && _economy.Saves > saves, 120);
        Check(slept && _economy.Clock.Ranch.WorldTime == wake && GetTree().Paused,
            $"slept from {new WorldClock(from)} to {_economy.Clock.Clock} (the next 6:00 is {new WorldClock(wake)}); the screen stays open, paused");
        Check(_economy.Saves == saves + 1, $"waking saved {_economy.SavePath}");
        _economy.HouseScreen.Close();
        Check(!GetTree().Paused, "closing the screen unpauses");

        // The save holds what was bought, read back on its own.
        var read = RanchFiles.Read(_economy.SavePath);
        var names = _economy.Names;
        var plot = CorralSite is null ? null : read.FindPlot(CorralSite);
        var live = CorralSite is null ? null : _economy.Plots.Ranch.FindPlot(CorralSite);
        Check(plot is not null && live is not null && plot.Type == live.Type && plot.Upgrades.SequenceEqual(live.Upgrades),
            $"the save's plot {CorralSite}: {(plot is null ? "missing" : $"{names.PlotType(plot.Type)} with {string.Join(", ", plot.Upgrades.Select(names.PlotUpgrade))}")}");
        Check(_boughtDoor is not null && read.AccessDoors.GetValueOrDefault(_boughtDoor) == names.Value(GameEnum.AccessDoorState, "OPEN")
              && _economy.Shop.Doors.Doors[_boughtDoor].Progress.All(p => read.Player.ProgressOf(p) == _economy.Plots.Ranch.Player.ProgressOf(p)),
            $"the save's door {_boughtDoor} is open and its progress counted");
        Check(read.Player.Money == Money && Math.Abs(read.WorldTime - _economy.Clock.Ranch.WorldTime) < 1,
            $"the save's money {read.Player.Money} (wallet {Money}) and world time {new WorldClock(read.WorldTime)}");
        Check(read.Plots.Count == _economy.Plots.Ranch.Plots.Count, $"the save lists {read.Plots.Count} plot sites");
        _produce?.CheckSave(read);
    }
}
