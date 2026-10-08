using OpenRanch.Formats.Game;

namespace OpenRanch.Simulation.Tests;

// Tests that need the real game. CI has none, so they are skipped unless the game is found
// through OPENRANCH_GAME_DIR or a Steam library on this machine.
public sealed class GameFactAttribute : FactAttribute
{
    public static GameInstall? Install { get; } = GameInstall.Find();

    public GameFactAttribute()
    {
        if (Install is null)
            Skip = $"Install Slime Rancher or set {GameInstall.GameDirVariable} to run this test.";
    }
}

// The install's slime and market data, read once for every test that needs it.
public static class InstalledData
{
    private static readonly Lazy<(SlimeData Slimes, MarketData Market, IReadOnlyList<string> Ids)> Data = new(() =>
    {
        using var scripts = new GameScripts(GameFactAttribute.Install!);
        return (SlimeData.Read(scripts), MarketData.Read(scripts), scripts.IdentifiableIds.Values.Keys.ToList());
    });

    public static SlimeData Slimes => Data.Value.Slimes;
    public static MarketData Market => Data.Value.Market;
    public static IReadOnlyList<string> ItemIds => Data.Value.Ids;

    public static SlimeSpecies Species(string id) => SlimeSpecies.From(Slimes.Get(id), ItemIds);
}
