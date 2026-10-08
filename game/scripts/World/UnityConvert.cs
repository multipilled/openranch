using Godot;
using N = System.Numerics;

namespace OpenRanch.Game.World;

/// <summary>
/// Unity is left-handed and Godot right-handed, both Y up. Mirroring Z converts between them:
/// positions and directions get z negated and transforms are conjugated by the mirror. Triangle
/// order stays as stored: both engines treat clockwise as the front, and the mirror keeps it so.
/// </summary>
public static class UnityConvert
{
    public static Vector3 Position(N.Vector3 v) => new(v.X, v.Y, -v.Z);

    /// <summary>Converts a Unity world matrix (row-vector convention) to a Godot transform.</summary>
    public static Transform3D Transform(N.Matrix4x4 m) => new(
        new Vector3(m.M11, m.M12, -m.M13),
        new Vector3(m.M21, m.M22, -m.M23),
        new Vector3(-m.M31, -m.M32, m.M33),
        new Vector3(m.M41, m.M42, -m.M43));

    /// <summary>True when the transform mirrors geometry (negative scale on an odd number of axes).</summary>
    public static bool IsMirrored(N.Matrix4x4 m) => m.GetDeterminant() < 0;

    /// <summary>Unity colours are stored in sRGB; Godot colours given to source_color uniforms are too.</summary>
    public static Color Color(N.Vector4 c) => new(c.X, c.Y, c.Z, c.W);
}
