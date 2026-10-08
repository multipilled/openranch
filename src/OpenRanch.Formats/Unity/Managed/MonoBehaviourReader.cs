using System.Numerics;
using System.Reflection.Metadata;

namespace OpenRanch.Formats.Unity.Managed;

/// <summary>Named values read from a script component or scriptable object, in stored order.</summary>
public sealed class SerializedObject
{
    public SerializedObject(string typeName) => TypeName = typeName;

    public string TypeName { get; }
    public List<KeyValuePair<string, object?>> Fields { get; } = new();

    public object? this[string name] => Fields.FirstOrDefault(f => f.Key == name).Value;

    public bool Has(string name) => Fields.Any(f => f.Key == name);
    public T Get<T>(string name) => (T)this[name]!;
    public SerializedObject? Object(string name) => this[name] as SerializedObject;
    public List<object?> List(string name) => this[name] as List<object?> ?? [];

    public override string ToString() => TypeName;
}

/// <summary>A script component or scriptable object, with its script and serialized fields.</summary>
public sealed record MonoBehaviourData(PPtr GameObject, bool Enabled, PPtr Script, string Name, string? ScriptClass, SerializedObject? Data, bool ReadCleanly);

/// <summary>Reads MonoBehaviour objects using layouts worked out from the game's own assemblies.</summary>
public sealed class MonoBehaviourReader
{
    private readonly AssetSet _assets;
    private readonly ManagedTypes _types;
    private readonly SerializedLayout _layouts;
    private readonly Dictionary<AssetRef, ManagedType?> _scriptTypes = new();

    public MonoBehaviourReader(AssetSet assets, ManagedTypes types)
    {
        _assets = assets;
        _types = types;
        _layouts = new SerializedLayout(types);
    }

    public sealed record ScriptInfo(string ClassName, string Namespace, string Assembly);

    public static ScriptInfo ReadMonoScript(EndianReader r)
    {
        r.ReadAlignedString(); // name
        r.ReadInt32(); // execution order
        r.Skip(16); // properties hash
        var className = r.ReadAlignedString();
        var ns = r.ReadAlignedString();
        var assembly = r.ReadAlignedString();
        return new ScriptInfo(className, ns, assembly);
    }

    public ManagedType? ScriptType(AssetRef scriptRef)
    {
        if (!_scriptTypes.TryGetValue(scriptRef, out var type))
        {
            var info = _assets.Read(scriptRef, ReadMonoScript);
            _scriptTypes[scriptRef] = type = _types.Find(info.Assembly, info.Namespace, info.ClassName);
        }
        return type;
    }

    /// <summary>Reads only the header (owner, enabled flag, script, name).</summary>
    public static (PPtr GameObject, bool Enabled, PPtr Script, string Name) ReadHeader(EndianReader r)
    {
        var go = PPtr.Read(r);
        var enabled = r.ReadBool();
        r.Align();
        var script = PPtr.Read(r);
        var name = r.ReadAlignedString();
        return (go, enabled, script, name);
    }

    public MonoBehaviourData Read(AssetRef behaviour)
    {
        var r = _assets.Reader(behaviour);
        var (go, enabled, script, name) = ReadHeader(r);
        var scriptRef = _assets.Resolve(behaviour.File, script);
        var type = scriptRef is null ? null : ScriptType(scriptRef.Value);
        if (type is null)
            return new MonoBehaviourData(go, enabled, script, name, null, null, false);

        var data = new SerializedObject(type.FullName);
        var clean = true;
        try
        {
            foreach (var field in _layouts.ForScript(type))
                data.Fields.Add(new(field.Name, ReadField(r, field, inArray: false)));
            clean = r.Position == r.Length;
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException)
        {
            clean = false;
        }
        return new MonoBehaviourData(go, enabled, script, name, type.FullName, data, clean);
    }

    private static object? ReadField(EndianReader r, FieldLayout field, bool inArray)
    {
        switch (field)
        {
            case PrimitiveLayout p:
                object value = p.Code switch
                {
                    PrimitiveTypeCode.Boolean => r.ReadBool(),
                    PrimitiveTypeCode.Byte => r.ReadByte(),
                    PrimitiveTypeCode.SByte => (sbyte)r.ReadByte(),
                    PrimitiveTypeCode.Char => (char)r.ReadUInt16(),
                    PrimitiveTypeCode.Int16 => r.ReadInt16(),
                    PrimitiveTypeCode.UInt16 => r.ReadUInt16(),
                    PrimitiveTypeCode.Int32 => r.ReadInt32(),
                    PrimitiveTypeCode.UInt32 => r.ReadUInt32(),
                    PrimitiveTypeCode.Int64 => r.ReadInt64(),
                    PrimitiveTypeCode.UInt64 => (ulong)r.ReadInt64(),
                    PrimitiveTypeCode.Single => r.ReadSingle(),
                    PrimitiveTypeCode.Double => BitConverter.Int64BitsToDouble(r.ReadInt64()),
                    _ => throw new InvalidDataException($"Unsupported primitive {p.Code}."),
                };
                if (!inArray)
                    r.Align();
                return value;
            case StringLayout:
                return r.ReadAlignedString();
            case ReferenceLayout:
                return PPtr.Read(r);
            case ArrayLayout a:
            {
                var count = r.ReadInt32();
                if (count < 0 || count > 10_000_000)
                    throw new InvalidDataException($"Bad array length {count}.");
                var list = new List<object?>(count);
                for (var i = 0; i < count; i++)
                    list.Add(ReadField(r, a.Element, inArray: true));
                r.Align();
                return list;
            }
            case GroupLayout g:
            {
                var obj = new SerializedObject(g.TypeName);
                foreach (var f in g.Fields)
                    obj.Fields.Add(new(f.Name, ReadField(r, f, inArray: false)));
                return obj;
            }
            case BuiltinLayout b:
                return ReadBuiltin(r, b.TypeName);
            default:
                throw new InvalidDataException($"Unknown layout {field}.");
        }
    }

