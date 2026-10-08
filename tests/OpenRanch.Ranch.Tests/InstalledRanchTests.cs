using OpenRanch.Formats.Saves;
using OpenRanch.Simulation;
using Xunit.Abstractions;

namespace OpenRanch.Ranch.Tests;

// Tests against the player's own saves and install. Saves are copied into memory and only read.
public class InstalledRanchTests(ITestOutputHelper output)
{
    // Milestone 3's check: the 2022 Game2 ranch imports with the same money, plots and slimes.
    [Game2Fact]
    public void Game2_imports_with_the_same_money_plots_and_slimes()
    {
        var (path, save) = Saves.Newest(Game2FactAttribute.GameName)!.Value;
        var ranch = SaveImport.FromSave(save);
        var player = save.Block("player");

        // Money and the clock.
        Assert.Equal(player.Get<int>("currency"), ranch.Player.Money);
        Assert.Equal(save.Block("summary").Get<int>("currency"), ranch.Player.Money);
        Assert.Equal(save.Block("world").Get<double>("worldTime"), ranch.WorldTime);
        Assert.Equal(SaveFile.Describe(save).Day, ranch.Clock.Day);

        // Every plot, in order, with its type and upgrades.
        var rawPlots = save.Block("ranch").List("plots").Cast<SaveBlock>().ToList();
        Assert.Equal(41, rawPlots.Count);
        Assert.Equal(rawPlots.Count, ranch.Plots.Count);
        for (var i = 0; i < rawPlots.Count; i++)
        {
            Assert.Equal(rawPlots[i].Get<string>("id"), ranch.Plots[i].Id);
            Assert.Equal(rawPlots[i].Get<int>("plotType"), ranch.Plots[i].Type);
            Assert.Equal(rawPlots[i].List("upgrades").Cast<int>(), ranch.Plots[i].Upgrades);
        }

        // Every actor, counted by type.
        var rawActors = save.List("actors").Cast<SaveBlock>().ToList();
        var rawByType = rawActors.GroupBy(a => a.Get<int>("typeId")).ToDictionary(g => g.Key, g => g.Count());
        var census = RanchCensus.Of(ranch);
        Assert.Equal(rawActors.Count, ranch.Actors.Count);
        Assert.Equal(rawByType.OrderBy(kv => kv.Key).ToList(), census.ActorsByType.OrderBy(kv => kv.Key).ToList());

        output.WriteLine($"{Path.GetFileName(path)}, save #{save.Block("summary").Get<ulong>("saveNumber")}, \"{ranch.DisplayName}\" (game {ranch.GameVersion})");
        if (GameFactAttribute.Install is null)
        {
            output.WriteLine(census.Describe());
            return;
        }

        // With the install: slimes by name, counted straight from the raw save as well.
        var names = InstalledNames.Get;
        var rawSlimes = rawByType
            .Select(kv => (Name: names.Item(kv.Key), Count: kv.Value))
            .Where(e => Items.KindOf(e.Name) is ItemKind.Slime or ItemKind.Largo)
            .ToDictionary(e => e.Name, e => e.Count);
        var slimes = census.Slimes(names);
        Assert.NotEmpty(slimes);
        Assert.Equal(rawSlimes.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList(), slimes.ToList());
        Assert.All(ranch.Plots, p => Assert.DoesNotMatch("^[0-9]+$", names.PlotType(p.Type)));
        output.WriteLine(census.Describe(names));
    }

    [Game2Fact]
    public void Game2_survives_the_openranch_format()
    {
        var (_, save) = Saves.Newest(Game2FactAttribute.GameName)!.Value;
        var ranch = SaveImport.FromSave(save);
        var bytes = RanchSave.ToBytes(ranch);
        var again = RanchSave.FromBytes(bytes);
        Assert.Equal(bytes, RanchSave.ToBytes(again));
        Assert.Equal(ranch.Player.Money, again.Player.Money);
        Assert.Equal(ranch.Plots.Select(p => (p.Id, p.Type)), again.Plots.Select(p => (p.Id, p.Type)));
        Assert.Equal(RanchCensus.Of(ranch).ActorsByType.ToList(), RanchCensus.Of(again).ActorsByType.ToList());
        output.WriteLine($"openranch save: {bytes.Length:N0} bytes of JSON for {ranch.Actors.Count} actors");
    }

