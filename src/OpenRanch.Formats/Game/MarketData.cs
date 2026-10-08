namespace OpenRanch.Formats.Game;

/// <summary>One item the plort market buys: its base price and how many sales fill it up.</summary>
public sealed record MarketEntryData(string Id, float BaseValue, float FullSaturation);

/// <summary>
/// The plort market's settings, from the world scene's EconomyDirector component: base prices,
/// saturation per plort, how fast saturation recovers each day and how long the market closes at
/// midnight. See docs/behavior/plort-market.md.
/// </summary>
public sealed record MarketData(
    IReadOnlyList<MarketEntryData> Entries,
    float SaturationSensitivity,
    float SaturationRecovery,
    float DailyShutdownMinutes)
{
    public MarketEntryData Get(string id) =>
        Entries.FirstOrDefault(e => e.Id == id) ?? throw new KeyNotFoundException($"The market doesn't buy {id}.");

    public static MarketData Read(GameScripts scripts)
    {
        var ids = scripts.IdentifiableIds;
        foreach (var (asset, mb) in scripts.OfClass("EconomyDirector"))
        {
            var d = mb.Data!;
            var entries = new List<MarketEntryData>();
            foreach (var item in d.List("baseValueMap").OfType<Unity.Managed.SerializedObject>())
            {
                // "accept" points at the Identifiable component on the item's prefab.
                if (item["accept"] is not Unity.PPtr accept || scripts.Follow(asset.File, accept) is not { } target)
                    continue;
                var id = ids.NameOf(Convert.ToInt64(target.Data.Data!["id"]));
                entries.Add(new MarketEntryData(id, item.Get<float>("value"), item.Get<float>("fullSaturation")));
            }
            return new MarketData(entries, d.Get<float>("saturationSensitivity"), d.Get<float>("saturationRecovery"), d.Get<float>("dailyShutdownMins"));
        }
        throw new InvalidDataException("No EconomyDirector was found in the install.");
    }
}
