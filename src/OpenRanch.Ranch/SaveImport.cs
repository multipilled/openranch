using OpenRanch.Formats.Saves;

namespace OpenRanch.Ranch;

/// <summary>
/// Brings a ranch from one of the original's saves (game block "SRGAME" version 12, game 1.4.x)
/// into a <see cref="RanchState"/>. The original's save is only ever read: the file is copied into
/// memory first and never opened for writing. What is carried over, and what isn't yet, is listed
/// in docs/behavior/ranch-saves.md.
/// </summary>
public static class SaveImport
{
    /// <summary>Reads an original save file into memory and imports it.</summary>
    public static RanchState ReadFile(string path) => FromSave(ReadSave(path));

    /// <summary>Reads an original save file into memory (read-only) without importing it.</summary>
    public static SaveBlock ReadSave(string path) =>
        SaveFile.Read(new MemoryStream(File.ReadAllBytes(path), writable: false));

    public static RanchState FromSave(SaveBlock game)
    {
        if (game.Tag != SaveFile.GameTag || game.Version != SaveSchemas.Game.Version)
            throw new NotSupportedException($"Can only import {SaveFile.GameTag} v{SaveSchemas.Game.Version} saves, not {game}.");

        var summary = game.Block("summary");
        var world = game.Block("world");
        var ranch = game.Block("ranch");
        var pedia = game.Block("pedia");
        var appearances = game.Block("appearances");
        var instruments = game.Block("instruments");

        return new RanchState
        {
            GameName = game.Get<string>("gameName"),
            DisplayName = game.Get<string>("displayName"),
            GameVersion = summary.Get<string>("gameVersion"),
            GameMode = summary.Get<int>("gameMode"),
            GameIconId = summary.Get<int>("iconId"),
            WorldTime = world.Get<double>("worldTime"),
            Player = Player(game.Block("player")),
            Plots = ranch.List("plots").Select(p => Plot((SaveBlock)p!)).ToList(),
            AccessDoors = Map(ranch.Map("accessDoors"), k => (string)k, v => (int)v!),
            Palettes = Map(ranch.Map("palettes"), k => (int)k, v => (int)v!),
            AreaCatchUpTimes = Map(ranch.Map("ranchFastForward"), k => (string)k, v => (double)v!),
            Actors = game.List("actors").Select(a => Actor((SaveBlock)a!)).ToList(),
            Pedia = new Slimepedia
            {
                Unlocked = Strings(pedia.List("unlocked")),
                CompletedTutorials = Strings(pedia.List("completedTutorials")),
                PopupQueue = Strings(pedia.List("popupQueue")),
                ProgressGivenForCount = pedia.Get<int>("progressGivenForPediaCount"),
            },
            PlacedGadgets = Map(world.Map("placedGadgets"), k => (string)k, v => Gadget((SaveBlock)v!)),
            World = World(world),
            Appearances = new SlimeAppearances
            {
                Unlocked = Map(appearances.Map("unlocks"), k => (int)k, v => Ints((List<object?>)v!)),
                Selected = Map(appearances.Map("selections"), k => (int)k, v => (int)v!),
            },
            Instruments = new Instruments
            {
                Unlocked = Ints(instruments.List("unlocks")),
                Selected = instruments.Get<int>("selection"),
            },
        };
    }

