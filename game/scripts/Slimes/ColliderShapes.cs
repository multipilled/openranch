using System;
using Godot;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// Turns a Unity box, sphere or capsule collider into a Godot collision shape. <paramref name="place"/>
/// is the collider object's transform (Godot coordinates); its scale is folded into the shape's size,
/// because Godot bodies and shapes don't take scale.
/// </summary>
public static class ColliderShapes
{
    public static CollisionShape3D? Make(ColliderData c, Transform3D place)
    {
        var scale = place.Basis.Scale.Abs();
        var basis = place.Basis.Orthonormalized();
        if (basis.Determinant() < 0)
            basis.Column0 = -basis.Column0;
        var center = place * World.UnityConvert.Position(c.Center);

        switch (c.Shape)
        {
            case ColliderShape.Box:
                var size = new Vector3(c.Size.X * scale.X, c.Size.Y * scale.Y, c.Size.Z * scale.Z).Abs();
                return new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Transform = new Transform3D(basis, center) };
            case ColliderShape.Sphere:
                return new CollisionShape3D
                {
                    Shape = new SphereShape3D { Radius = c.Radius * Math.Max(scale.X, Math.Max(scale.Y, scale.Z)) },
                    Transform = new Transform3D(basis, center),
                };
            case ColliderShape.Capsule:
                // Godot capsules run along local Y; Unity's direction is 0 = X, 1 = Y, 2 = Z.
                Basis b;
                float along, across;
                switch (c.Direction)
                {
                    case 0:
                        b = new Basis(-basis.Column1, basis.Column0, basis.Column2);
                        along = scale.X;
                        across = Math.Max(scale.Y, scale.Z);
                        break;
                    case 2:
                        b = new Basis(basis.Column0, basis.Column2, -basis.Column1);
                        along = scale.Z;
                        across = Math.Max(scale.X, scale.Y);
                        break;
                    default:
                        b = basis;
                        along = scale.Y;
                        across = Math.Max(scale.X, scale.Z);
                        break;
                }
                var radius = c.Radius * across;
                var height = Math.Max(c.Height * along, radius * 2);
                return new CollisionShape3D { Shape = new CapsuleShape3D { Radius = radius, Height = height }, Transform = new Transform3D(b, center) };
            default:
                return null;
        }
    }
}
