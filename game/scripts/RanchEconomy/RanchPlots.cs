using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Scene;
using OpenRanch.Game.World;
using OpenRanch.Ranch;
using N = System.Numerics;

namespace OpenRanch.Game.RanchEconomy;

/// <summary>Pays from the running world's wallet (milestone 2's newbucks on the HUD).</summary>
public sealed class WalletPurse(Simulation.Wallet wallet) : IPurse
{
    public int Money => wallet.Coins;
    public bool TrySpend(int amount) => wallet.TrySpend(amount);
}

/// <summary>
/// The plots on the zone's sites in play (docs/behavior/plots.md): each site is its own node, built
/// from its plot type's prefab with its upgrades switched (src/OpenRanch.Ranch/PlotUpgrades.cs), its
/// crop, its corral walls and its menu activator, and rebuilt when something is built, demolished,
/// upgraded, planted or cleared there. Buying goes through <see cref="PlotRules"/> with the prices
/// read from the install and is paid from the wallet. The plots live in the ranch itself
/// (<see cref="RanchState.Plots"/>), which the save writer writes.
/// </summary>
public partial class RanchPlots : Node3D
{
    /// <summary>A site in play: what stands on it now, its crop, and its node.</summary>
    public sealed class Site(PlotSite info)
    {
        public PlotSite Info { get; } = info;
        public PlacedPlot Placed { get; set; } = null!;
        public CropSpawner? Crop { get; set; }
        public Node3D? Node { get; set; }
        public List<RenderItem> Renderers { get; set; } = [];
    }

    /// <summary>The meta key on a plot's activator area holding its site id.</summary>
    public const string SiteMeta = "plot_site";

    private readonly SaveLoad.SavedRanch _saved;
    private readonly ZoneExtract _zone;
    private readonly Slimes.M2World _m2;
    private readonly PhysicsLayers _layers;
    private readonly Dictionary<string, Site> _sites = new();
    private readonly int _noCrop;

    public RanchPlots(SaveLoad.SavedRanch saved, ZoneExtract zone, Slimes.M2World m2, PhysicsLayers layers, GameEnums names)
    {
        Name = "Plots";
        _saved = saved;
        _zone = zone;
        _m2 = m2;
        _layers = layers;
        Names = names;
        var scene = m2.Scripts.Assets.File("level3") ?? throw new System.IO.IOException("level3 is missing.");
        Layout = PlotLayout.Read(m2.Scripts, scene, zone.Name);
        Crops = PlotCrops.Read(m2.Scripts, scene);
        Catalog = PlotCatalog.Read(m2.Scripts);
        Rules = new PlotRules(Catalog, names);
        Purse = new WalletPurse(m2.Wallet);
        _noCrop = names.Value(GameEnum.SpawnResource, "NONE");
        foreach (var site in Layout.Sites)
        {
            // A save lists every site; a ranch that doesn't gets the scene's own plot there, as stored.
            if (Ranch.FindPlot(site.Id) is null)
                Ranch.Plots.Add(new Plot { Id = site.Id, Type = site.SceneType, AttachedResource = _noCrop });
            _sites[site.Id] = new Site(site);
        }
    }

    public RanchState Ranch => _saved.Ranch;
    public GameEnums Names { get; }
    public PlotLayout Layout { get; }
    public PlotCatalog Catalog { get; }
    public PlotCrops Crops { get; }
    public PlotRules Rules { get; }
    public IPurse Purse { get; }

    public IEnumerable<Site> Sites => _sites.Values;
    public Site? Get(string siteId) => _sites.GetValueOrDefault(siteId);

    /// <summary>Raised after a site was rebuilt because of a purchase or planting.</summary>
    public event Action<string>? Changed;

    /// <summary>What the plots and their crops draw now, under "site/prefab/..." (as the zone builder would).</summary>
    public IEnumerable<RenderItem> Renderers => _sites.Values.SelectMany(s => s.Renderers);

    /// <summary>The crops standing on plots now.</summary>
    public IEnumerable<CropSpawner> PlantedCrops => _sites.Values.Select(s => s.Crop).OfType<CropSpawner>();

    public override void _Ready()
    {
        foreach (var site in _sites.Values)
            Rebuild(site);
        GD.Print($"Plots: {_sites.Count} sites in play ({string.Join(", ", _sites.Values.GroupBy(s => Names.PlotType(s.Placed.Plot.Type)).Select(g => $"{g.Count()} {g.Key}"))})");
    }

    public PlotPurchase Build(string siteId, int type) => Do(siteId, () => Rules.Replace(Ranch, siteId, type, Purse));
    public PlotPurchase Upgrade(string siteId, int upgrade) => Do(siteId, () => Rules.Upgrade(Ranch, siteId, upgrade, Purse));
    public PlotPurchase ClearCrop(string siteId) => Do(siteId, () => Rules.ClearCrop(Ranch, siteId, Purse));

