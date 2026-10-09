using System.Text.Json.Serialization;

namespace OpenRanch.Ranch;

// The state of one ranch game, as openranch keeps it in memory and in its own save files
// (docs/formats/openranch-saves.md). Values of the game's enums are stored as the game's own
// numbers; the comment on each one names the enum (see GameEnum), and GameEnums resolves names.

/// <summary>A position or a rotation (Euler angles in degrees), in the original's world units.</summary>
public readonly record struct Vec3(float X, float Y, float Z);

/// <summary>Everything about one ranch game: the clock, the rancher, the plots, the creatures and items lying about, and the world.</summary>
public sealed class RanchState
{
    /// <summary>The file-name part of the game, such as "20220508105930_Game2" for the original's saves.</summary>
    public string GameName { get; set; } = "";
    /// <summary>The name the player gave the ranch.</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>The game version that last wrote this ranch, for example "1.4.4" for an imported save.</summary>
    public string GameVersion { get; set; } = "";
    /// <summary><see cref="GameEnum.GameMode"/>.</summary>
    public int GameMode { get; set; }
    /// <summary>The item shown as the ranch's icon in the save menu (<see cref="GameEnum.ItemId"/>).</summary>
    public int GameIconId { get; set; }

    /// <summary>Game seconds since midnight before day 1. See <see cref="WorldClock"/>.</summary>
    public double WorldTime { get; set; } = WorldClock.NewGameStart;

    [JsonIgnore]
    public WorldClock Clock => new(WorldTime);

    public Rancher Player { get; set; } = new();

    /// <summary>Every land plot site on the ranch, built on or empty, in stored order.</summary>
    public List<Plot> Plots { get; set; } = [];

    /// <summary>The doors to the ranch's expansions by door id (<see cref="GameEnum.AccessDoorState"/>).</summary>
    public Dictionary<string, int> AccessDoors { get; set; } = [];

    /// <summary>The chosen colour palette for each paintable part of the ranch (<see cref="GameEnum.PaletteType"/> to <see cref="GameEnum.Palette"/>).</summary>
    public Dictionary<int, int> Palettes { get; set; } = [];

    /// <summary>
    /// For ranch areas the player has left, the world time when they were last simulated, by area
    /// (cell) id. When the player comes back, the area catches up from that time.
    /// </summary>
    public Dictionary<string, double> AreaCatchUpTimes { get; set; } = [];

    /// <summary>Slimes, animals, food, plorts and other loose things in the world.</summary>
    public List<Actor> Actors { get; set; } = [];

    public Slimepedia Pedia { get; set; } = new();

    /// <summary>Gadgets standing on gadget sites, by site id.</summary>
    public Dictionary<string, PlacedGadget> PlacedGadgets { get; set; } = [];

    public WorldState World { get; set; } = new();

    public SlimeAppearances Appearances { get; set; } = new();

    public Instruments Instruments { get; set; } = new();

    /// <summary>The plot with this site id, or null.</summary>
    public Plot? FindPlot(string id) => Plots.FirstOrDefault(p => p.Id == id);
}

/// <summary>The player: money, health, position, upgrades, vacpack, mail, progress and owned gadgets.</summary>
public sealed class Rancher
{
    public int Health { get; set; }
    public int Energy { get; set; }
    public int Radiation { get; set; }
    /// <summary>Newbucks on hand.</summary>
    public int Money { get; set; }
    /// <summary>Slime keys on hand.</summary>
    public int Keys { get; set; }
    /// <summary>Newbucks earned over the whole game.</summary>
    public int MoneyEverCollected { get; set; }

    public Vec3 Position { get; set; }
    public Vec3 Rotation { get; set; }

    /// <summary>Personal upgrades bought (<see cref="GameEnum.PlayerUpgrade"/>), in the order stored.</summary>
    public List<int> Upgrades { get; set; } = [];
    /// <summary>Personal upgrades offered for sale (<see cref="GameEnum.PlayerUpgrade"/>).</summary>
    public List<int> AvailableUpgrades { get; set; } = [];
    /// <summary>Upgrades that are held back for a while (<see cref="GameEnum.PlayerUpgrade"/>).</summary>
    public Dictionary<int, TimedLock> UpgradeLocks { get; set; } = [];

