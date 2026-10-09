using System.Threading.Tasks;

namespace OpenRanch.Game.RanchEconomy;

/// <summary>Part 3 of <see cref="M6Check"/>: produce growing on a crop and a coop laying.</summary>
public sealed class ProduceCheck(M6Check check, Economy economy)
{
    public Task Run() => Task.CompletedTask;
}