    /// <summary>
    /// Plants <paramref name="crop"/> (<see cref="GameEnum.SpawnResource"/>) on the site: the crop
    /// prefab stands on the plot root (docs/behavior/plots.md, "Crops") and starts growing.
    /// </summary>
    public bool Plant(string siteId, int crop)
    {
        if (Ranch.FindPlot(siteId) is not { } plot || !Crops.Prefabs.ContainsKey(crop))
            return false;
        plot.AttachedResource = crop;
        plot.AttachedDeathTime = 0;
        Rebuild(_sites[siteId]);
        Changed?.Invoke(siteId);
        return true;
    }

    private PlotPurchase Do(string siteId, Func<PlotPurchase> purchase)
    {
        if (!_sites.TryGetValue(siteId, out var site))
            return PlotPurchase.NoSuchPlot;
        var result = purchase();
        if (result == PlotPurchase.Done)
        {
            Rebuild(site);
            Changed?.Invoke(siteId);
        }
        return result;
    }

    /// <summary>The corral (or other plot with regions) whose inside holds <paramref name="godot"/>.</summary>
    public Site? RegionAt(Vector3 godot)
    {
        var unity = new N.Vector3(godot.X, godot.Y, -godot.Z);
        return _sites.Values.FirstOrDefault(s => s.Placed.Contains(unity));
    }

    /// <summary>Whether a point is inside one of the site's trigger boxes of <paramref name="script"/> (a coop's CoopRegion and so on), switched on.</summary>
    public bool InTrigger(Site site, string script, Vector3 godot)
    {
        var unity = new N.Vector3(godot.X, godot.Y, -godot.Z);
        var tree = site.Placed.Prefab.Tree;
        return tree is not null && tree.Triggers.Any(t => t.Region.Class == script && tree.IsOn(t.Chain, site.Placed.Upgrades.Switched)
                                                          && t.Region.Contains(site.Info.PlotWorld, unity));
    }

    private void Rebuild(Site site)
    {
        var plot = Ranch.FindPlot(site.Info.Id)!;
        site.Node?.QueueFree();
        site.Node = null;
        site.Renderers = [];
        site.Crop = null;
        if (!Layout.Prefabs.TryGetValue(plot.Type, out var prefab))
            return;
        var placed = new PlacedPlot(site.Info, plot, prefab) { Upgrades = PlotUpgrades.Apply(prefab.Upgraders, plot.Upgrades, Names) };
        site.Placed = placed;
        var (_, renderers, colliders) = Layout.Build([placed]);
        site.Crop = Crops.On(placed);
        if (site.Crop is not null)
        {
            var (cropRenderers, cropColliders) = OpenRanch.Ranch.PlotCrops.Build([site.Crop]);
            renderers.AddRange(cropRenderers);
            colliders.AddRange(cropColliders);
        }
        site.Renderers = renderers;

        var part = _zone with { Name = site.Info.Id, Renderers = renderers, Colliders = colliders };
        var node = ZoneBuilder.Build(part, _m2.Catalog.World, _layers).Root;
        node.AddChild(Slimes.PenWalls.Build(part, _layers));
        // The menu activator(s): the prefab's UIActivator trigger boxes the player looks at and uses.
        var tree = prefab.Tree!;
        foreach (var trigger in tree.Triggers.Where(t => t.Region.Class == "UIActivator" && tree.IsOn(t.Chain, placed.Upgrades.Switched)))
            node.AddChild(Activator(trigger.Region.ToRoot * site.Info.PlotWorld, trigger.Region, SiteMeta, site.Info.Id, trigger.Sphere));
        site.Node = node;
        AddChild(node);
    }

    /// <summary>An area the player's interact ray can hit (<see cref="Home.RanchHouse.InteractLayer"/>), tagged with <paramref name="meta"/>.</summary>
    public static Area3D Activator(N.Matrix4x4 unityWorld, PlotRegion box, string meta, string value, bool sphere = false)
    {
        var world = UnityConvert.Transform(unityWorld);
        var scale = world.Basis.Scale.Abs();
        var area = new Area3D
        {
            Name = "Activator", Transform = new Transform3D(world.Basis.Orthonormalized(), world.Origin),
            CollisionLayer = Home.RanchHouse.InteractLayer, CollisionMask = 0, Monitoring = false, Monitorable = true,
        };
        // Unity takes a box collider's size as its absolute value.
        // A sphere collider scales with the largest axis (as Unity does).
        Shape3D shape = sphere
            ? new SphereShape3D { Radius = box.Size.X / 2 * Mathf.Max(scale.X, Mathf.Max(scale.Y, scale.Z)) }
            : new BoxShape3D { Size = UnityConvert.Position(box.Size).Abs() * scale };
        area.AddChild(new CollisionShape3D { Shape = shape, Position = UnityConvert.Position(box.Center) * scale });
        area.SetMeta(meta, value);
        return area;
    }
}
