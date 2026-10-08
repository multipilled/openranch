using System;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Game.Slimes;
using OpenRanch.Game.World;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Market;

/// <summary>
/// The plort market's deposit hole at the ranch (the ScorePlort trigger in the world scene): anything
/// the market buys that enters it is sold at today's price and paid into the wallet; anything else is
/// left alone. See docs/behavior/plort-market.md, "The market at the ranch".
/// </summary>
public partial class MarketStand : Area3D
{
    private readonly PlortMarket _market;
    private readonly Wallet _wallet;
    private readonly GameClock _clock;

    public MarketStand(MarketSite site, PlortMarket market, Wallet wallet, GameClock clock)
    {
        Name = "PlortMarket";
        _market = market;
        _wallet = wallet;
        _clock = clock;
        var world = UnityConvert.Transform(site.World);
        var scale = world.Basis.Scale.Abs();
        Transform = new Transform3D(world.Basis.Orthonormalized(), world.Origin);
        AddChild(new CollisionShape3D
        {
            Shape = new SphereShape3D { Radius = site.Radius * Math.Max(scale.X, Math.Max(scale.Y, scale.Z)) },
            Position = UnityConvert.Position(site.Center),
        });
        CollisionLayer = 0;
        CollisionMask = Actor.ActorLayer;
        Monitoring = true;
        Monitorable = false;
        BodyEntered += OnBodyEntered;
    }

    /// <summary>Raised for each sale: the item id and the coins paid.</summary>
    public event Action<string, int>? Sold;

    private void OnBodyEntered(Node3D body)
    {
        if (body is not Actor item || item.Consumed || !_market.Accepts(item.Id) || _market.IsClosed(_clock.HourOfDay))
            return;
        if (_market.Sell(item.Id) is not { } paid)
            return;
        item.Consume();
        _wallet.Add(paid);
        Sold?.Invoke(item.Id, paid);
    }
}
