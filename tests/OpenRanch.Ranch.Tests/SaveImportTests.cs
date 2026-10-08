using OpenRanch.Formats.Saves;

namespace OpenRanch.Ranch.Tests;

public class SaveImportTests
{
    private static SaveBlock Ammo(int id, int count, params (int Emotion, float Level)[] emotions)
    {
        var levels = new SaveMap();
        foreach (var (emotion, level) in emotions)
            levels.Add(new(emotion, level));
        return BlankSave.Of(SaveSchemas.Ammo).Set("id", id).Set("count", count)
            .Set("emotions", BlankSave.Of(SaveSchemas.Emotions).Set("levels", levels));
    }

    private static SaveBlock Plot(string id, int type, params int[] upgrades) =>
        BlankSave.Of(SaveSchemas.LandPlot).Set("id", id).Set("plotType", type)
            .Set("upgrades", upgrades.Cast<object?>().ToList());

    private static SaveBlock Actor(long id, int type, float x) =>
        BlankSave.Of(SaveSchemas.Actor).Set("actorId", id).Set("typeId", type)
            .Set("position", BlankSave.Vector(x, 2, 3)).Set("destroyTime", double.PositiveInfinity);

    // A small ranch made by hand in the original's layout: two plots, three actors, mail, progress.
    private static SaveBlock SampleGame()
    {
        var game = BlankSave.Game().Set("gameName", "20240101000000_Test").Set("displayName", "Test");
        game.Block("summary").Set("gameVersion", "1.4.4").Set("currency", 1234).Set("iconId", 7);
        game.Block("world").Set("worldTime", WorldClock.At(3, 13.5))
            .Set("marketSaturation", new SaveMap { new(50, 0.25f) })
            .Set("gordos", new SaveMap { new("gordo1", BlankSave.Of(SaveSchemas.Gordo).Set("eatenCount", 12)) });

        var lockRecord = new SaveRecord();
        lockRecord.Fields.Add(new("timedLock", true));
        lockRecord.Fields.Add(new("lockedUntil", 99.5));
        game.Block("player")
            .Set("currency", 1234).Set("keys", 2).Set("health", 100)
            .Set("position", BlankSave.Vector(10, 20, 30))
            .Set("upgrades", new List<object?> { 3, 1 })
            .Set("upgradeLocks", new SaveMap { new(5, lockRecord) })
            .Set("ammo", new SaveMap { new(0, new List<object?> { Ammo(11, 20, (0, 0.5f)), Ammo(0, 0) }) })
            .Set("mail", new List<object?> { BlankSave.Of(SaveSchemas.Mail).Set("type", 1).Set("messageKey", "welcome").Set("isRead", true) })
            .Set("progress", new SaveMap { new(4, 2) })
            .Set("gadgets", new SaveMap { new(9, 3) })
            .Set("endGameTime", 5000.0);

        var silo = Plot("plotB", 5, 3, 4);
        silo.Set("siloAmmo", new SaveMap { new(1, new List<object?> { Ammo(50, 15) }) }).Set("siloActivatorIndices", new List<object?> { 0, 2 });
        var corral = Plot("plotA", 2, 1, 9).Set("feederNextTime", 100.0).Set("feederPendingCount", 4).Set("feederSpeed", 2);
        game.Block("ranch")
            .Set("plots", new List<object?> { corral, silo })
            .Set("accessDoors", new SaveMap { new("door1", 1) })
            .Set("palettes", new SaveMap { new(0, 3) })
            .Set("ranchFastForward", new SaveMap { new("cellLab", 777.0) });

        game.Set("actors", new List<object?> { Actor(1, 11, 1), Actor(2, 11, 2), Actor(3, 50, 3) });
        game.Block("pedia").Set("unlocked", new List<object?> { "PINK_SLIME", "CORRAL" });
        return game;
    }

    [Fact]
    public void Blank_game_is_a_valid_save()
    {
        // Proves BlankSave builds every block the reader expects, in order.
        var bytes = SaveFile.ToBytes(BlankSave.Game());
        var again = SaveFile.Read(new MemoryStream(bytes));
        Assert.Equal(bytes, SaveFile.ToBytes(again));
    }

