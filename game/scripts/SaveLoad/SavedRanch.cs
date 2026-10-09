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

/// <summary>A slime the save keeps in one of the zone's corrals, where it stood and which way it faced (Unity coordinates).</summary>
public sealed record SavedSlime(string Id, N.Vector3 Position, N.Vector3 EulerDegrees);

/// <summary>
/// A ranch save opened for The Ranch (--save FILE): one of the original's version 12 saves or
/// openranch's own JSON save (src/OpenRanch.Ranch/RanchFiles.cs). The zone's plot sites carry the
/// plots the save built, made from the game's plot prefabs; the money and the slimes in the corrals
/// come back once milestone 2's world is up (<see cref="Populate"/>). The file is only read.
/// </summary>
public sealed class SavedRanch
{
    private readonly List<RenderItem> _renderers;
    private readonly List<ColliderItem> _colliders;

    private SavedRanch(RanchState ranch, IReadOnlyList<PlacedPlot> plots, WorldState state, List<RenderItem> renderers,
        List<ColliderItem> colliders, IReadOnlyList<SavedSlime> slimes)
    {
        Ranch = ranch;
        Plots = plots;
        State = state;
        _renderers = renderers;
        _colliders = colliders;
        CorralSlimes = slimes;
    }

    public RanchState Ranch { get; }
    /// <summary>The plots on the zone's sites. The save lists the other zones' sites too; those aren't built here.</summary>
    public IReadOnlyList<PlacedPlot> Plots { get; }
    /// <summary>Progress, hour and the scene plots to hide, for <see cref="ZoneExtractor.Extract"/>.</summary>
    public WorldState State { get; }
    /// <summary>The save's slimes and largos standing inside one of the zone's corrals.</summary>
    public IReadOnlyList<SavedSlime> CorralSlimes { get; }
    /// <summary>The expansion barriers (scene paths) the save has opened, which <see cref="State"/> hides.</summary>
    public IReadOnlyList<string> OpenedBarriers { get; private init; } = [];

    /// <summary>Reads the save and lays its plots out on the sites of <paramref name="zoneName"/>.</summary>
    /// <param name="assets">The asset set the zone is built from; the plots' meshes and materials are taken from it.</param>
    public static SavedRanch Load(GameInstall install, AssetSet assets, string path, string zoneName)
    {
        var ranch = RanchFiles.Read(path);
        using var scripts = new GameScripts(install);
        var scene = scripts.Assets.File("level3") ?? throw new IOException("level3 is missing.");
        var layout = PlotLayout.Read(scripts, scene, zoneName);
        var plots = layout.Place(ranch);
        var (hidden, renderers, colliders) = layout.Build(plots);
        using var names = new GameEnums(install);

        // The expansions the save has opened lose their barriers (a stand-in rule, see ExpansionBarriers).
        var barriers = ExpansionBarriers.Read(scripts, scene, zoneName, names);
        var opened = barriers.Where(b => ExpansionBarriers.IsOpen(b, ranch, names)).ToList();
        hidden.UnionWith(opened.Select(b => b.GameObject));

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
        foreach (var actor in ranch.Actors)
        {
            var id = names.Item(actor.TypeId);
            var at = new N.Vector3(actor.Position.X, actor.Position.Y, actor.Position.Z);
            if (Items.KindOf(id) is ItemKind.Slime or ItemKind.Largo && corrals.Any(p => p.Contains(at)))
                slimes.Add(new SavedSlime(id, at, new N.Vector3(actor.Rotation.X, actor.Rotation.Y, actor.Rotation.Z)));
        }

        var state = new WorldState(ranch.Player.Progress, (float)ranch.Clock.Hour, Hidden: hidden);
        return new SavedRanch(ranch, plots, state, renderers, colliders, slimes) { OpenedBarriers = opened.Select(b => b.Path).ToList() };
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

    /// <summary>Gives milestone 2's world the save's money and puts the corral slimes back where they were.</summary>
    public void Populate(Slimes.M2World m2)
    {
        m2.Wallet.Add(Ranch.Player.Money);
        Callable.From(() => SpawnSlimes(m2)).CallDeferred();
    }

    /// <summary>The corral slimes that went into the world, by id, filled in once they are spawned.</summary>
    public List<Slimes.Actor> Spawned { get; } = [];

    /// <summary>Corral slimes that couldn't be made because the install has no prefab for them.</summary>
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
            Spawned.Add(m2.Catalog.Spawn(slime.Id, UnityConvert.Position(slime.Position), -Mathf.DegToRad(slime.EulerDegrees.Y)));
        }
        GD.Print($"Save: {Ranch.Player.Money} money, {Plots.Count} plots on the zone's sites, {Spawned.Count} corral slimes" +
                 (Missing.Count > 0 ? $" ({Missing.Count} without a prefab: {string.Join(", ", Missing.Distinct())})" : ""));
    }
}