    /// <summary>Vacpack contents for each vacpack mode (<see cref="GameEnum.AmmoMode"/>), one entry per slot.</summary>
    public Dictionary<int, List<AmmoSlot>> Ammo { get; set; } = [];

    public List<Mail> Mail { get; set; } = [];

    /// <summary>Progress counters (<see cref="GameEnum.Progress"/>), such as ranch expansions bought.</summary>
    public Dictionary<int, int> Progress { get; set; } = [];
    /// <summary>World times at which delayed progress happens (<see cref="GameEnum.ProgressTracker"/>).</summary>
    public Dictionary<int, double> DelayedProgress { get; set; } = [];

    /// <summary>Gadget blueprints owned (<see cref="GameEnum.GadgetId"/>).</summary>
    public List<int> Blueprints { get; set; } = [];
    public List<int> AvailableBlueprints { get; set; } = [];
    public Dictionary<int, TimedLock> BlueprintLocks { get; set; } = [];
    /// <summary>Built gadgets waiting in the inventory, by gadget (<see cref="GameEnum.GadgetId"/>).</summary>
    public Dictionary<int, int> Gadgets { get; set; } = [];
    /// <summary>Crafting materials held, by item (<see cref="GameEnum.ItemId"/>).</summary>
    public Dictionary<int, int> CraftMaterials { get; set; } = [];

    /// <summary>Which world the player is in (<see cref="GameEnum.RegionSet"/>): the Far, Far Range or the Slimulations.</summary>
    public int RegionSetId { get; set; }
    /// <summary>Zones whose map has been unlocked (<see cref="GameEnum.Zone"/>).</summary>
    public List<int> UnlockedZoneMaps { get; set; } = [];
    /// <summary>When the game was finished (the credits), if it has been.</summary>
    public double? EndGameTime { get; set; }

    public Decorizer? Decorizer { get; set; }

    public int ProgressOf(int progressType) => Progress.GetValueOrDefault(progressType);
}

/// <summary>A lock on an upgrade or blueprint: either until a set world time, or until something else unlocks it.</summary>
public sealed record TimedLock(bool Timed, double LockedUntil);

/// <summary>One slot of a vacpack, silo or gadget: the item (<see cref="GameEnum.ItemId"/>), how many, and for slimes their moods.</summary>
public sealed record AmmoSlot(int Id, int Count)
{
    /// <summary>Moods of the slimes held (<see cref="GameEnum.Emotion"/>).</summary>
    public Dictionary<int, float> Emotions { get; init; } = [];
}

/// <summary>A letter in the ranch house mailbox.</summary>
public sealed record Mail(int Type, string Key, bool IsRead);

/// <summary>
/// One land plot site. <see cref="Type"/> says what is built on it (<see cref="GameEnum.PlotType"/>:
/// empty, corral, coop, garden, silo, pond or incinerator); see docs/behavior/plots.md.
/// </summary>
public sealed class Plot
{
    /// <summary>The site id, shared with the world scene (for example "plot1234567890").</summary>
    public string Id { get; set; } = "";
    /// <summary><see cref="GameEnum.PlotType"/>.</summary>
    public int Type { get; set; }
    /// <summary>Upgrades bought for this plot (<see cref="GameEnum.PlotUpgrade"/>), in the order stored.</summary>
    public List<int> Upgrades { get; set; } = [];

    /// <summary>What grows in a garden (<see cref="GameEnum.SpawnResource"/>; the "none" value when nothing is planted).</summary>
    public int AttachedResource { get; set; }
    /// <summary>When the planted crop dies, as world time.</summary>
    public double AttachedDeathTime { get; set; }

    public FeederState Feeder { get; set; } = new();
    /// <summary>When the plort collector next empties the corral, as world time.</summary>
    public double CollectorNextTime { get; set; }

