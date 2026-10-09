using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Formats.Game;

/// <summary>
/// One gordo as the install defines it, from its prefab in the LookupDirector's gordo list:
/// <c>GordoEat</c> (the slime definition whose diet it eats, how much it must eat, how much it grows)
/// and <c>GordoRewards</c> (what pops out when it bursts). Ids are item names such as "PINK_GORDO".
/// See docs/behavior/gordos.md.
/// </summary>
/// <param name="Slime">The slime whose diet the gordo eats (the definition's item id).</param>
/// <param name="TargetCount">How much it has to eat before it bursts (<c>GordoEat.targetCount</c>).</param>
/// <param name="GrowthFactor">How many times its starting size it reaches when fully fed (<c>GordoEat.growthFactor</c>).</param>
/// <param name="Rewards">The default rewards, in order (<c>GordoRewards.rewardPrefabs</c>).</param>
/// <param name="FillSlime">The slime that fills the spawn points the rewards leave over (<c>GordoRewards.slimePrefab</c>).</param>
/// <param name="RewardOverrides">Rewards that replace the default ones in a game mode (by the mode's number).</param>
public sealed record GordoInfo(
    string Id,
    string Name,
    string Slime,
    int TargetCount,
    float GrowthFactor,
    IReadOnlyList<string> Rewards,
    string? FillSlime,
    IReadOnlyDictionary<int, IReadOnlyList<string>> RewardOverrides);

/// <summary>Every gordo in the install's LookupDirector gordo list. Nothing here is hard-coded.</summary>
public sealed class GordoData
{
    private GordoData(List<GordoInfo> gordos) => Gordos = gordos;

    public IReadOnlyList<GordoInfo> Gordos { get; }

    public GordoInfo? Get(string id) => Gordos.FirstOrDefault(g => g.Id == id);

    public static GordoData Read(GameScripts scripts)
    {
        var assets = scripts.Assets;
        var (lookupRef, lookup) = scripts.OfClass("LookupDirector").FirstOrDefault();
        var gordos = new List<GordoInfo>();
        if (lookup?.Data?["gordoEntries"] is not PPtr listPtr || scripts.Follow(lookupRef.File, listPtr) is not { } list)
            return new GordoData(gordos);

        foreach (var item in list.Data.Data!.List("items").OfType<PPtr>())
        {
            if (assets.Resolve(list.Ref.File, item) is not { ClassId: UnityClassId.GameObject } goRef)
                continue;
            var go = assets.Read(goRef, GameObjectData.Read);
            var parts = go.Components.Select(c => scripts.Follow(goRef.File, c)).OfType<(AssetRef Ref, MonoBehaviourData Data)>()
                .Where(m => m.Data is { ScriptClass: not null, Data: not null })
                .Select(m => (m.Data.ScriptClass!, m.Data.Data!));
            if (Build(scripts, goRef.File, go.Name, parts) is { } gordo)
                gordos.Add(gordo);
        }
        return new GordoData(gordos);
    }

    /// <summary>
    /// The gordos placed in one area of a scene (under its root object <paramref name="rootName"/>),
    /// each with its world matrix (Unity coordinates). A placed gordo keeps its own rewards, which
    /// differ from the prefab list's (keys, slime-specific crates).
    /// </summary>
    public static IReadOnlyList<(string Path, System.Numerics.Matrix4x4 World, GordoInfo Gordo)> Placed(GameScripts scripts, SerializedFile scene, string rootName)
    {
        var found = new List<(string, System.Numerics.Matrix4x4, GordoInfo)>();
        foreach (var g in SceneScripts.Find(scripts, scene, rootName, "GordoIdentifiable", "GordoEat", "BoomGordoEat", "GordoRewards").GroupBy(s => s.Path))
        {
            var name = g.Key[(g.Key.LastIndexOf('/') + 1)..];
            if (Build(scripts, scene, name, g.Select(s => (s.Class, s.Data))) is { } gordo)
                found.Add((g.Key, g.First().World, gordo));
        }
        return found;
    }

    private static GordoInfo? Build(GameScripts scripts, SerializedFile file, string name, IEnumerable<(string Class, SerializedObject Data)> parts)
    {
        var assets = scripts.Assets;
        var ids = scripts.IdentifiableIds;
        SerializedObject? identity = null, eat = null, rewards = null;
        foreach (var (cls, d) in parts)
        {
            if (cls == "GordoIdentifiable")
                identity = d;
            // Subclasses (BoomGordoEat and the event gordos') carry the same fields first.
            else if (cls.EndsWith("GordoEat", StringComparison.Ordinal))
                eat = d;
            else if (cls.EndsWith("GordoRewards", StringComparison.Ordinal))
                rewards = d;
        }
        if (identity is null || eat is null)
            return null;

        // The item id of the prefab a reference points at (its Identifiable or GordoIdentifiable).
        string? IdOf(object? value)
        {
            if (value is not PPtr p || assets.Resolve(file, p) is not { ClassId: UnityClassId.GameObject } go)
                return null;
            foreach (var c in assets.Read(go, GameObjectData.Read).Components)
                if (scripts.Follow(go.File, c) is { Data: { ScriptClass: "Identifiable" or "GordoIdentifiable", Data: { } d } })
                    return ids.NameOf(Convert.ToInt64(d["id"]));
            return null;
        }
        string? DefinitionId(object? value) =>
            value is PPtr p && scripts.Follow(file, p) is { Data: { ScriptClass: "SlimeDefinition", Data: { } d } }
                ? ids.NameOf(Convert.ToInt64(d["IdentifiableId"]))
                : null;

        List<string> Ids(IEnumerable<object?> refs) => refs.Select(IdOf).OfType<string>().ToList();
        var overrides = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var o in rewards?.List("rewardOverrides").OfType<SerializedObject>() ?? [])
            overrides[Convert.ToInt32(o["gameMode"])] = Ids(o.List("rewardPrefabs"));
        return new GordoInfo(
            ids.NameOf(Convert.ToInt64(identity["id"])), name, DefinitionId(eat["slimeDefinition"]) ?? "",
            eat.Get<int>("targetCount"), eat.Get<float>("growthFactor"),
            rewards is null ? [] : Ids(rewards.List("rewardPrefabs")),
            rewards is null ? null : IdOf(rewards["slimePrefab"]),
            overrides);
    }
}
