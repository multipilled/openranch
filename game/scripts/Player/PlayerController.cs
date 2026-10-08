using Godot;

namespace OpenRanch.Game.Player;

/// <summary>
/// First-person rancher: walk, sprint, jump, and a jetpack that lifts while jump is held in the air
/// and energy lasts. Body size and slope limit come from the original player rig; speeds are our own
/// first tuning and will be matched to the original later.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    [Export] public float WalkSpeed = 7.5f;
    [Export] public float SprintSpeed = 11.5f;
    [Export] public float Acceleration = 40f;
    [Export] public float AirControl = 0.35f;
    [Export] public float JumpSpeed = 7f;
    [Export] public float Gravity = 22f;
    [Export] public float JetpackThrust = 34f;
    [Export] public float JetpackMaxRise = 6f;
    [Export] public float MaxEnergy = 100f;
    [Export] public float EnergyUsePerSecond = 25f;
    [Export] public float EnergyRegenPerSecond = 15f;
    [Export] public float MouseSensitivity = 0.0025f;

    public Camera3D Camera { get; private set; } = null!;
    public float Energy { get; private set; }

    private float _pitch;
    private bool _jumpHeldSinceGround;

    /// <summary>Sets the body to the original rig's capsule and eye height.</summary>
    public void Configure(float height, float radius, float slopeDegrees, float eyeHeight)
    {
        var shape = new CapsuleShape3D { Radius = radius, Height = height };
        AddChild(new CollisionShape3D { Shape = shape, Position = new Vector3(0, height / 2, 0) });
        FloorMaxAngle = Mathf.DegToRad(slopeDegrees);
        FloorSnapLength = 0.5f;
        Camera = new Camera3D { Position = new Vector3(0, eyeHeight, 0), Fov = 75, Current = true, Far = 2000 };
        AddChild(Camera);
        Energy = MaxEnergy;
    }

    public void Look(float yawDegrees, float pitchDegrees)
    {
        Rotation = new Vector3(0, Mathf.DegToRad(yawDegrees), 0);
        _pitch = Mathf.DegToRad(pitchDegrees);
        Camera.Rotation = new Vector3(_pitch, 0, 0);
    }

    public override void _Ready()
    {
        EnsureAction("move_forward", Key.W);
        EnsureAction("move_back", Key.S);
        EnsureAction("move_left", Key.A);
        EnsureAction("move_right", Key.D);
        EnsureAction("jump", Key.Space);
        EnsureAction("sprint", Key.Shift);
        EnsureAction("release_mouse", Key.Escape);
    }

    private static void EnsureAction(string name, Key key)
    {
        if (InputMap.HasAction(name))
            return;
        InputMap.AddAction(name);
        InputMap.ActionAddEvent(name, new InputEventKey { PhysicalKeycode = key });
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
        else if (e.IsActionPressed("release_mouse"))
            Input.MouseMode = Input.MouseModeEnum.Visible;
        else if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-motion.Relative.X * MouseSensitivity);
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity, -1.5f, 1.5f);
            Camera.Rotation = new Vector3(_pitch, 0, 0);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var wish = (Transform.Basis * new Vector3(input.X, 0, input.Y)) with { Y = 0 };
        if (wish.LengthSquared() > 1)
            wish = wish.Normalized();
        var speed = Input.IsActionPressed("sprint") ? SprintSpeed : WalkSpeed;
        var velocity = Velocity;
        var horizontal = new Vector3(velocity.X, 0, velocity.Z);
        var control = IsOnFloor() ? 1f : AirControl;
        horizontal = horizontal.MoveToward(wish * speed, Acceleration * control * dt);

        if (IsOnFloor())
        {
            _jumpHeldSinceGround = false;
            Energy = Mathf.Min(MaxEnergy, Energy + EnergyRegenPerSecond * dt);
            if (Input.IsActionJustPressed("jump"))
            {
                velocity.Y = JumpSpeed;
                _jumpHeldSinceGround = true;
            }
        }
        else
        {
            velocity.Y -= Gravity * dt;
            var jetpack = Input.IsActionPressed("jump") && Energy > 0 && !(_jumpHeldSinceGround && velocity.Y > 0);
            if (!Input.IsActionPressed("jump"))
                _jumpHeldSinceGround = false;
            if (jetpack)
            {
                velocity.Y = Mathf.Min(velocity.Y + JetpackThrust * dt, JetpackMaxRise);
                Energy = Mathf.Max(0, Energy - EnergyUsePerSecond * dt);
            }
        }

        Velocity = new Vector3(horizontal.X, velocity.Y, horizontal.Z);
        MoveAndSlide();
    }
}
