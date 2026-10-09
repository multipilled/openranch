using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Scene;
using OpenRanch.Formats.Unity;
using OpenRanch.Game.World;
using OpenRanch.Ranch;
using OpenRanch.Simulation;
using N = System.Numerics;
using WorldState = OpenRanch.Formats.Scene.WorldState;

namespace OpenRanch.Game.SaveLoad;

/// <summary>
/// A slime the save keeps in one of the zone's corrals, where it stood and which way it faced (Unity
/// coordinates), its actor id in the save, and its saved hunger and agitation (0 to 1) when the save has them.
/// </summary>
public sealed record SavedSlime(string Id, N.Vector3 Position, N.Vector3 EulerDegrees, long ActorId = 0, float? Hunger = null, float? Agitation = null);

/// <summary>
/// A ranch save opened for The Ranch (--save FILE): one of the original's version 12 saves or
/// openranch's own JSON save (src/OpenRanch.Ranch/RanchFiles.cs). The zone's plot sites carry the
/// plots the save built, made from the game's plot prefabs with their saved upgrades switched on,
/// and the crops planted on them; the money, the slimes in the corrals and the zone's loose actors
/// (food, plorts, chickens, produce hanging from crops) come back once milestone 2's world is up
/// (<see cref="Populate"/>). The file is only read.
/// </summary>
public sealed class SavedRanch
{
    private readonly List<RenderItem> _renderers;
    private readonly List<ColliderItem> _colliders;

    private SavedRanch(string path, RanchState ranch, IReadOnlyList<PlacedPlot> plots, WorldState state, List<RenderItem> renderers,
        List<ColliderItem> colliders, IReadOnlyList<SavedSlime> slimes)
    {
        FilePath = path;
        Ranch = ranch;
        Plots = plots;
        State = state;
        _renderers = renderers;
        _colliders = colliders;
        CorralSlimes = slimes;
    }

    /// <summary>The save file it was read from.</summary>
    public string FilePath { get; }
    public RanchState Ranch { get; }
    /// <summary>The plots on the zone's sites. The save lists the other zones' sites too; those aren't built here.</summary>
    public IReadOnlyList<PlacedPlot> Plots { get; }
    /// <summary>Progress, hour and the scene plots to hide, for <see cref="ZoneExtractor.Extract"/>.</summary>
    public WorldState State { get; }
    /// <summary>The save's slimes and largos standing inside one of the zone's corrals.</summary>
    public IReadOnlyList<SavedSlime> CorralSlimes { get; }
    /// <summary>The expansion barriers (scene paths) the save has opened, which <see cref="State"/> hides.</summary>
    public IReadOnlyList<string> OpenedBarriers { get; private init; } = [];
    /// <summary>The crops standing in the zone: those planted on the plots, then the scene's own.</summary>
    public IReadOnlyList<CropSpawner> Crops { get; private init; } = [];
    /// <summary>The zone's cells, which decide which of the save's actors are loaded here.</summary>
    public ZoneRegions Zone { get; private init; } = new(0, []);
    /// <summary>What each plot on the zone's sites holds (crop, feeder, collector, stores, ash), by site id.</summary>
    public IReadOnlyDictionary<string, PlotContents> Contents { get; private init; } = new Dictionary<string, PlotContents>();

