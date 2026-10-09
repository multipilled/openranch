using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Ranch;

/// <summary>
/// An access door of the world scene (an <c>AccessDoor</c> script or one derived from it, such as the
/// Lab's <c>LabAccessDoor</c>): its saved id, the price its <c>doorPurchase</c> asks (0 for doors
/// that aren't sold, such as the slime gates), the Slimepedia entry it names (<c>lockedRegionId</c>),
/// the progress counters it records once unlocked (<see cref="GameEnum.Progress"/>), the doors it
/// unlocks along with it (<c>linkedDoors</c>), and the game object it sits on (path id in the scene).
/// </summary>
public sealed record ExpansionDoor(string Id, string Class, int Cost, int LockedRegionId, IReadOnlyList<int> Progress,
    IReadOnlyList<string> LinkedDoorIds, long GameObject);

/// <summary>What happened when the player tried to buy an expansion.</summary>
public enum ExpansionPurchase
{
    Done,
    NoSuchDoor,
    /// <summary>The door isn't sold (its <c>doorPurchase</c> costs nothing: slime gates and the like).</summary>
    NotForSale,
    /// <summary>The door isn't locked any more: using it just opens it.</summary>
    AlreadyUnlocked,
    NotEnoughMoney,
}

/// <summary>
/// The ranch expansions (the Lab, the Grotto, the Overgrowth and the Docks) and the other access doors
/// of the world scene, read from the player's install, and buying them. The rules are code-only facts
/// from static analysis of <c>AccessDoorUI</c> and <c>AccessDoor</c>; see docs/behavior/plots.md,
/// "Ranch expansions".
/// </summary>
public sealed class Expansions
{
    private readonly Dictionary<string, ExpansionDoor> _doors;

    public Expansions(IEnumerable<ExpansionDoor> doors) => _doors = doors.ToDictionary(d => d.Id);

    /// <summary>Every access door of the scene, by id.</summary>
    public IReadOnlyDictionary<string, ExpansionDoor> Doors => _doors;

    /// <summary>The doors sold through their purchase screen: those whose <c>doorPurchase</c> has a price.</summary>
    public IEnumerable<ExpansionDoor> ForSale => _doors.Values.Where(d => d.Cost > 0);

    /// <summary>The state a door has in a ranch: the saved one, or locked, which every door of a new game is (all 16 are LOCKED in the development PC's first Game1 save).</summary>
    public static int StateOf(RanchState ranch, string doorId, IGameNames names) =>
        ranch.AccessDoors.TryGetValue(doorId, out var state) ? state : names.Value(GameEnum.AccessDoorState, "LOCKED");

    /// <summary>
    /// Buys the expansion behind <paramref name="doorId"/>. Static analysis of <c>AccessDoorUI</c>: a
    /// locked door offers itself for its <c>doorPurchase.cost</c>, with no other condition; buying
    /// spends the price, sets the door OPEN and every linked door that is still LOCKED to CLOSED. Each
    /// door that changes recounts the progress it records (<see cref="Recount"/>).
    /// </summary>
    public ExpansionPurchase Buy(RanchState ranch, string doorId, IGameNames names, IPurse? purse = null)
    {
        if (!_doors.TryGetValue(doorId, out var door))
            return ExpansionPurchase.NoSuchDoor;
        if (door.Cost <= 0)
            return ExpansionPurchase.NotForSale;
        var locked = names.Value(GameEnum.AccessDoorState, "LOCKED");
        if (StateOf(ranch, doorId, names) != locked)
            return ExpansionPurchase.AlreadyUnlocked;
        if (purse is not null ? !purse.TrySpend(door.Cost) : ranch.Player.Money < door.Cost)
            return ExpansionPurchase.NotEnoughMoney;
        if (purse is null)
            ranch.Player.Money -= door.Cost;

        ranch.AccessDoors[doorId] = names.Value(GameEnum.AccessDoorState, "OPEN");
        Recount(ranch, door, names);
        foreach (var linked in door.LinkedDoorIds)
        {
            if (StateOf(ranch, linked, names) != locked || !_doors.TryGetValue(linked, out var other))
                continue;
            ranch.AccessDoors[linked] = names.Value(GameEnum.AccessDoorState, "CLOSED");
            Recount(ranch, other, names);
        }
        return ExpansionPurchase.Done;
    }

