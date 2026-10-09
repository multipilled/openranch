using Godot;
using OpenRanch.Game.Home;
using OpenRanch.Game.Player;

namespace OpenRanch.Game.RanchEconomy;

/// <summary>
/// Using what the player looks at (docs/behavior/day-cycle.md, "Sleeping", has the rule): a ray from
/// the middle of the view, as long as the player rig's reach (<c>UIDetector.interactDistance</c>),
/// acting when the interact button is released. A plot's activator opens its <see cref="PlotMenu"/>; an
/// expansion door's opens its <see cref="DoorMenu"/> while it is locked, and opens the door otherwise.
/// The ranch house's door is handled by <see cref="RanchHouse"/> itself.
/// </summary>
public partial class Interactor : Node
{
    private readonly PlayerController _player;
    private readonly float _reach;
    private readonly PlotMenu _plotMenu;
    private readonly DoorMenu _doorMenu;
    private readonly ExpansionShop _shop;

    public Interactor(PlayerController player, float reach, PlotMenu plotMenu, DoorMenu doorMenu, ExpansionShop shop)
    {
        Name = "Interactor";
        _player = player;
        _reach = reach;
        _plotMenu = plotMenu;
        _doorMenu = doorMenu;
        _shop = shop;
    }

    public float Reach => _reach;

    public override void _Ready()
    {
        if (InputMap.HasAction(RanchHouse.InteractAction))
            return;
        InputMap.AddAction(RanchHouse.InteractAction);
        InputMap.ActionAddEvent(RanchHouse.InteractAction, new InputEventKey { PhysicalKeycode = Key.E });
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionReleased(RanchHouse.InteractAction))
            Use();
    }

    /// <summary>The activator area the middle of the view meets within reach, if any.</summary>
    public Area3D? LookedAt()
    {
        var camera = _player.Camera.GlobalTransform;
        var from = camera.Origin;
        var to = from - camera.Basis.Z.Normalized() * _reach;
        var space = _player.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to, RanchHouse.InteractLayer, [_player.GetRid()]);
        query.CollideWithAreas = true;
        query.CollideWithBodies = false;
        var hit = space.IntersectRay(query);
        if (hit.Count == 0 || hit["collider"].AsGodotObject() is not Area3D area)
            return null;
        // Solid things in front hide it. Some activators are solid colliders as well (the plots' and the
        // doors' spheres), so a solid hit where the activator is doesn't count.
        var solid = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, Slimes.Actor.WorldLayer, [_player.GetRid()]));
        var toArea = from.DistanceTo(hit["position"].AsVector3());
        return solid.Count == 0 || from.DistanceTo(solid["position"].AsVector3()) >= toArea - 0.05f ? area : null;
    }

    /// <summary>Uses the activator the player looks at; returns what it opened ("plot", "door", "opened") or null.</summary>
    public string? Use()
    {
        if (GetTree().Paused || LookedAt() is not { } area)
            return null;
        if (area.HasMeta(RanchPlots.SiteMeta))
        {
            _plotMenu.Open(area.GetMeta(RanchPlots.SiteMeta).AsString());
            return "plot";
        }
        if (area.HasMeta(ExpansionShop.DoorMeta))
        {
            var door = area.GetMeta(ExpansionShop.DoorMeta).AsString();
            if (_shop.Open(door))
                return "opened";
            _doorMenu.Open(door);
            return "door";
        }
        return null;
    }
}
