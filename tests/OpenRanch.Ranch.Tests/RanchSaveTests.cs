using System.Text;
using System.Text.Json;
using OpenRanch.Formats.Saves;

namespace OpenRanch.Ranch.Tests;

public class RanchSaveTests
{
    // A ranch with every part of the model filled in, including the awkward values: infinity,
    // negative zero, absent optional values and the largest numbers.
    public static RanchState Sample()
    {
        var ranch = new RanchState
        {
            GameName = "20261008120000_Sample",
            DisplayName = "Sample \"ranch\" ✓",
            GameVersion = "1.4.4",
            GameMode = 1,
            GameIconId = 42,
            WorldTime = WorldClock.At(12, 17.25),
            AccessDoors = { ["door_lab"] = 2, ["door_grotto"] = 0 },
            Palettes = { [0] = 4, [3] = 1 },
            AreaCatchUpTimes = { ["cellDocks"] = 123456.789 },
        };
        var p = ranch.Player;
        p.Health = 150;
        p.Energy = 250;
        p.Radiation = 3;
        p.Money = int.MaxValue;
        p.Keys = 9;
        p.MoneyEverCollected = 1_000_000;
        p.Position = new Vec3(91.5f, 15.25f, -140.125f);
        p.Rotation = new Vec3(0, -0f, 359.9f);
        p.Upgrades.AddRange([5, 2, 9]);
        p.AvailableUpgrades.Add(10);
        p.UpgradeLocks[10] = new TimedLock(true, 1.5e6);
        p.Ammo[0] = [new AmmoSlot(11, 20) { Emotions = { [0] = 0.25f, [2] = 1f } }, new AmmoSlot(0, 0)];
        p.Ammo[1] = [new AmmoSlot(3, 50)];
        p.Mail.Add(new Mail(2, "m.mail.welcome", false));
        p.Progress[7] = 3;
        p.DelayedProgress[1] = 99.0;
        p.Blueprints.AddRange([100, 101]);
        p.AvailableBlueprints.Add(102);
        p.BlueprintLocks[102] = new TimedLock(false, 0);
        p.Gadgets[100] = 2;
        p.CraftMaterials[300] = 17;
        p.RegionSetId = 1;
        p.UnlockedZoneMaps.AddRange([1, 2]);
        p.EndGameTime = null;
        p.Decorizer = new Decorizer { Contents = { [400] = 5 }, Settings = { ["slot1"] = 400, ["slot2"] = null } };

        ranch.Plots.Add(new Plot
        {
            Id = "plot1",
            Type = 2,
            Upgrades = [1, 9, 12],
            Feeder = new FeederState(1000.5, 3, 1),
            CollectorNextTime = 2000,
        });
        ranch.Plots.Add(new Plot
        {
            Id = "plot2",
            Type = 5,
            Upgrades = [3],
            Silo = { [1] = [new AmmoSlot(50, 99), new AmmoSlot(0, 0)] },
            SiloSlotSelections = [0, 1, 0, 0],
        });
        ranch.Plots.Add(new Plot { Id = "plot3", Type = 4, AttachedResource = 6, AttachedDeathTime = double.PositiveInfinity, AshUnits = 0.5f });

        ranch.Actors.Add(new Actor
        {
            ActorId = long.MaxValue,
            TypeId = 11,
            Position = new Vec3(1, 2, 3),
            Rotation = new Vec3(0, 90, 0),
            Emotions = { [0] = 0.666f, [1] = 0.1f, [2] = 0f },
            TransformTime = 0,
            ReproduceTime = double.PositiveInfinity,
            DestroyTime = double.NegativeInfinity,
            CycleState = 2,
            CycleProgressTime = 5,
            DisabledAtTime = 77.25,
            IsFeral = true,
            Fashions = [500],
            IsGlitch = false,
            RegionSetId = 1,
        });
        ranch.Actors.Add(new Actor { ActorId = 2, TypeId = 50 });

        ranch.Pedia.Unlocked.AddRange(["PINK_SLIME", "CORRAL"]);
        ranch.Pedia.CompletedTutorials.Add("INTRO");
        ranch.Pedia.PopupQueue.Add("CORRAL");
        ranch.Pedia.ProgressGivenForCount = 2;

        ranch.PlacedGadgets["site1"] = new PlacedGadget
        {
            GadgetId = 100,
            YRotation = 45,
            IsPrimaryInLink = true,
            Ammo = [new AmmoSlot(3, 1)],
            ExtractorCyclesRemaining = 4,
            ExtractorQueuedToProduce = 1,
            ExtractorCycleEndTime = 10,
            ExtractorNextProduceTime = 11,
            WaitForChargeupTime = 12,
            LastSpawnTime = 13,
            BaitId = 6,
            GordoId = 70,
            GordoEatenCount = 8,
            Fashions = [501],
            Drone = new DroneGadget
            {
                Drone = new Drone { Position = new Vec3(1, 1, 1), Rotation = null, Ammo = new AmmoSlot(50, 3), Fashions = [], NoClip = true },
                Station = new DroneStation(BatteryTime: null),
                Programs = [new DroneProgram("PLORTS", "CORRAL", "SILO")],
            },
        };
        ranch.PlacedGadgets["site2"] = new PlacedGadget { GadgetId = 101 };

        var w = ranch.World;
        w.EconomySeed = 0.123f;
        w.MarketSaturation[50] = 2.5f;
        w.Gordos["gordo1"] = new Gordo(10, [500]);
        w.TreasurePods["pod1"] = new TreasurePod(1, [300, 301]);
        w.Switches["switch1"] = 2;
        w.PuzzleSlotsFilled["slot1"] = true;
        w.TeleporterActivations["tele1"] = false;
        w.OccupiedPhaseSites["phase1"] = true;
        w.OasisStates["oasis1"] = true;
        w.ActiveGingerPatches.Add("ginger1");
        w.EchoNoteGordos["echo1"] = 1;
        w.EchoNoteGordos["echo2"] = null;

        ranch.Appearances.Unlocked[11] = [0, 1];
        ranch.Appearances.Selected[11] = 1;
        ranch.Instruments.Unlocked.AddRange([1, 2]);
        ranch.Instruments.Selected = 2;
        return ranch;
    }