    /// <summary>
    /// Static analysis of <c>AccessDoor.MaybeRecountProgress</c>: when a door's state changes and it
    /// isn't locked, each progress counter it lists is set to the number of doors listing that counter
    /// that aren't locked (counted, not added to).
    /// </summary>
    public void Recount(RanchState ranch, ExpansionDoor door, IGameNames names)
    {
        var locked = names.Value(GameEnum.AccessDoorState, "LOCKED");
        if (StateOf(ranch, door.Id, names) == locked)
            return;
        foreach (var type in door.Progress)
            ranch.Player.Progress[type] = _doors.Values.Count(d => d.Progress.Contains(type) && StateOf(ranch, d.Id, names) != locked);
    }

    /// <summary>Every access door of the world scene, whichever area it is in.</summary>
    public static Expansions Read(GameScripts scripts, SerializedFile scene)
    {
        var ids = SceneIds.Read(scripts, scene);
        var found = new List<(ExpansionDoor Door, List<long> Linked)>();
        foreach (var (asset, script) in scripts.All())
        {
            if (asset.File != scene || script.ScriptClass is not { } cls || script.Data is not { } data
                || !(cls == "AccessDoor" || scripts.Types.Find(GameScripts.GameAssembly, "", cls)?.DerivesFrom("AccessDoor") == true)
                || !ids.TryGetValue(asset.PathId, out var id))
                continue;
            var cost = data.Object("doorPurchase") is { } purchase && purchase.Has("cost") ? Convert.ToInt32(purchase["cost"]) : 0;
            var linked = data.List("linkedDoors").OfType<PPtr>().Select(p => scripts.Assets.Resolve(scene, p)).OfType<AssetRef>().Select(r => r.PathId).ToList();
            var go = scripts.Assets.Resolve(scene, script.GameObject)?.PathId ?? 0;
            found.Add((new ExpansionDoor(id, cls, cost, Convert.ToInt32(data["lockedRegionId"]), data.List("progress").Select(Convert.ToInt32).ToList(), [], go), linked));
        }
        return new Expansions(found.Select(f => f.Door with { LinkedDoorIds = f.Linked.Where(ids.ContainsKey).Select(l => ids[l]).ToList() }));
    }
}

/// <summary>A new game's ranch, as the original starts one (docs/behavior/plots.md, "A new game").</summary>
public static class NewRanch
{
    /// <summary>
    /// A new game: the clock at 9:00 on day 1; the money of the classic game mode's settings
    /// (<see cref="InitialMoney"/>), also counted as earned (static analysis of the player model's new
    /// game reset); on every site of <paramref name="layout"/> the plot the scene places there, with
    /// nothing planted; every access door locked; the player in the ranch's region set (HOME).
    /// </summary>
    public static RanchState Create(PlotLayout layout, Expansions doors, int money, IGameNames names, string gameName = "openranch_new")
    {
        var none = names.Value(GameEnum.SpawnResource, "NONE");
        var locked = names.Value(GameEnum.AccessDoorState, "LOCKED");
        var ranch = new RanchState
        {
            GameName = gameName,
            DisplayName = gameName,
            Player = { Money = money, MoneyEverCollected = money, RegionSetId = names.Value(GameEnum.RegionSet, "HOME") },
            Plots = layout.Sites.Select(s => new Plot { Id = s.Id, Type = s.SceneType, AttachedResource = none }).ToList(),
        };
        foreach (var id in doors.Doors.Keys)
            ranch.AccessDoors[id] = locked;
        return ranch;
    }

    /// <summary>The newbucks a new classic game starts with: <c>GameModeConfig.classicSettings.initCurrency</c> in the world scene.</summary>
    public static int InitialMoney(GameScripts scripts)
    {
        foreach (var (config, data) in scripts.OfClass("GameModeConfig"))
            if (data.Data?["classicSettings"] is PPtr settings && scripts.Follow(config.File, settings) is { } s && s.Data.Data?.Has("initCurrency") == true)
                return Convert.ToInt32(s.Data.Data["initCurrency"]);
        throw new InvalidDataException("The classic game mode's settings weren't found.");
    }
}