    /// <summary>Reads the save and lays its plots out on the sites of <paramref name="zoneName"/>.</summary>
    /// <param name="assets">The asset set the zone is built from; the plots' meshes and materials are taken from it.</param>
    public static SavedRanch Load(GameInstall install, AssetSet assets, string path, string zoneName)
    {
        var ranch = RanchFiles.Read(path);
        using var scripts = new GameScripts(install);
        using var names = new GameEnums(install);
        var scene = scripts.Assets.File("level3") ?? throw new IOException("level3 is missing.");
        var layout = PlotLayout.Read(scripts, scene, zoneName);
        var plots = layout.Place(ranch, names);
        var (hidden, renderers, colliders) = layout.Build(plots);

        // The expansions the save has opened lose their barriers (a stand-in rule, see ExpansionBarriers).
        var barriers = ExpansionBarriers.Read(scripts, scene, zoneName, names);
        var opened = barriers.Where(b => ExpansionBarriers.IsOpen(b, ranch, names)).ToList();
        hidden.UnionWith(opened.Select(b => b.GameObject));

        // Each plot's saved crop stands on it; with the scene's own crops, their spawn joints hold produce.
        var cropPrefabs = PlotCrops.Read(scripts, scene);
        var plotCrops = plots.Select(cropPrefabs.On).OfType<CropSpawner>().ToList();
        var (cropRenderers, cropColliders) = PlotCrops.Build(plotCrops);
        renderers.AddRange(cropRenderers);
        colliders.AddRange(cropColliders);
        var crops = plotCrops.Concat(PlotCrops.InScene(scripts, scene, zoneName, hidden)).ToList();

        // The ranch's cells are in the "HOME" region set (static analysis of Region.Awake and ZoneDirector.GetRegionSetId).
        var zone = ZoneRegions.Read(scripts, scene, zoneName, names.Value(GameEnum.RegionSet, "HOME"));
        var contents = plots.ToDictionary(p => p.Site.Id, p => PlotContents.Of(p.Plot, names));

        // The prefabs were read through the scripts' own asset set, which closes with it.
        AssetRef Rebase(AssetRef r) => new(assets.File(ExternalName(r.File.Path, install)) ?? throw new IOException($"{r.File.Path} is missing."), r.Info);
        renderers = renderers.Select(r => r with
        {
            Mesh = Rebase(r.Mesh),
            Materials = r.Materials.Select(m => m is { } a ? Rebase(a) : (AssetRef?)null).ToList(),
        }).ToList();
        colliders = colliders.Select(c => c with { Mesh = c.Mesh is { } m ? Rebase(m) : null }).ToList();

        var corrals = plots.Where(p => p.Prefab.Regions.Count > 0).ToList();
        var slimes = new List<SavedSlime>();
        var hunger = names.Value(GameEnum.Emotion, "HUNGER");
        var agitation = names.Value(GameEnum.Emotion, "AGITATION");
        foreach (var actor in ranch.Actors)
        {
            var id = names.Item(actor.TypeId);
            var at = new N.Vector3(actor.Position.X, actor.Position.Y, actor.Position.Z);
            if (Items.KindOf(id) is ItemKind.Slime or ItemKind.Largo && corrals.Any(p => p.Contains(at)))
                slimes.Add(new SavedSlime(id, at, new N.Vector3(actor.Rotation.X, actor.Rotation.Y, actor.Rotation.Z), actor.ActorId,
                    actor.Emotions.TryGetValue(hunger, out var h) ? h : null, actor.Emotions.TryGetValue(agitation, out var a) ? a : null));
        }

        var state = new WorldState(ranch.Player.Progress, (float)ranch.Clock.Hour, Hidden: hidden);
        return new SavedRanch(path, ranch, plots, state, renderers, colliders, slimes)
        {
            OpenedBarriers = opened.Select(b => b.Path).ToList(),
            Crops = crops,
            Zone = zone,
            Contents = contents,
        };
    }

    // The name an external reference would use for a file of the install's data folder.
    private static string ExternalName(string filePath, GameInstall install)
    {
        var dir = Path.GetFullPath(Path.GetDirectoryName(filePath)!).TrimEnd('\\', '/');
        var resources = Path.GetFullPath(Path.Combine(install.DataDirectory, "Resources")).TrimEnd('\\', '/');
        var file = Path.GetFileName(filePath);
        return string.Equals(dir, resources, System.StringComparison.OrdinalIgnoreCase) ? "Resources/" + file : file;
    }

    /// <summary>The zone with the saved plots in place of the scene's own (which <see cref="State"/> hid).</summary>
    public ZoneExtract Apply(ZoneExtract zone) => zone with
    {
        Renderers = zone.Renderers.Concat(_renderers).ToList(),
        Colliders = zone.Colliders.Concat(_colliders).ToList(),
    };

    /// <summary>
    /// Gives milestone 2's world the save's money and puts the corral slimes and the zone's loose
    /// actors back where they were.
    /// </summary>
    public void Populate(Slimes.M2World m2)
    {
        m2.Wallet.Add(Ranch.Player.Money);
        var names = new GameEnums(m2.Scripts.Types);
        LooseActors = ZoneActors.Of(Ranch, Zone, names, Crops, id => GrowsOnCrops(m2, id));
        Callable.From(() => SpawnSlimes(m2)).CallDeferred();
        Callable.From(() => SpawnLoose(m2)).CallDeferred();
    }