    /// <summary>Silo contents per storage kind (<see cref="GameEnum.SiloStorage"/>), one entry per slot.</summary>
    public Dictionary<int, List<AmmoSlot>> Silo { get; set; } = [];
    /// <summary>Which slot each of the silo's buttons has selected.</summary>
    public List<int> SiloSlotSelections { get; set; } = [];
    /// <summary>Ash collected in an incinerator's ash trough.</summary>
    public float AshUnits { get; set; }

    public bool HasUpgrade(int upgrade) => Upgrades.Contains(upgrade);
}

/// <summary>A plot's auto-feeder: when it next drops food, how many drops are queued, and its speed (<see cref="GameEnum.FeedSpeed"/>).</summary>
public sealed record FeederState(double NextTime = 0, int PendingCount = 0, int Speed = 0);

/// <summary>A loose thing in the world: a slime, animal, food, plort, crafting material and so on.</summary>
public sealed class Actor
{
    /// <summary>Unique id within the game.</summary>
    public long ActorId { get; set; }
    /// <summary>What it is (<see cref="GameEnum.ItemId"/>).</summary>
    public int TypeId { get; set; }
    public Vec3 Position { get; set; }
    public Vec3 Rotation { get; set; }
    /// <summary>Slime moods (<see cref="GameEnum.Emotion"/>), each from 0 to 1.</summary>
    public Dictionary<int, float> Emotions { get; set; } = [];
    /// <summary>World times when it changes: grows up (chicks), lays (hens), or disappears (plorts, food).</summary>
    public double TransformTime { get; set; }
    public double ReproduceTime { get; set; }
    public double DestroyTime { get; set; }
    /// <summary>Ripeness of produce (<see cref="GameEnum.ResourceCycleState"/>) and when its stage started.</summary>
    public int CycleState { get; set; }
    public double CycleProgressTime { get; set; }
    public double? DisabledAtTime { get; set; }
    public bool IsFeral { get; set; }
    /// <summary>Fashions worn (<see cref="GameEnum.ItemId"/>).</summary>
    public List<int> Fashions { get; set; } = [];
    public bool IsGlitch { get; set; }
    /// <summary><see cref="GameEnum.RegionSet"/>.</summary>
    public int RegionSetId { get; set; }
}

/// <summary>The Slimepedia: unlocked entries (by entry name, as the original stores them) and tutorials seen.</summary>
public sealed class Slimepedia
{
    public List<string> Unlocked { get; set; } = [];
    public List<string> CompletedTutorials { get; set; } = [];
    /// <summary>Entries unlocked but not yet announced with a popup.</summary>
    public List<string> PopupQueue { get; set; } = [];
    /// <summary>How many unlocks have already counted toward progress.</summary>
    public int ProgressGivenForCount { get; set; }

    public bool IsUnlocked(string entry) => Unlocked.Contains(entry);
}

/// <summary>A gadget built on a gadget site.</summary>
public sealed class PlacedGadget
{
    /// <summary><see cref="GameEnum.GadgetId"/>.</summary>
    public int GadgetId { get; set; }
    public float YRotation { get; set; }
    /// <summary>For linked pairs such as teleporters, whether this is the first of the pair.</summary>
    public bool IsPrimaryInLink { get; set; }
    /// <summary>Contents (warp depots, feeders and the like).</summary>
    public List<AmmoSlot> Ammo { get; set; } = [];
    public int ExtractorCyclesRemaining { get; set; }
    public int ExtractorQueuedToProduce { get; set; }
    public double ExtractorCycleEndTime { get; set; }
    public double ExtractorNextProduceTime { get; set; }
    public double WaitForChargeupTime { get; set; }
    public double LastSpawnTime { get; set; }
    /// <summary>Bait in a lure (<see cref="GameEnum.ItemId"/>).</summary>
    public int BaitId { get; set; }
    /// <summary>A gordo snared by a gordo snare (<see cref="GameEnum.ItemId"/>) and how much it has eaten.</summary>
    public int GordoId { get; set; }
    public int GordoEatenCount { get; set; }
    public List<int> Fashions { get; set; } = [];
    public DroneGadget? Drone { get; set; }
}