    // Every version 12 save on the PC imports, and its import survives the openranch format.
    [SaveFact]
    public void Every_supported_save_imports_and_round_trips()
    {
        foreach (var path in Saves.Supported)
        {
            var save = SaveImport.ReadSave(path);
            var ranch = SaveImport.FromSave(save);
            var info = SaveFile.Describe(save);
            Assert.Equal(info.Currency, ranch.Player.Money);
            Assert.Equal(save.Block("summary").Get<int>("currency"), ranch.Player.Money);
            Assert.Equal(info.ActorCount, ranch.Actors.Count);
            Assert.Equal(info.PlotCount, ranch.Plots.Count);
            Assert.Equal(41, ranch.Plots.Count);
            Assert.True(ranch.WorldTime >= WorldClock.NewGameStart);
            Assert.Equal(save.Block("pedia").List("unlocked").Count, ranch.Pedia.Unlocked.Count);
            Assert.Equal(save.Block("player").List("mail").Count, ranch.Player.Mail.Count);
            var bytes = RanchSave.ToBytes(ranch);
            Assert.Equal(bytes, RanchSave.ToBytes(RanchSave.FromBytes(bytes)));
            output.WriteLine($"{Path.GetFileName(path)}: money {ranch.Player.Money}, {ranch.Clock}, {ranch.Plots.Count} plots, {ranch.Actors.Count} actors");
        }
    }

    // Every enum the ranch state stores numbers of can be named from the install.
    [GameFact]
    public void Every_stored_enum_is_in_the_install()
    {
        var names = InstalledNames.Get;
        foreach (var field in typeof(GameEnum).GetFields())
        {
            var label = (string)field.GetValue(null)!;
            var values = names.Get(label);
            Assert.NotEmpty(values.Names);
            output.WriteLine($"{label}: {values.Names.Count} values");
        }
        var plotTypes = names.Get(GameEnum.PlotType);
        output.WriteLine("Plot types: " + string.Join(", ", plotTypes.Names.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.Equal(plotTypes.Values["CORRAL"], names.Value(GameEnum.PlotType, "CORRAL"));
    }

    [GameFact]
    public void Day_length_is_read_from_the_install()
    {
        var length = InstalledData.DayLength;
        output.WriteLine($"A game day lasts {length.RealSecondsPerDay} real seconds, {length.FastForwardRealSecondsPerDay} while sleeping");
        Assert.InRange(length.RealSecondsPerDay, 60, 7200);
        Assert.InRange(length.FastForwardRealSecondsPerDay, 1, length.RealSecondsPerDay);
    }

    [GameFact]
    public void Plot_menus_are_read_from_the_install()
    {
        var names = InstalledNames.Get;
        var catalog = InstalledData.Plots;
        foreach (var menu in catalog.Menus.Values.OrderBy(m => m.Type))
        {
            output.WriteLine($"{names.PlotType(menu.Type)} ({menu.ScriptClass}):");
            foreach (var r in menu.Replacements)
                output.WriteLine($"  {r.MenuItem}: becomes {names.PlotType(r.Type)} for {r.Cost}");
            foreach (var u in menu.Upgrades)
                output.WriteLine($"  {u.MenuItem}: upgrade {names.PlotUpgrade(u.Upgrade)} for {u.Cost}");
            foreach (var (item, cost) in menu.OtherCosts)
                output.WriteLine($"  {item}: {cost}");
        }

        var empty = names.Value(GameEnum.PlotType, "EMPTY");
        var buildable = catalog.Menu(empty)!.Replacements.Select(r => names.PlotType(r.Type)).Order().ToList();
        Assert.Equal(["COOP", "CORRAL", "GARDEN", "INCINERATOR", "POND", "SILO"], buildable);
        foreach (var type in buildable)
        {
            var menu = catalog.Menu(names.Value(GameEnum.PlotType, type));
            Assert.NotNull(menu);
            Assert.Contains(menu.Replacements, r => r.Type == empty); // every built plot can be demolished
        }
    }
}