    private static Rancher Player(SaveBlock p) => new()
    {
        Health = p.Get<int>("health"),
        Energy = p.Get<int>("energy"),
        Radiation = p.Get<int>("radiation"),
        Money = p.Get<int>("currency"),
        Keys = p.Get<int>("keys"),
        MoneyEverCollected = p.Get<int>("currencyEverCollected"),
        Position = Vector(p["position"]),
        Rotation = Vector(p["rotation"]),
        Upgrades = Ints(p.List("upgrades")),
        AvailableUpgrades = Ints(p.List("availableUpgrades")),
        UpgradeLocks = Map(p.Map("upgradeLocks"), k => (int)k, v => Lock((SaveRecord)v!)),
        Ammo = Map(p.Map("ammo"), k => (int)k, v => Ammo((List<object?>)v!)),
        Mail = p.List("mail").Select(m =>
        {
            var mail = (SaveBlock)m!;
            return new Mail(mail.Get<int>("type"), mail.Get<string>("messageKey"), mail.Get<bool>("isRead"));
        }).ToList(),
        Progress = Map(p.Map("progress"), k => (int)k, v => (int)v!),
        DelayedProgress = Map(p.Map("delayedProgress"), k => (int)k, v => (double)v!),
        Blueprints = Ints(p.List("blueprints")),
        AvailableBlueprints = Ints(p.List("availableBlueprints")),
        BlueprintLocks = Map(p.Map("blueprintLocks"), k => (int)k, v => Lock((SaveRecord)v!)),
        Gadgets = Map(p.Map("gadgets"), k => (int)k, v => (int)v!),
        CraftMaterials = Map(p.Map("craftMaterials"), k => (int)k, v => (int)v!),
        RegionSetId = p.Get<int>("regionSetId"),
        UnlockedZoneMaps = Ints(p.List("unlockedZoneMaps")),
        EndGameTime = (double?)p["endGameTime"],
        Decorizer = p["decorizer"] is SaveBlock d
            ? new Decorizer
            {
                Contents = Map(d.Map("contents"), k => (int)k, v => (int)v!),
                Settings = Map(d.Map("settings"), k => (string)k, v => (int?)(v as SaveBlock)?.Get<int>("selected")),
            }
            : null,
    };

    private static Plot Plot(SaveBlock p) => new()
    {
        Id = p.Get<string>("id"),
        Type = p.Get<int>("plotType"),
        Upgrades = Ints(p.List("upgrades")),
        AttachedResource = p.Get<int>("attachedResource"),
        AttachedDeathTime = p.Get<double>("attachedDeathTime"),
        Feeder = new FeederState(p.Get<double>("feederNextTime"), p.Get<int>("feederPendingCount"), p.Get<int>("feederSpeed")),
        CollectorNextTime = p.Get<double>("collectorNextTime"),
        Silo = Map(p.Map("siloAmmo"), k => (int)k, v => Ammo((List<object?>)v!)),
        SiloSlotSelections = Ints(p.List("siloActivatorIndices")),
        AshUnits = p.Get<float>("ashUnits"),
    };

    private static Actor Actor(SaveBlock a)
    {
        var cycle = a.Block("cycle");
        return new Actor
        {
            ActorId = a.Get<long>("actorId"),
            TypeId = a.Get<int>("typeId"),
            Position = Vector(a["position"]),
            Rotation = Vector(a["rotation"]),
            Emotions = Emotions(a.Block("emotions")),
            TransformTime = a.Get<double>("transformTime"),
            ReproduceTime = a.Get<double>("reproduceTime"),
            DestroyTime = a.Get<double>("destroyTime"),
            CycleState = cycle.Get<int>("state"),
            CycleProgressTime = cycle.Get<double>("progressTime"),
            DisabledAtTime = (double?)a["disabledAtTime"],
            IsFeral = a.Get<bool>("isFeral"),
            Fashions = Ints(a.List("fashions")),
            IsGlitch = a.Get<bool>("isGlitch"),
            RegionSetId = a.Get<int>("regionSetId"),
        };
    }

    private static PlacedGadget Gadget(SaveBlock g) => new()
    {
        GadgetId = g.Get<int>("gadgetId"),
        YRotation = g.Get<float>("yRotation"),
        IsPrimaryInLink = g.Get<bool>("isPrimaryInLink"),
        Ammo = Ammo(g.List("ammo")),
        ExtractorCyclesRemaining = g.Get<int>("extractorCyclesRemaining"),
        ExtractorQueuedToProduce = g.Get<int>("extractorQueuedToProduce"),
        ExtractorCycleEndTime = g.Get<double>("extractorCycleEndTime"),
        ExtractorNextProduceTime = g.Get<double>("extractorNextProduceTime"),
        WaitForChargeupTime = g.Get<double>("waitForChargeupTime"),
        LastSpawnTime = g.Get<double>("lastSpawnTime"),
        BaitId = g.Get<int>("baitId"),
        GordoId = g.Get<int>("gordoId"),
        GordoEatenCount = g.Get<int>("gordoEatenCount"),
        Fashions = Ints(g.List("fashions")),
        Drone = g["drone"] is SaveBlock d ? DroneGadget(d) : null,
    };

