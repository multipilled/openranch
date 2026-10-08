using System.Text;
using OpenRanch.Simulation;

namespace OpenRanch.Ranch;

/// <summary>Counts of what is on a ranch: plots by type and loose things (actors) by type.</summary>
public sealed class RanchCensus
{
    private RanchCensus(RanchState ranch)
    {
        Money = ranch.Player.Money;
        Clock = ranch.Clock;
        PlotsByType = ranch.Plots.GroupBy(p => p.Type).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
        ActorsByType = ranch.Actors.GroupBy(a => a.TypeId).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
    }

    public int Money { get; }
    public WorldClock Clock { get; }
    /// <summary>Number of plots of each type (<see cref="GameEnum.PlotType"/>).</summary>
    public IReadOnlyDictionary<int, int> PlotsByType { get; }
    /// <summary>Number of actors of each type (<see cref="GameEnum.ItemId"/>).</summary>
    public IReadOnlyDictionary<int, int> ActorsByType { get; }

    public static RanchCensus Of(RanchState ranch) => new(ranch);

    /// <summary>
    /// Slimes and largos in the world by type name, sorted by name. Telling slimes apart from other
    /// actors needs the item names, so it reads them from the install through <paramref name="names"/>.
    /// </summary>
    public IReadOnlyDictionary<string, int> Slimes(GameEnums names) =>
        ActorsByType
            .Select(kv => (Name: names.Item(kv.Key), Count: kv.Value))
            .Where(e => Items.KindOf(e.Name) is ItemKind.Slime or ItemKind.Largo)
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToDictionary(e => e.Name, e => e.Count);

    /// <summary>A few lines for logs and test output; names are used when <paramref name="names"/> is given.</summary>
    public string Describe(GameEnums? names = null)
    {
        var text = new StringBuilder();
        text.AppendLine($"Money {Money}, {Clock}");
        text.AppendLine($"Plots ({PlotsByType.Values.Sum()}): " + string.Join(", ",
            PlotsByType.Select(kv => $"{(names is null ? kv.Key.ToString() : names.PlotType(kv.Key))} {kv.Value}")));
        if (names is null)
        {
            text.AppendLine($"Actors ({ActorsByType.Values.Sum()}) by type number: " +
                string.Join(", ", ActorsByType.Select(kv => $"{kv.Key} {kv.Value}")));
        }
        else
        {
            var slimes = Slimes(names);
            text.AppendLine($"Slimes ({slimes.Values.Sum()}): " + string.Join(", ", slimes.Select(kv => $"{kv.Key} {kv.Value}")));
            var kinds = ActorsByType
                .GroupBy(kv => Items.KindOf(names.Item(kv.Key)))
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key} {g.Sum(kv => kv.Value)}");
            text.AppendLine($"Actors ({ActorsByType.Values.Sum()}) by kind: " + string.Join(", ", kinds));
        }
        return text.ToString();
    }
}