/// <summary>A drone station, its drone and its programs.</summary>
public sealed class DroneGadget
{
    public Drone? Drone { get; set; }
    public DroneStation? Station { get; set; }
    public List<DroneProgram> Programs { get; set; } = [];
}

public sealed class Drone
{
    public Vec3? Position { get; set; }
    public Vec3? Rotation { get; set; }
    public AmmoSlot? Ammo { get; set; }
    public List<int> Fashions { get; set; } = [];
    public bool NoClip { get; set; }
}

/// <summary>A drone station; <see cref="BatteryTime"/> is when its battery runs out, if one is fitted.</summary>
public sealed record DroneStation(double? BatteryTime);

/// <summary>One drone program: what to carry, where from and where to, by the original's component names.</summary>
public sealed record DroneProgram(string Target, string Source, string Destination);

/// <summary>The decorizer's stored decorations and its per-slot settings.</summary>
public sealed class Decorizer
{
    /// <summary>Count of each stored item (<see cref="GameEnum.ItemId"/>).</summary>
    public Dictionary<int, int> Contents { get; set; } = [];
    /// <summary>The item selected for each decorizer setting, or null (<see cref="GameEnum.ItemId"/>).</summary>
    public Dictionary<string, int?> Settings { get; set; } = [];
}

/// <summary>World state outside the ranch that progress depends on.</summary>
public sealed class WorldState
{
    /// <summary>Seed for the plort market's daily price swings.</summary>
    public float EconomySeed { get; set; }
    /// <summary>How saturated the market is with each plort (<see cref="GameEnum.ItemId"/>). See docs/behavior/plort-market.md.</summary>
    public Dictionary<int, float> MarketSaturation { get; set; } = [];
    /// <summary>Gordos by id: how much each has eaten (a popped gordo is missing from the world, not from here).</summary>
    public Dictionary<string, Gordo> Gordos { get; set; } = [];
    public Dictionary<string, TreasurePod> TreasurePods { get; set; } = [];
    /// <summary>Switch states by id (<see cref="GameEnum.SwitchState"/>).</summary>
    public Dictionary<string, int> Switches { get; set; } = [];
    public Dictionary<string, bool> PuzzleSlotsFilled { get; set; } = [];
    public Dictionary<string, bool> TeleporterActivations { get; set; } = [];
    public Dictionary<string, bool> OccupiedPhaseSites { get; set; } = [];
    public Dictionary<string, bool> OasisStates { get; set; } = [];
    public List<string> ActiveGingerPatches { get; set; } = [];
    /// <summary>Echo note gordos by id (<see cref="GameEnum.EchoNoteGordoState"/>), null when the original stored no state.</summary>
    public Dictionary<string, int?> EchoNoteGordos { get; set; } = [];
    /// <summary>
    /// Each crop's clock (the original's <c>resourceSpawnerWater</c>), keyed by where the crop stands: when
    /// it next grows produce and the water it has stored. See docs/behavior/produce.md.
    /// </summary>
    public List<CropTimes> ResourceSpawners { get; set; } = [];
}

/// <summary>A crop's clock: where it stands (its spawner's position), when it next grows produce (world time) and its stored water.</summary>
public sealed record CropTimes(Vec3 Position, double NextSpawnTime, float Water);

public sealed record Gordo(int EatenCount, List<int> Fashions);

/// <summary>A treasure pod: whether it is locked, open and so on (<see cref="GameEnum.TreasurePodState"/>), and the items it still has to give.</summary>
public sealed record TreasurePod(int State, List<int> SpawnQueue);

/// <summary>Slime appearances (secret styles) unlocked and chosen, per slime (<see cref="GameEnum.ItemId"/> to <see cref="GameEnum.Appearance"/>).</summary>
public sealed class SlimeAppearances
{
    public Dictionary<int, List<int>> Unlocked { get; set; } = [];
    public Dictionary<int, int> Selected { get; set; } = [];
}

/// <summary>Instruments for the echo note gordos (<see cref="GameEnum.Instrument"/>).</summary>
public sealed class Instruments
{
    public List<int> Unlocked { get; set; } = [];
    public int Selected { get; set; }
}
