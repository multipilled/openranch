using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Formats.Game;

/// <summary>What a slime eats and what it makes, from its SlimeDefinition's Diet. Ids are names such as "CARROT_VEGGIE".</summary>
public sealed record SlimeDietData(
    IReadOnlyList<string> FoodGroups,
    IReadOnlyList<string> Favorites,
    IReadOnlyList<string> AdditionalFoods,
    IReadOnlyList<string> Produces,
    int FavoriteProductionCount);

/// <summary>Eating tuning from the slime prefab's SlimeEat component. See docs/behavior/slimes.md.</summary>
public sealed record SlimeEatingData(float MinDriveToEat, float DrivePerEat, float AgitationPerEat, float AgitationPerFavoriteEat, float ChanceToSkipProduce);

/// <summary>One emotion (hunger, agitation) from the slime prefab's SlimeEmotions component: where it starts, where it drifts to, and how fast.</summary>
public sealed record EmotionTuning(float Start, float Rest, float DriftPerGameHour);

/// <summary>
/// One slime type as the game defines it. A largo names its two base slimes (<see cref="BaseSlimes"/>,
/// the definition's <c>BaseSlimes</c>, as item ids); its diet is stored already combined. See docs/behavior/largos.md.
/// </summary>
public sealed record SlimeInfo(
    string AssetName,
    string Name,
    string Id,
    bool IsLargo,
    bool CanLargofy,
    SlimeDietData Diet,
    SlimeEatingData? Eating,
    EmotionTuning? Hunger,
    EmotionTuning? Agitation,
    IReadOnlyList<string>? BaseSlimes = null);

/// <summary>
/// Slime definitions read from the player's install: diet, favorites, plorts, and the eating and
/// hunger tuning on each slime's prefab. Nothing here is hard-coded.
/// </summary>
public sealed class SlimeData
{
    private readonly Dictionary<string, SlimeInfo> _byId;

    private SlimeData(List<SlimeInfo> slimes)
    {
        Slimes = slimes;
        _byId = new Dictionary<string, SlimeInfo>(StringComparer.Ordinal);
        foreach (var s in slimes)
            _byId.TryAdd(s.Id, s);
    }

    public IReadOnlyList<SlimeInfo> Slimes { get; }

    public SlimeInfo Get(string id) =>
        _byId.TryGetValue(id, out var slime) ? slime : throw new KeyNotFoundException($"No slime definition for {id}.");

    public bool TryGet(string id, out SlimeInfo slime) => _byId.TryGetValue(id, out slime!);

    /// <summary>A set of slime types made by hand, for tests.</summary>
    public static SlimeData FromInfos(IEnumerable<SlimeInfo> slimes) => new(slimes.ToList());

    public static SlimeData Read(GameScripts scripts)
    {
        var ids = scripts.IdentifiableIds;
        var groups = scripts.Enum("", "SlimeEat/FoodGroup");

        // Eating and hunger tuning live on the prefabs (and on every placed copy of them); the first
        // SlimeEat that points at a definition, plus the SlimeEmotions beside it, stands for that slime.
        var eating = new Dictionary<(string File, long PathId), (SlimeEatingData Eat, EmotionTuning? Hunger, EmotionTuning? Agitation)>();
        var emotionsByObject = scripts.OfClass("SlimeEmotions")
            .GroupBy(m => (m.Ref.File.Path, m.Data.GameObject.PathId))
            .ToDictionary(g => g.Key, g => g.First().Data.Data!);
        foreach (var (asset, mb) in scripts.OfClass("SlimeEat"))
        {
            var def = mb.Data!["slimeDefinition"] is PPtr p ? scripts.Assets.Resolve(asset.File, p) : null;
            if (def is null)
                continue;
            var key = (def.Value.File.Path, def.Value.PathId);
            if (eating.ContainsKey(key))
                continue;
            var d = mb.Data;
            var eat = new SlimeEatingData(
                d.Get<float>("minDriveToEat"), d.Get<float>("drivePerEat"),
                d.Get<float>("agitationPerEat"), d.Get<float>("agitationPerFavEat"),
                d.Get<float>("chanceToSkipProduce"));
            emotionsByObject.TryGetValue((asset.File.Path, mb.GameObject.PathId), out var emotions);
            eating[key] = (eat, Emotion(emotions, "initHunger"), Emotion(emotions, "initAgitation"));
        }

        // Base slimes are references to other definitions; name them by their item ids.
        var definitions = scripts.OfClass("SlimeDefinition").ToList();
        var idOfDefinition = definitions.ToDictionary(m => (m.Ref.File.Path, m.Ref.PathId), m => ids.NameOf(Convert.ToInt64(m.Data.Data!["IdentifiableId"])));
        var slimes = new List<SlimeInfo>();
        foreach (var (asset, mb) in definitions)
        {
            var d = mb.Data!;
            var bases = d.List("BaseSlimes").OfType<PPtr>()
                .Select(p => scripts.Assets.Resolve(asset.File, p) is { } r && idOfDefinition.TryGetValue((r.File.Path, r.PathId), out var id) ? id : null)
                .OfType<string>().ToList();
            var diet = d.Object("Diet")!;
            List<string> Names(SerializedObject o, string field, EnumValues e) =>
                o.List(field).Select(v => e.NameOf(Convert.ToInt64(v))).ToList();
            var dietData = new SlimeDietData(
                Names(diet, "MajorFoodGroups", groups),
                Names(diet, "Favorites", ids),
                Names(diet, "AdditionalFoods", ids),
                Names(diet, "Produces", ids),
                diet.Get<int>("FavoriteProductionCount"));
            var tuned = eating.TryGetValue((asset.File.Path, asset.PathId), out var tuning);
            slimes.Add(new SlimeInfo(
                mb.Name, d.Get<string>("Name"), ids.NameOf(Convert.ToInt64(d["IdentifiableId"])),
                d.Get<bool>("IsLargo"), d.Get<bool>("CanLargofy"), dietData, tuned ? tuning.Eat : null, tuned ? tuning.Hunger : null, tuned ? tuning.Agitation : null,
                bases));
        }
        return new SlimeData(slimes);
    }

    private static EmotionTuning? Emotion(SerializedObject? emotions, string field) =>
        emotions?.Object(field) is { } e
            ? new EmotionTuning(e.Get<float>("currVal"), e.Get<float>("defVal"), e.Get<float>("recoveryPerGameHour"))
            : null;
}
