using System.Text;
using Godot;
using OpenRanch.Simulation;

namespace OpenRanch.Game.Market;

/// <summary>A plain text HUD: the player's newbucks and the vacpack's slots, the selected one marked.</summary>
public partial class Hud : CanvasLayer
{
    private readonly Wallet _wallet;
    private readonly Simulation.Vacpack _pack;
    private readonly Label _label = new() { Position = new Vector2(24, 24) };
    private string _shown = "";

    public Hud(Wallet wallet, Simulation.Vacpack pack)
    {
        Name = "Hud";
        _wallet = wallet;
        _pack = pack;
        _label.AddThemeColorOverride("font_color", Colors.White);
        _label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        var text = new StringBuilder();
        text.Append($"{_wallet.Coins:N0} newbucks");
        for (var i = 0; i < _pack.UsableSlots; i++)
        {
            var slot = _pack[i];
            text.Append('\n').Append(i == _pack.SelectedSlot ? "> " : "  ").Append(i + 1).Append(": ")
                .Append(slot is null ? "-" : $"{slot.Id} x{slot.Count}");
        }
        var s = text.ToString();
        if (s != _shown)
            _label.Text = _shown = s;
    }
}
