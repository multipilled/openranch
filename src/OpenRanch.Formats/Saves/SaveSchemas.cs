using static OpenRanch.Formats.Saves.SaveCodec;

namespace OpenRanch.Formats.Saves;

/// <summary>
/// Block layouts written by Slime Rancher 1.4.x (game block "SRGAME" version 12).
/// Leaf blocks come first because the larger ones refer to them.
/// Enum names are labels for the game's own enums; values stay numeric.
/// See docs/formats/save-files.md for what each block holds.
/// </summary>
public static class SaveSchemas
{
    private static SaveSchema.Field F(string name, SaveCodec codec) => new(name, codec);
    private static readonly SaveSchema.Section Section = new();

    private static readonly SaveCodec ItemId = EnumOf("Identifiable.Id");
    private static readonly SaveCodec GadgetId = EnumOf("Gadget.Id");

    public static readonly SaveSchema Vector3 = new("SRV3", 2,
        F("x", F32), F("y", F32), F("z", F32));

    public static readonly SaveSchema Emotions = new("SRSED", 2,
        F("levels", PairsOf(EnumOf("SlimeEmotions.Emotion"), F32)));

    public static readonly SaveSchema ResourceCycle = new("SRRCD", 3,
        F("state", EnumOf("ResourceCycle.State")), F("progressTime", F64));

    public static readonly SaveSchema Ammo = new("SRAD", 2,
        F("id", ItemId), F("count", I32), F("emotions", BlockOf(Emotions)));

    public static readonly SaveSchema ItemEntry = new("SRIE", 3,
        F("id", ItemId), F("nonItemReward", EnumOf("ExchangeDirector.NonIdentReward")), F("count", I32));

    public static readonly SaveSchema RequestedItem = new("SRRIE", 3,
        F("id", ItemId), F("nonItemReward", EnumOf("ExchangeDirector.NonIdentReward")), F("count", I32), F("progress", I32));

    public static readonly SaveSchema ExchangeOffer = new("SREO", 4,
        new SaveSchema.Gate("hasOffer",
            F("rancherId", Str), F("offerId", Str), F("expireTime", F64), F("earlyExchangeTime", F64),
            F("requests", ListOf(BlockOf(RequestedItem))), F("rewards", ListOf(BlockOf(ItemEntry)))));

    public static readonly SaveSchema Gordo = new("SRG", 1,
        F("eatenCount", I32), F("fashions", ListOf(ItemId)));

    public static readonly SaveSchema ResourceWater = new("SRRW", 3,
        F("spawnTime", F64), F("water", F32));

    public static readonly SaveSchema DroneBattery = new("SRDRSTB", 1,
        F("time", F64));

    public static readonly SaveSchema DroneStation = new("SRDRST", 1,
        F("battery", OptBlock(DroneBattery)));

    public static readonly SaveSchema DroneProgram = new("SRDRONEPROG", 1,
        F("target", Str), F("source", Str), F("destination", Str));

    public static readonly SaveSchema Drone = new("SRDRONE", 5,
        F("position", OptBlock(Vector3)), F("rotation", OptBlock(Vector3)), F("ammo", OptBlock(Ammo)),
        F("fashions", ListOf(ItemId)), F("noClip", Bool));

    public static readonly SaveSchema DroneGadget = new("SRDRGD", 1,
        F("drone", OptBlock(Drone)), F("station", OptBlock(DroneStation)), F("programs", ListOf(BlockOf(DroneProgram))));

    public static readonly SaveSchema PlacedGadget = new("SRPG", 8,
        F("gadgetId", GadgetId), F("yRotation", F32), F("isPrimaryInLink", Bool), F("ammo", ListOf(BlockOf(Ammo))),
        F("extractorCyclesRemaining", I32), F("extractorQueuedToProduce", I32), F("extractorCycleEndTime", F64),
        F("extractorNextProduceTime", F64), F("waitForChargeupTime", F64), F("lastSpawnTime", F64),
        F("baitId", ItemId), F("gordoId", ItemId), F("gordoEatenCount", I32), F("fashions", ListOf(ItemId)),
        F("drone", OptBlock(DroneGadget)));

    public static readonly SaveSchema TreasurePod = new("SRTP", 1,
        F("state", EnumOf("TreasurePod.State")), F("spawnQueue", ListOf(ItemId)));

    public static readonly SaveSchema Firestorm = new("SRF", 1,
        F("endStormTime", F64), F("stormPreparing", Bool), F("nextStormTime", F64));

    public static readonly SaveSchema QuicksilverGenerator = new("SRQSEG", 2,
        F("state", EnumOf("QuicksilverEnergyGenerator.State")), F("timer", Opt(F64)));

    public static readonly SaveSchema EchoNoteGordo = new("SRENG", 1,
        F("state", EnumOf("EchoNoteGordoModel.State")));

    public static readonly SaveSchema GlitchTeleporter = new("SRGLITCH_TPD", 1,
        F("activationTime", Opt(F64)));