    private static DroneGadget DroneGadget(SaveBlock d) => new()
    {
        Drone = d["drone"] is SaveBlock drone
            ? new Drone
            {
                Position = drone["position"] is SaveBlock pos ? Vector(pos) : null,
                Rotation = drone["rotation"] is SaveBlock rot ? Vector(rot) : null,
                Ammo = drone["ammo"] is SaveBlock ammo ? Slot(ammo) : null,
                Fashions = Ints(drone.List("fashions")),
                NoClip = drone.Get<bool>("noClip"),
            }
            : null,
        Station = d["station"] is SaveBlock station
            ? new DroneStation((station["battery"] as SaveBlock)?.Get<double>("time"))
            : null,
        Programs = d.List("programs").Select(p =>
        {
            var program = (SaveBlock)p!;
            return new DroneProgram(program.Get<string>("target"), program.Get<string>("source"), program.Get<string>("destination"));
        }).ToList(),
    };

    private static WorldState World(SaveBlock w) => new()
    {
        EconomySeed = w.Get<float>("economySeed"),
        MarketSaturation = Map(w.Map("marketSaturation"), k => (int)k, v => (float)v!),
        Gordos = Map(w.Map("gordos"), k => (string)k, v =>
        {
            var gordo = (SaveBlock)v!;
            return new Gordo(gordo.Get<int>("eatenCount"), Ints(gordo.List("fashions")));
        }),
        TreasurePods = Map(w.Map("treasurePods"), k => (string)k, v =>
        {
            var pod = (SaveBlock)v!;
            return new TreasurePod(pod.Get<int>("state"), Ints(pod.List("spawnQueue")));
        }),
        Switches = Map(w.Map("switches"), k => (string)k, v => (int)v!),
        PuzzleSlotsFilled = Map(w.Map("puzzleSlotsFilled"), k => (string)k, v => (bool)v!),
        TeleporterActivations = Map(w.Map("teleporterActivations"), k => (string)k, v => (bool)v!),
        OccupiedPhaseSites = Map(w.Map("occupiedPhaseSites"), k => (string)k, v => (bool)v!),
        OasisStates = Map(w.Map("oasisStates"), k => (string)k, v => (bool)v!),
        ActiveGingerPatches = Strings(w.List("activeGingerPatches")),
        EchoNoteGordos = Map(w.Map("echoNoteGordos"), k => (string)k, v => (int?)(v as SaveBlock)?.Get<int>("state")),
        ResourceSpawners = w.Map("resourceSpawnerWater").Select(kv =>
        {
            var times = (SaveBlock)kv.Value!;
            return new CropTimes(Vector(kv.Key), times.Get<double>("spawnTime"), times.Get<float>("water"));
        }).ToList(),
    };

    private static List<AmmoSlot> Ammo(List<object?> slots) => slots.Select(s => Slot((SaveBlock)s!)).ToList();

    private static AmmoSlot Slot(SaveBlock s) =>
        new(s.Get<int>("id"), s.Get<int>("count")) { Emotions = Emotions(s.Block("emotions")) };

    private static Dictionary<int, float> Emotions(SaveBlock e)
    {
        var levels = new Dictionary<int, float>();
        foreach (var (emotion, level) in e.Map("levels"))
            levels[(int)emotion] = (float)level!;
        return levels;
    }

    private static TimedLock Lock(SaveRecord r) => new(r.Get<bool>("timedLock"), r.Get<double>("lockedUntil"));

    private static Vec3 Vector(object? v)
    {
        var b = (SaveBlock)v!;
        return new Vec3(b.Get<float>("x"), b.Get<float>("y"), b.Get<float>("z"));
    }

    private static List<int> Ints(List<object?> list) => list.Select(v => (int)v!).ToList();

    private static List<string> Strings(List<object?> list) => list.Select(v => (string)v!).ToList();

    // Later entries win if a key repeats.
    private static Dictionary<TKey, TValue> Map<TKey, TValue>(SaveMap map, Func<object, TKey> key, Func<object?, TValue> value)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, TValue>(map.Count);
        foreach (var (k, v) in map)
            result[key(k)] = value(v);
        return result;
    }
}