    private static object ReadBuiltin(EndianReader r, string type)
    {
        switch (type)
        {
            case "UnityEngine.Vector2": return r.ReadVector2();
            case "UnityEngine.Vector3": return r.ReadVector3();
            case "UnityEngine.Vector4":
            case "UnityEngine.Color":
            case "UnityEngine.Rect": return r.ReadVector4();
            case "UnityEngine.Quaternion": return r.ReadQuaternion();
            case "UnityEngine.Bounds": return (r.ReadVector3(), r.ReadVector3());
            case "UnityEngine.Matrix4x4":
            {
                var m = new float[16];
                for (var i = 0; i < 16; i++)
                    m[i] = r.ReadSingle();
                return m;
            }
            case "UnityEngine.Vector2Int": return (r.ReadInt32(), r.ReadInt32());
            case "UnityEngine.Vector3Int": return (r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case "UnityEngine.RectInt": return (r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case "UnityEngine.BoundsInt": return (r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case "UnityEngine.LayerMask": return r.ReadUInt32();
            case "UnityEngine.Color32": return r.ReadUInt32();
            case "UnityEngine.RectOffset": return (r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            case "UnityEngine.AnimationCurve":
            {
                var keys = r.ReadArray(x =>
                {
                    var k = new float[7];
                    for (var i = 0; i < 7; i++)
                        k[i] = x.ReadSingle(); // time, value, in/out slope, weighted mode (as bits), in/out weight
                    return k;
                });
                r.ReadInt32(); // pre-infinity
                r.ReadInt32(); // post-infinity
                r.ReadInt32(); // rotation order
                return new Curve(keys.Select(k => (k[0], k[1], k[2], k[3])).ToList());
            }
            case "UnityEngine.Gradient":
            {
                var colors = new Vector4[8];
                for (var i = 0; i < 8; i++)
                    colors[i] = r.ReadVector4();
                var colorTimes = new ushort[8];
                for (var i = 0; i < 8; i++)
                    colorTimes[i] = r.ReadUInt16();
                var alphaTimes = new ushort[8];
                for (var i = 0; i < 8; i++)
                    alphaTimes[i] = r.ReadUInt16();
                r.ReadInt32(); // mode
                var colorKeys = r.ReadByte();
                var alphaKeys = r.ReadByte();
                r.Align();
                return new ColorGradient(colors, colorTimes, alphaTimes, colorKeys, alphaKeys);
            }
            case "UnityEngine.GUIStyle":
            {
                r.ReadAlignedString(); // name
                for (var s = 0; s < 8; s++)
                {
                    PPtr.Read(r); // background
                    r.ReadArray(PPtr.Read); // scaled backgrounds
                    r.ReadVector4(); // text colour
                }
                r.Skip(4 * 16); // border, margin, padding, overflow
                PPtr.Read(r); // font
                r.Skip(4 * 3); // font size, style, alignment
                r.ReadBool(); // word wrap
                r.ReadBool(); // rich text
                r.Align();
                r.Skip(4 * 2); // clipping, image position
                r.Skip(8 + 8); // content offset, fixed size
                r.ReadBool();
                r.ReadBool();
                r.Align();
                return "GUIStyle";
            }
            default:
                throw new InvalidDataException($"No reader for {type}.");
        }
    }
}

/// <summary>An animation curve's keys: time, value, incoming and outgoing slope.</summary>
public sealed record Curve(List<(float Time, float Value, float InSlope, float OutSlope)> Keys)
{
    /// <summary>Evaluates the curve with Hermite interpolation between keys.</summary>
    public float Evaluate(float t)
    {
        if (Keys.Count == 0)
            return 0;
        if (t <= Keys[0].Time)
            return Keys[0].Value;
        if (t >= Keys[^1].Time)
            return Keys[^1].Value;
        for (var i = 0; i < Keys.Count - 1; i++)
        {
            var a = Keys[i];
            var b = Keys[i + 1];
            if (t > b.Time)
                continue;
            var dt = b.Time - a.Time;
            var s = (t - a.Time) / dt;
            float s2 = s * s, s3 = s2 * s;
            return (2 * s3 - 3 * s2 + 1) * a.Value + (s3 - 2 * s2 + s) * dt * a.OutSlope
                   + (-2 * s3 + 3 * s2) * b.Value + (s3 - s2) * dt * b.InSlope;
        }
        return Keys[^1].Value;
    }
}

/// <summary>A colour gradient: up to 8 colour keys and 8 alpha keys, times scaled to 0-65535.</summary>
public sealed record ColorGradient(Vector4[] Colors, ushort[] ColorTimes, ushort[] AlphaTimes, int ColorKeyCount, int AlphaKeyCount);