    public static readonly SaveSchema GlitchTarrNode = new("SRGLITCH_TS", 1,
        F("activationTime", F64));

    public static readonly SaveSchema GlitchSlime = new("SRAD_GS", 1,
        F("exposureChance", F32), F("deathTime", F64));

    public static readonly SaveSchema GlitchImpostoDirector = new("SRGLITCH_ID", 1,
        F("hibernationTime", Opt(F64)));

    public static readonly SaveSchema GlitchImposto = new("SRGLITCH_IP", 1,
        F("deactivateTime", Opt(F64)), F("cooldownTime", F64));

    public static readonly SaveSchema GlitchStorage = new("SRGLITCH_ST", 1,
        F("id", ItemId), F("count", I32));

    public static readonly SaveSchema Slimulation = new("SRGLITCH", 2,
        F("teleporters", MapOf(Str, OptBlock(GlitchTeleporter))),
        F("tarrNodes", MapOf(Str, OptBlock(GlitchTarrNode))),
        F("slimes", MapOf(I64, OptBlock(GlitchSlime))),
        F("impostoDirectors", MapOf(Str, OptBlock(GlitchImpostoDirector))),
        F("impostos", MapOf(Str, OptBlock(GlitchImposto))),
        F("storage", MapOf(Str, OptBlock(GlitchStorage))));

    public static readonly SaveSchema World = new("SRW", 22,
        F("worldTime", F64), F("economySeed", F32), F("dailyOfferCreateTime", F64),
        F("lastOfferRancherIds", ListOf(Str)), F("pendingOfferRancherIds", ListOf(Str)),
        F("weatherUntil", F64), F("weather", EnumOf("AmbianceDirector.Weather")),
        F("offers", MapOf(EnumOf("ExchangeDirector.OfferType"), BlockOf(ExchangeOffer))),
        F("marketSaturation", MapOf(ItemId, F32)),
        F("teleporterActivations", MapOf(Str, Bool)),
        F("animalSpawnerTimes", MapOf(BlockOf(Vector3), F64)),
        F("liquidSourceUnits", MapOf(Str, F32)),
        F("spawnerTriggerTimes", MapOf(BlockOf(Vector3), F64)),
        F("gordos", MapOf(Str, BlockOf(Gordo))),
        F("resourceSpawnerWater", MapOf(BlockOf(Vector3), BlockOf(ResourceWater))),
        F("placedGadgets", MapOf(Str, BlockOf(PlacedGadget))),
        F("treasurePods", MapOf(Str, BlockOf(TreasurePod))),
        F("switches", MapOf(Str, EnumOf("SwitchHandler.State"))),
        F("puzzleSlotsFilled", MapOf(Str, Bool)),
        F("occupiedPhaseSites", MapOf(Str, Bool)),
        F("firestorm", BlockOf(Firestorm)),
        F("oasisStates", MapOf(Str, Bool)),
        F("activeGingerPatches", ListOf(Str)),
        F("quicksilverGenerators", MapOf(Str, BlockOf(QuicksilverGenerator))),
        F("echoNoteGordos", MapOf(Str, OptBlock(EchoNoteGordo))),
        F("slimulation", OptBlock(Slimulation)));

    public static readonly SaveSchema Mail = new("SRMAIL", 2,
        F("type", EnumOf("MailDirector.Type")), F("messageKey", Str), F("isRead", Bool));

    public static readonly SaveSchema DecorizerSetting = new("SRDZRSETTINGS", 1,
        F("selected", ItemId));

    public static readonly SaveSchema Decorizer = new("SRDZR", 1,
        F("contents", MapOf(ItemId, I32)), F("settings", MapOf(Str, OptBlock(DecorizerSetting))));

    private static readonly SaveCodec TimedLock = RecordOf(("timedLock", Bool), ("lockedUntil", F64));

    public static readonly SaveSchema Player = new("SRPL", 14,
        F("health", I32), F("energy", I32), F("radiation", I32), F("currency", I32), F("keys", I32),
        F("currencyEverCollected", I32), F("version", Str), F("gameMode", EnumOf("PlayerState.GameMode")),
        F("gameIconId", ItemId), F("position", BlockOf(Vector3)), F("rotation", BlockOf(Vector3)),
        F("upgrades", ListOf(EnumOf("PlayerState.Upgrade"))),
        F("ammo", MapOf(EnumOf("PlayerState.AmmoMode"), ListOf(BlockOf(Ammo)))),
        F("mail", ListOf(BlockOf(Mail))),
        F("availableUpgrades", ListOf(EnumOf("PlayerState.Upgrade"))),
        F("upgradeLocks", MapOf(EnumOf("PlayerState.Upgrade"), TimedLock)),
        F("progress", MapOf(EnumOf("ProgressDirector.ProgressType"), I32)),
        F("delayedProgress", MapOf(EnumOf("ProgressDirector.ProgressTrackerId"), F64)),
        F("blueprints", ListOf(GadgetId)),
        F("availableBlueprints", ListOf(GadgetId)),
        F("blueprintLocks", MapOf(GadgetId, TimedLock)),
        F("gadgets", MapOf(GadgetId, I32)),
        F("craftMaterials", MapOf(ItemId, I32)),
        F("regionSetId", EnumOf("RegionRegistry.RegionSetId")),
        F("unlockedZoneMaps", ListOf(EnumOf("ZoneDirector.Zone"))),
        F("endGameTime", Opt(F64)),
        F("decorizer", OptBlock(Decorizer)));

