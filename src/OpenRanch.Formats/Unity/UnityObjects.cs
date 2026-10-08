using System.Numerics;

namespace OpenRanch.Formats.Unity;

/// <summary>A reference to an object: a file index (0 = same file, n = n-th external) and a path id.</summary>
public readonly record struct PPtr(int FileId, long PathId)
{
    public bool IsNull => PathId == 0;

    public static PPtr Read(EndianReader r) => new(r.ReadInt32(), r.ReadInt64());
}

/// <summary>
/// Readers for the Unity 2019.4 object layouts openranch needs. Each layout was checked against
/// every object of its class in Slime Rancher's data, consuming exactly the stored bytes.
/// </summary>
public static class UnityReaders
{
    public static Vector3 ReadVector3(this EndianReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    public static Vector2 ReadVector2(this EndianReader r) => new(r.ReadSingle(), r.ReadSingle());
    public static Vector4 ReadVector4(this EndianReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    public static Quaternion ReadQuaternion(this EndianReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    public static List<T> ReadArray<T>(this EndianReader r, Func<EndianReader, T> item)
    {
        var count = r.ReadInt32();
        if (count < 0 || count > 50_000_000)
            throw new InvalidDataException($"Bad array length {count} at {r.Position - 4}.");
        var list = new List<T>(count);
        for (var i = 0; i < count; i++)
            list.Add(item(r));
        return list;
    }

    public static byte[] ReadByteArray(this EndianReader r)
    {
        var count = r.ReadInt32();
        var bytes = r.ReadBytes(count);
        r.Align();
        return bytes;
    }
}

public sealed record GameObjectData(List<PPtr> Components, uint Layer, string Name, ushort Tag, bool IsActive)
{
    public static GameObjectData Read(EndianReader r)
    {
        var components = r.ReadArray(PPtr.Read);
        var layer = r.ReadUInt32();
        var name = r.ReadAlignedString();
        var tag = r.ReadUInt16();
        var active = r.ReadBool();
        return new GameObjectData(components, layer, name, tag, active);
    }
}

public sealed record TransformData(PPtr GameObject, Quaternion LocalRotation, Vector3 LocalPosition, Vector3 LocalScale,
    List<PPtr> Children, PPtr Father)
{
    public static TransformData Read(EndianReader r) => new(
        PPtr.Read(r), r.ReadQuaternion(), r.ReadVector3(), r.ReadVector3(), r.ReadArray(PPtr.Read), PPtr.Read(r));

    public Matrix4x4 LocalMatrix =>
        Matrix4x4.CreateScale(LocalScale) * Matrix4x4.CreateFromQuaternion(LocalRotation) * Matrix4x4.CreateTranslation(LocalPosition);
}

public sealed record MeshFilterData(PPtr GameObject, PPtr Mesh)
{
    public static MeshFilterData Read(EndianReader r) => new(PPtr.Read(r), PPtr.Read(r));
}

/// <summary>A MeshRenderer or the renderer part of a SkinnedMeshRenderer.</summary>
public sealed record RendererData(PPtr GameObject, bool Enabled, bool CastShadows, ushort LightmapIndex,
    List<PPtr> Materials, ushort StaticBatchFirstSubMesh, ushort StaticBatchSubMeshCount)
{
    /// <summary>True when the renderer draws a slice of a combined static-batch mesh, already in world space.</summary>
    public bool IsStaticBatched => StaticBatchSubMeshCount > 0;

    public static RendererData Read(EndianReader r)
    {
        var go = PPtr.Read(r);
        var enabled = r.ReadBool();
        var castShadows = r.ReadByte() != 0;
        r.Skip(6); // receive shadows, occludee, motion vectors, probe usage x2, ray tracing mode
        r.ReadUInt32(); // rendering layer mask
        r.ReadInt32(); // renderer priority
        var lightmapIndex = r.ReadUInt16();
        r.ReadUInt16(); // dynamic lightmap index
        r.Skip(32); // lightmap tiling offsets
        var materials = r.ReadArray(PPtr.Read);
        var first = r.ReadUInt16();
        var count = r.ReadUInt16();
        return new RendererData(go, enabled, castShadows, lightmapIndex, materials, first, count);
    }
}

public sealed record LodLevel(float ScreenRelativeHeight, List<PPtr> Renderers);

public sealed record LodGroupData(PPtr GameObject, Vector3 LocalReferencePoint, float Size, List<LodLevel> Lods, bool Enabled)
{
    public static LodGroupData Read(EndianReader r)
    {
        var go = PPtr.Read(r);
        var point = r.ReadVector3();
        var size = r.ReadSingle();
        r.ReadInt32(); // fade mode
        r.ReadBool(); // animate cross-fading
        r.ReadBool(); // last LOD is billboard
        r.Align();
        var lods = r.ReadArray(x =>
        {
            var height = x.ReadSingle();
            x.ReadSingle(); // fade transition width
            return new LodLevel(height, x.ReadArray(PPtr.Read));
        });
        var enabled = r.ReadBool();
        return new LodGroupData(go, point, size, lods, enabled);
    }
}

public enum ColliderShape { Box, Sphere, Capsule, Mesh }

/// <summary>Any of the four collider classes. Unused fields stay default.</summary>
public sealed record ColliderData(PPtr GameObject, ColliderShape Shape, bool IsTrigger, bool Enabled, Vector3 Center,
    Vector3 Size, float Radius, float Height, int Direction, bool Convex, PPtr Mesh)
{
    public static ColliderData Read(EndianReader r, int classId)
    {
        var go = PPtr.Read(r);
        PPtr.Read(r); // physic material
        var trigger = r.ReadBool();
        var enabled = r.ReadBool();
        r.Align();
        switch (classId)
        {
            case UnityClassId.BoxCollider:
            {
                var size = r.ReadVector3();
                var center = r.ReadVector3();
                return new(go, ColliderShape.Box, trigger, enabled, center, size, 0, 0, 0, false, default);
            }
            case UnityClassId.SphereCollider:
            {
                var radius = r.ReadSingle();
                var center = r.ReadVector3();
                return new(go, ColliderShape.Sphere, trigger, enabled, center, default, radius, 0, 0, false, default);
            }
            case UnityClassId.CapsuleCollider:
            {
                var radius = r.ReadSingle();
                var height = r.ReadSingle();
                var direction = r.ReadInt32();
                var center = r.ReadVector3();
                return new(go, ColliderShape.Capsule, trigger, enabled, center, default, radius, height, direction, false, default);
            }
            case UnityClassId.MeshCollider:
            {
                var convex = r.ReadBool();
                r.Align();
                r.ReadInt32(); // cooking options
                var mesh = PPtr.Read(r);
                return new(go, ColliderShape.Mesh, trigger, enabled, default, default, 0, 0, 0, convex, mesh);
            }
            default:
                throw new ArgumentException($"Class {classId} is not a collider.");
        }
    }
}

public sealed record TextureSlot(string Name, PPtr Texture, Vector2 Scale, Vector2 Offset);

public sealed record MaterialData(string Name, PPtr Shader, string Keywords, int CustomRenderQueue,
    Dictionary<string, string> Tags, List<TextureSlot> Textures, Dictionary<string, float> Floats, Dictionary<string, Vector4> Colors)
{
    public static MaterialData Read(EndianReader r)
    {
        var name = r.ReadAlignedString();
        var shader = PPtr.Read(r);
        var keywords = r.ReadAlignedString();
        r.ReadUInt32(); // lightmap flags
        r.ReadBool(); // instancing variants
        r.ReadBool(); // double-sided GI
        r.Align();
        var queue = r.ReadInt32();
        var tags = new Dictionary<string, string>();
        foreach (var (k, v) in r.ReadArray(x => (x.ReadAlignedString(), x.ReadAlignedString())))
            tags[k] = v;
        r.ReadArray(x => x.ReadAlignedString()); // disabled shader passes
        var textures = r.ReadArray(x => new TextureSlot(x.ReadAlignedString(), PPtr.Read(x), x.ReadVector2(), x.ReadVector2()));
        var floats = new Dictionary<string, float>();
        foreach (var (k, v) in r.ReadArray(x => (x.ReadAlignedString(), x.ReadSingle())))
            floats[k] = v;
        var colors = new Dictionary<string, Vector4>();
        foreach (var (k, v) in r.ReadArray(x => (x.ReadAlignedString(), x.ReadVector4())))
            colors[k] = v;
        return new MaterialData(name, shader, keywords, queue, tags, textures, floats, colors);
    }

    public TextureSlot? Texture(string slot) => Textures.FirstOrDefault(t => t.Name == slot && !t.Texture.IsNull);
    public float Float(string key, float fallback) => Floats.TryGetValue(key, out var v) ? v : fallback;
    public Vector4 Color(string key, Vector4 fallback) => Colors.TryGetValue(key, out var v) ? v : fallback;
}

/// <summary>Where an object's bulk data lives when it is kept outside the serialized file.</summary>
public readonly record struct StreamedData(ulong Offset, uint Size, string Path)
{
    public bool IsEmpty => Size == 0 || string.IsNullOrEmpty(Path);
}

public sealed record Texture2DData(string Name, int Width, int Height, int Format, int MipCount, int ImageCount,
    int FilterMode, int WrapU, int WrapV, int ColorSpace, byte[] ImageData, StreamedData Stream)
{
    public static Texture2DData Read(EndianReader r)
    {
        var name = r.ReadAlignedString();
        r.ReadInt32(); // forced fallback format
        r.Skip(4); // downscale fallback, alpha optional, padding
        var width = r.ReadInt32();
        var height = r.ReadInt32();
        r.ReadInt32(); // complete image size
        var format = r.ReadInt32();
        var mips = r.ReadInt32();
        r.Skip(4); // readable, ignore texture limit, preprocessed, streaming mipmaps
        r.ReadInt32(); // streaming mipmap priority
        var imageCount = r.ReadInt32();
        r.ReadInt32(); // dimension
        var filter = r.ReadInt32();
        r.ReadInt32(); // anisotropy
        r.ReadSingle(); // mip bias
        var wrapU = r.ReadInt32();
        var wrapV = r.ReadInt32();
        r.ReadInt32(); // wrap W
        r.ReadInt32(); // lightmap format
        var colorSpace = r.ReadInt32();
        var data = r.ReadByteArray();
        var stream = new StreamedData(r.ReadUInt32(), r.ReadUInt32(), r.ReadAlignedString());
        return new Texture2DData(name, width, height, format, mips, imageCount, filter, wrapU, wrapV, colorSpace, data, stream);
    }
}