    [Fact]
    public void Round_trip_keeps_every_byte()
    {
        var bytes = RanchSave.ToBytes(Sample());
        var again = RanchSave.FromBytes(bytes);
        Assert.Equal(Encoding.UTF8.GetString(bytes), Encoding.UTF8.GetString(RanchSave.ToBytes(again)));
    }

    [Fact]
    public void Round_trip_keeps_the_values()
    {
        var original = Sample();
        var again = RanchSave.FromBytes(RanchSave.ToBytes(original));

        Assert.Equal(original.DisplayName, again.DisplayName);
        Assert.Equal(original.WorldTime, again.WorldTime);
        Assert.Equal(int.MaxValue, again.Player.Money);
        Assert.Equal(original.Player.Position, again.Player.Position);
        Assert.True(float.IsNegative(again.Player.Rotation.Y), "negative zero survives");
        Assert.Equal([5, 2, 9], again.Player.Upgrades);
        Assert.Equal(0.25f, again.Player.Ammo[0][0].Emotions[0]);
        Assert.Equal(new TimedLock(true, 1.5e6), again.Player.UpgradeLocks[10]);
        Assert.Null(again.Player.EndGameTime);
        Assert.Null(again.Player.Decorizer!.Settings["slot2"]);
        Assert.Equal(["plot1", "plot2", "plot3"], again.Plots.Select(p => p.Id));
        Assert.Equal(new FeederState(1000.5, 3, 1), again.Plots[0].Feeder);
        Assert.Equal(99, again.Plots[1].Silo[1][0].Count);
        Assert.Equal(double.PositiveInfinity, again.Plots[2].AttachedDeathTime);
        Assert.Equal(long.MaxValue, again.Actors[0].ActorId);
        Assert.Equal(double.NegativeInfinity, again.Actors[0].DestroyTime);
        Assert.Equal(0.666f, again.Actors[0].Emotions[0]);
        Assert.Equal(77.25, again.Actors[0].DisabledAtTime);
        Assert.Null(again.Actors[1].DisabledAtTime);
        Assert.Equal(new DroneProgram("PLORTS", "CORRAL", "SILO"), again.PlacedGadgets["site1"].Drone!.Programs[0]);
        Assert.Null(again.PlacedGadgets["site1"].Drone!.Station!.BatteryTime);
        Assert.Null(again.PlacedGadgets["site2"].Drone);
        Assert.Equal([300, 301], again.World.TreasurePods["pod1"].SpawnQueue);
        Assert.Null(again.World.EchoNoteGordos["echo2"]);
        Assert.Equal([0, 1], again.Appearances.Unlocked[11]);
        Assert.Equal(2, again.Instruments.Selected);
    }

