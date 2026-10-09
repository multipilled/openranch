using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace OpenRanch.Game.RanchEconomy;

/// <summary>
/// A minimal menu screen: a title, a line of status text and a column of buttons, each named by a key
/// so that a headless check can press it (<see cref="Press"/>). openranch's own look; the original's
/// menu art is milestone 7. While open the game is paused (static analysis: the original's menus
/// pause the time director, docs/behavior/day-cycle.md) and the mouse is free.
/// </summary>
public partial class MenuScreen : CanvasLayer
{
    private readonly Label _title;
    private readonly Label _status;
    private readonly VBoxContainer _items;
    private Input.MouseModeEnum _mouseBefore;

    protected MenuScreen(string name)
    {
        Name = name;
        Layer = 20;
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;
        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -220, OffsetRight = 220, OffsetTop = -260, OffsetBottom = 260,
        };
        var column = new VBoxContainer();
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 22);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _items = new VBoxContainer();
        column.AddChild(_title);
        column.AddChild(_status);
        column.AddChild(new ScrollContainer { CustomMinimumSize = new Vector2(0, 380), SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        column.GetChild<ScrollContainer>(2).AddChild(_items);
        var close = new Button { Name = "close", Text = "Close" };
        close.Pressed += Close;
        column.AddChild(close);
        panel.AddChild(column);
        AddChild(panel);
    }

    public bool IsOpen => Visible;

    /// <summary>Whether closing is allowed now (the ranch house can't be left while sleeping).</summary>
    protected virtual bool CanClose => true;

    /// <summary>The status line: what the last button did.</summary>
    public string StatusText => _status.Text;

    /// <summary>The keys of the buttons shown, enabled or not.</summary>
    public IEnumerable<string> Keys => _items.GetChildren().OfType<Button>().Select(b => b.Name.ToString());

    public event Action? Closed;

    protected void ShowScreen(string title)
    {
        _title.Text = title;
        _status.Text = "";
        if (!Visible)
        {
            _mouseBefore = Input.MouseMode;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetTree().Paused = true;
        }
        Visible = true;
    }

    public void Close()
    {
        if (!Visible || !CanClose)
            return;
        Visible = false;
        GetTree().Paused = false;
        Input.MouseMode = _mouseBefore;
        Closed?.Invoke();
    }

    protected void ClearItems()
    {
        foreach (var child in _items.GetChildren())
        {
            _items.RemoveChild(child);
            child.QueueFree();
        }
    }

    protected void AddItem(string key, string text, bool enabled, Action pressed)
    {
        var button = new Button { Name = key, Text = text, Disabled = !enabled, Alignment = HorizontalAlignment.Left };
        button.Pressed += pressed;
        _items.AddChild(button);
    }

    protected void SetStatus(string text) => _status.Text = text;

    /// <summary>Presses the button named <paramref name="key"/>, as a click would; returns false when there is none or it is disabled.</summary>
    public bool Press(string key)
    {
        if (key == "close")
        {
            Close();
            return !Visible;
        }
        if (_items.GetChildren().OfType<Button>().FirstOrDefault(b => b.Name == key) is not { Disabled: false } button)
            return false;
        button.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Visible && e.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}