    public static readonly SaveSchema LandPlot = new("SRLP", 8,
        F("feederNextTime", F64), F("feederPendingCount", I32), F("feederSpeed", EnumOf("SlimeFeeder.FeedSpeed")),
        F("collectorNextTime", F64), F("attachedDeathTime", F64), F("plotType", EnumOf("LandPlot.Id")),
        F("attachedResource", EnumOf("SpawnResource.Id")), F("id", Str),
        F("upgrades", ListOf(EnumOf("LandPlot.Upgrade"))),
        F("siloAmmo", MapOf(EnumOf("SiloStorage.StorageType"), ListOf(BlockOf(Ammo)))),
        F("siloActivatorIndices", ListOf(I32)), F("ashUnits", F32));

    public static readonly SaveSchema Ranch = new("SRRANCH", 7,
        F("plots", ListOf(BlockOf(LandPlot))),
        F("accessDoors", MapOf(Str, EnumOf("AccessDoor.State"))),
        F("palettes", MapOf(EnumOf("RanchDirector.PaletteType"), EnumOf("RanchDirector.Palette"))),
        F("ranchFastForward", MapOf(Str, F64)));

    public static readonly SaveSchema Actor = new("SRAD", 9,
        F("position", BlockOf(Vector3)), F("rotation", BlockOf(Vector3)), F("actorId", I64), F("typeId", ItemId),
        F("emotions", BlockOf(Emotions)), F("transformTime", F64), F("reproduceTime", F64), F("destroyTime", F64),
        F("cycle", BlockOf(ResourceCycle)), F("disabledAtTime", Opt(F64)), F("isFeral", Bool),
        F("fashions", ListOf(ItemId)), F("isGlitch", Bool), F("regionSetId", EnumOf("RegionRegistry.RegionSetId")));

    public static readonly SaveSchema Pedia = new("SRPED", 3,
        F("progressGivenForPediaCount", I32), F("unlocked", ListOf(Str)), F("completedTutorials", ListOf(Str)),
        F("popupQueue", ListOf(Str)));

    public static readonly SaveSchema Achievements = new("SRGA", 3,
        F("floatStats", MapOf(EnumOf("AchievementsDirector.GameFloatStat"), F32)),
        F("doubleStats", MapOf(EnumOf("AchievementsDirector.GameDoubleStat"), F64)),
        F("intStats", MapOf(EnumOf("AchievementsDirector.GameIntStat"), I32)),
        F("countsByItem", MapOf(EnumOf("AchievementsDirector.GameIdDictStat"), MapOf(ItemId, I32))));

    public static readonly SaveSchema Holiday = new("SRHD", 2,
        F("eventGordos", ListOf(Str)), F("eventEchoNoteGordos", ListOf(Str)));

    public static readonly SaveSchema Appearances = new("SRAPP", 1,
        F("unlocks", MapOf(ItemId, ListOf(EnumOf("SlimeAppearance.AppearanceSaveSet")))),
        F("selections", MapOf(ItemId, EnumOf("SlimeAppearance.AppearanceSaveSet"))));

    public static readonly SaveSchema Instruments = new("SRINSTR", 1,
        F("unlocks", ListOf(EnumOf("InstrumentModel.Instrument"))), F("selection", EnumOf("InstrumentModel.Instrument")));

    public static readonly SaveSchema Summary = new("SRGSUMM", 4,
        F("gameVersion", Str), F("gameMode", EnumOf("PlayerState.GameMode")), F("iconId", ItemId),
        F("currency", I32), F("pediaCount", I32), F("worldTime", F64), F("savedAt", Stamp),
        F("isGameOver", Bool), F("saveNumber", U64));

    public static readonly SaveSchema Game = new("SRGAME", 12,
        F("gameName", Str), F("displayName", Str), F("summary", BlockOf(Summary)),
        F("world", BlockOf(World)), F("player", BlockOf(Player)), F("ranch", BlockOf(Ranch)),
        Section, F("actors", ListOf(BlockOf(Actor))), Section,
        F("pedia", BlockOf(Pedia)), F("achievements", BlockOf(Achievements)), F("holiday", BlockOf(Holiday)),
        F("appearances", BlockOf(Appearances)), F("instruments", BlockOf(Instruments)));
}