    [Fact]
    public void Sample_game_imports_every_field_it_sets()
    {
        // Through bytes first, the way a real save arrives.
        var save = SaveFile.Read(new MemoryStream(SaveFile.ToBytes(SampleGame())));
        var ranch = SaveImport.FromSave(save);

        Assert.Equal("20240101000000_Test", ranch.GameName);
        Assert.Equal("Test", ranch.DisplayName);
        Assert.Equal("1.4.4", ranch.GameVersion);
        Assert.Equal(7, ranch.GameIconId);
        Assert.Equal(3, ranch.Clock.Day);
        Assert.Equal(13.5, ranch.Clock.Hour, 6);

        var player = ranch.Player;
        Assert.Equal(1234, player.Money);
        Assert.Equal(2, player.Keys);
        Assert.Equal(100, player.Health);
        Assert.Equal(new Vec3(10, 20, 30), player.Position);
        Assert.Equal([3, 1], player.Upgrades);
        Assert.Equal(new TimedLock(true, 99.5), player.UpgradeLocks[5]);
        var slots = player.Ammo[0];
        Assert.Equal(2, slots.Count);
        Assert.Equal((11, 20), (slots[0].Id, slots[0].Count));
        Assert.Equal(0.5f, slots[0].Emotions[0]);
        Assert.Equal((0, 0), (slots[1].Id, slots[1].Count));
        Assert.Equal(new Mail(1, "welcome", true), Assert.Single(player.Mail));
        Assert.Equal(2, player.ProgressOf(4));
        Assert.Equal(0, player.ProgressOf(99));
        Assert.Equal(3, player.Gadgets[9]);
        Assert.Equal(5000.0, player.EndGameTime);
        Assert.Null(player.Decorizer);

        Assert.Equal(["plotA", "plotB"], ranch.Plots.Select(p => p.Id));
        var corral = ranch.FindPlot("plotA")!;
        Assert.Equal(2, corral.Type);
        Assert.True(corral.HasUpgrade(9));
        Assert.Equal(new FeederState(100.0, 4, 2), corral.Feeder);
        var silo = ranch.FindPlot("plotB")!;
        Assert.Equal([3, 4], silo.Upgrades);
        Assert.Equal(15, Assert.Single(silo.Silo[1]).Count);
        Assert.Equal([0, 2], silo.SiloSlotSelections);

        Assert.Equal(1, ranch.AccessDoors["door1"]);
        Assert.Equal(3, ranch.Palettes[0]);
        Assert.Equal(777.0, ranch.AreaCatchUpTimes["cellLab"]);

        Assert.Equal(3, ranch.Actors.Count);
        Assert.Equal(new Vec3(2, 2, 3), ranch.Actors[1].Position);
        Assert.Equal(double.PositiveInfinity, ranch.Actors[0].DestroyTime);
        Assert.Equal(["PINK_SLIME", "CORRAL"], ranch.Pedia.Unlocked);
        Assert.Equal(0.25f, ranch.World.MarketSaturation[50]);
        Assert.Equal(12, ranch.World.Gordos["gordo1"].EatenCount);

        var census = RanchCensus.Of(ranch);
        Assert.Equal(2, census.ActorsByType[11]);
        Assert.Equal(1, census.PlotsByType[5]);
    }

    [Fact]
    public void Imported_sample_survives_the_openranch_format()
    {
        var ranch = SaveImport.FromSave(SampleGame());
        var bytes = RanchSave.ToBytes(ranch);
        var again = RanchSave.FromBytes(bytes);
        Assert.Equal(bytes, RanchSave.ToBytes(again));
        Assert.Equal(double.PositiveInfinity, again.Actors[0].DestroyTime);
    }

    [Fact]
    public void Other_blocks_are_refused()
    {
        Assert.Throws<NotSupportedException>(() => SaveImport.FromSave(new SaveBlock("SRGAME", 9)));
    }
}
