namespace OpenRanch.Ranch;

/// <summary>
/// Names of the game's enums whose values <see cref="RanchState"/> stores. The state keeps the
/// game's own numbers; <see cref="GameEnums"/> turns them into names (such as "PINK_SLIME") by
/// reading these enums from the player's install. Most labels match the ones in
/// OpenRanch.Formats.Saves.SaveSchemas ("Outer.Inner" for an enum nested in a class); the three whose
/// class sits in a namespace give it in full ("Namespace.Outer/Inner"), as the game's assembly
/// metadata names them.
/// </summary>
public static class GameEnum
{
    public const string ItemId = "Identifiable.Id";
    public const string PlotType = "LandPlot.Id";
    public const string PlotUpgrade = "LandPlot.Upgrade";
    public const string PlayerUpgrade = "PlayerState.Upgrade";
    public const string AmmoMode = "PlayerState.AmmoMode";
    public const string GameMode = "PlayerState.GameMode";
    public const string Progress = "ProgressDirector.ProgressType";
    public const string ProgressTracker = "ProgressDirector.ProgressTrackerId";
    public const string MailType = "MailDirector.Type";
    public const string GadgetId = "Gadget.Id";
    public const string PaletteType = "RanchDirector.PaletteType";
    public const string Palette = "RanchDirector.Palette";
    public const string AccessDoorState = "AccessDoor.State";
    public const string SiloStorage = "SiloStorage.StorageType";
    public const string FeedSpeed = "SlimeFeeder.FeedSpeed";
    public const string SpawnResource = "SpawnResource.Id";
    public const string Zone = "ZoneDirector.Zone";
    public const string RegionSet = "MonomiPark.SlimeRancher.Regions.RegionRegistry/RegionSetId";
    public const string Emotion = "SlimeEmotions.Emotion";
    public const string ResourceCycleState = "ResourceCycle.State";
    public const string SwitchState = "SwitchHandler.State";
    public const string TreasurePodState = "TreasurePod.State";
    public const string EchoNoteGordoState = "MonomiPark.SlimeRancher.DataModel.EchoNoteGordoModel/State";
    public const string Appearance = "SlimeAppearance.AppearanceSaveSet";
    public const string Instrument = "MonomiPark.SlimeRancher.DataModel.InstrumentModel/Instrument";
}