    [Fact]
    public void Header_names_the_format_and_version_first()
    {
        using var doc = JsonDocument.Parse(RanchSave.ToBytes(new RanchState()));
        var members = doc.RootElement.EnumerateObject().Select(m => m.Name).ToList();
        Assert.Equal(["format", "version", "ranch"], members);
        Assert.Equal(RanchSave.FormatName, doc.RootElement.GetProperty("format").GetString());
        Assert.Equal(RanchSave.CurrentVersion, doc.RootElement.GetProperty("version").GetInt32());
        // Enum values are numbers, and maps keyed by them use the number as text.
        var ranch = doc.RootElement.GetProperty("ranch");
        Assert.Equal(JsonValueKind.Number, ranch.GetProperty("gameMode").ValueKind);
    }

    [Theory]
    [InlineData("""{"format":"something-else","version":1,"ranch":{}}""", typeof(RanchSaveException))]
    [InlineData("""{"version":1,"ranch":{}}""", typeof(RanchSaveException))]
    [InlineData("""{"format":"openranch-ranch","ranch":{}}""", typeof(RanchSaveException))]
    [InlineData("""{"format":"openranch-ranch","version":0,"ranch":{}}""", typeof(RanchSaveException))]
    [InlineData("""{"format":"openranch-ranch","version":999,"ranch":{}}""", typeof(NotSupportedException))]
    [InlineData("""{"format":"openranch-ranch","version":1}""", typeof(RanchSaveException))]
    [InlineData("""{"format":"openranch-ranch","version":1,"ranch":{"noSuchField":1}}""", typeof(JsonException))]
    [InlineData("""[1,2,3]""", typeof(RanchSaveException))]
    public void Bad_documents_are_refused(string json, Type error)
    {
        var thrown = Record.Exception(() => RanchSave.FromBytes(Encoding.UTF8.GetBytes(json)));
        Assert.NotNull(thrown);
        Assert.IsAssignableFrom(error, thrown);
    }

    [Fact]
    public void A_minimal_document_reads_as_a_new_ranch()
    {
        var ranch = RanchSave.FromBytes("""{"format":"openranch-ranch","version":1,"ranch":{}}"""u8.ToArray());
        Assert.Equal(WorldClock.NewGameStart, ranch.WorldTime);
        Assert.Empty(ranch.Plots);
    }

    [Fact]
    public void Files_are_written_whole_and_read_back()
    {
        var dir = Path.Combine(Path.GetTempPath(), "openranch-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "sample" + RanchSave.FileExtension);
            RanchSave.WriteFile(path, Sample());
            RanchSave.WriteFile(path, Sample()); // replacing works too
            Assert.Equal(["sample" + RanchSave.FileExtension], Directory.GetFiles(dir).Select(Path.GetFileName));
            Assert.Equal(RanchSave.ToBytes(Sample()), File.ReadAllBytes(path));
            Assert.Equal("Sample \"ranch\" ✓", RanchSave.ReadFile(path).DisplayName);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    // Checked without calling WriteFile, so a broken guard can never touch the real folder.
    [Fact]
    public void The_original_save_folder_is_off_limits()
    {
        var saves = SaveFile.DefaultSaveDirectory();
        Assert.True(RanchSave.IsInOriginalSaveFolder(Path.Combine(saves, "x" + RanchSave.FileExtension)));
        Assert.True(RanchSave.IsInOriginalSaveFolder(Path.Combine(saves, "sub", "x" + RanchSave.FileExtension)));
        Assert.True(RanchSave.IsInOriginalSaveFolder(saves));
        Assert.False(RanchSave.IsInOriginalSaveFolder(saves + "-copy"));
        Assert.False(RanchSave.IsInOriginalSaveFolder(Path.Combine(Path.GetTempPath(), "x" + RanchSave.FileExtension)));
    }
}