    /// <summary>The save's loose actors in the zone (everything but slimes), set by <see cref="Populate"/>.</summary>
    public IReadOnlyList<ZoneActor> LooseActors { get; private set; } = [];

    /// <summary>The loose actors that went into the world, with what the save said about each.</summary>
    public List<(ZoneActor Saved, Slimes.Actor Actor)> SpawnedLoose { get; } = [];

    /// <summary>Holds the produce hanging from crops until it is picked.</summary>
    public CropHold? Hold { get; private set; }

    // Produce has a ResourceCycle script on its prefab's root.
    private static bool GrowsOnCrops(Slimes.M2World m2, string id) =>
        m2.Catalog.Prefabs.Has(id) && m2.Catalog.Prefabs.Get(id).RootScript("ResourceCycle") is not null;

    private void SpawnLoose(Slimes.M2World m2)
    {
        Hold = new CropHold();
        m2.AddChild(Hold);
        var missing = new List<string>();
        foreach (var saved in LooseActors)
        {
            if (!m2.Catalog.Prefabs.Has(saved.Id))
            {
                missing.Add(saved.Id);
                continue;
            }
            var actor = m2.Catalog.Spawn(saved.Id, UnityConvert.Position(saved.Position));
            // Unity's Euler angles turn about Z, then X, then Y, as System.Numerics' yaw, pitch and roll do.
            var e = saved.EulerDegrees * (Mathf.Pi / 180);
            actor.Transform = UnityConvert.Transform(N.Matrix4x4.CreateFromYawPitchRoll(e.Y, e.X, e.Z) * N.Matrix4x4.CreateTranslation(saved.Position));
            if (saved.Joint is not null)
                Hold.Add(actor, saved.Unripe, m2.Catalog.Prefabs.Get(saved.Id).RootFloat("ResourceCycle", "releasePrepTime", 0));
            SpawnedLoose.Add((saved, actor));
            _spawnedIds[actor] = saved.Saved.ActorId;
        }
        Missing.AddRange(missing);
        GD.Print($"Save: {SpawnedLoose.Count} loose actors in the zone, {Hold.Count} hanging from {Crops.Count} crops" +
                 (missing.Count > 0 ? $" ({missing.Count} without a prefab: {string.Join(", ", missing.Distinct())})" : ""));
    }

    /// <summary>The corral slimes that went into the world, by id, filled in once they are spawned.</summary>
    public List<Slimes.Actor> Spawned { get; } = [];

    private readonly Dictionary<Slimes.Actor, long> _spawnedIds = [];

    /// <summary>The save's actor id of every actor the loader put into the world (corral slimes and loose actors).</summary>
    public IReadOnlyDictionary<Slimes.Actor, long> SpawnedIds => _spawnedIds;

    /// <summary>Corral slimes and loose actors that couldn't be made because the install has no prefab for them.</summary>
    public List<string> Missing { get; } = [];

    private void SpawnSlimes(Slimes.M2World m2)
    {
        foreach (var slime in CorralSlimes)
        {
            if (!m2.Catalog.Prefabs.Has(slime.Id))
            {
                Missing.Add(slime.Id);
                continue;
            }
            // Unity's yaw turns clockwise seen from above; Godot's turns the other way.
            var actor = m2.Catalog.Spawn(slime.Id, UnityConvert.Position(slime.Position), -Mathf.DegToRad(slime.EulerDegrees.Y));
            // The save's moods (SlimeEmotions) replace the species' starting ones.
            if (actor is Slimes.SlimeActor s)
            {
                if (slime.Hunger is { } hunger)
                    s.Sim.Hunger = hunger;
                if (slime.Agitation is { } agitation)
                    s.Sim.Agitation = agitation;
            }
            Spawned.Add(actor);
            _spawnedIds[actor] = slime.ActorId;
        }
        GD.Print($"Save: {Ranch.Player.Money} money, {Plots.Count} plots on the zone's sites, {Spawned.Count} corral slimes" +
                 (Missing.Count > 0 ? $" ({Missing.Count} without a prefab: {string.Join(", ", Missing.Distinct())})" : ""));
    }
}
