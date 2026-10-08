namespace OpenRanch.Formats.Saves;

/// <summary>Reads and writes one kind of value in a save file. Everything is little-endian.</summary>
public abstract class SaveCodec
{
    public abstract object? Read(BinaryReader reader);
    public abstract void Write(BinaryWriter writer, object? value);

    /// <summary>Short description used in dumps and error messages.</summary>
    public abstract string Describe();

    public static readonly SaveCodec I32 = new Primitive("int", r => r.ReadInt32(), (w, v) => w.Write((int)v!));
    public static readonly SaveCodec U32 = new Primitive("uint", r => r.ReadUInt32(), (w, v) => w.Write((uint)v!));
    public static readonly SaveCodec I64 = new Primitive("long", r => r.ReadInt64(), (w, v) => w.Write((long)v!));
    public static readonly SaveCodec U64 = new Primitive("ulong", r => r.ReadUInt64(), (w, v) => w.Write((ulong)v!));
    public static readonly SaveCodec F32 = new Primitive("float", r => r.ReadSingle(), (w, v) => w.Write((float)v!));
    public static readonly SaveCodec F64 = new Primitive("double", r => r.ReadDouble(), (w, v) => w.Write((double)v!));
    public static readonly SaveCodec Bool = new Primitive("bool", r => r.ReadBoolean(), (w, v) => w.Write((bool)v!));
    public static readonly SaveCodec Str = new Primitive("string", r => r.ReadString(), (w, v) => w.Write((string)v!));

    public static readonly SaveCodec Stamp = new Primitive("timestamp",
        r => new SaveTimestamp(r.ReadInt64(), r.ReadDouble()),
        (w, v) =>
        {
            var t = (SaveTimestamp)v!;
            w.Write(t.Ticks);
            w.Write(t.OffsetMinutes);
        });

    /// <summary>An int32 holding a value of one of the game's enums; the name is only a label.</summary>
    public static SaveCodec EnumOf(string enumName) => new Primitive(enumName, r => r.ReadInt32(), (w, v) => w.Write((int)v!));

    /// <summary>A bool saying whether the value follows.</summary>
    public static SaveCodec Opt(SaveCodec inner) => new NullableCodec(inner);

    public static SaveCodec BlockOf(SaveSchema schema) => new BlockCodec(schema);

    /// <summary>A bool saying whether the block follows.</summary>
    public static SaveCodec OptBlock(SaveSchema schema) => new NullableCodec(new BlockCodec(schema));

    /// <summary>An int32 count, then the items.</summary>
    public static SaveCodec ListOf(SaveCodec item) => new ListCodec(item);

    /// <summary>An int32 count, then each key and value followed by an element marker.</summary>
    public static SaveCodec MapOf(SaveCodec key, SaveCodec value) => new MapCodec(key, value, separated: true);

    /// <summary>An int32 count, then each key and value with no marker between pairs.</summary>
    public static SaveCodec PairsOf(SaveCodec key, SaveCodec value) => new MapCodec(key, value, separated: false);

    /// <summary>A fixed run of unframed values, stored as a <see cref="SaveRecord"/>.</summary>
    public static SaveCodec RecordOf(params (string Name, SaveCodec Codec)[] fields) => new RecordCodec(fields);

    private sealed class Primitive(string name, Func<BinaryReader, object> read, Action<BinaryWriter, object?> write) : SaveCodec
    {
        public override object? Read(BinaryReader reader) => read(reader);
        public override void Write(BinaryWriter writer, object? value) => write(writer, value);
        public override string Describe() => name;
    }

    private sealed class NullableCodec(SaveCodec inner) : SaveCodec
    {
        public override object? Read(BinaryReader reader) => reader.ReadBoolean() ? inner.Read(reader) : null;

        public override void Write(BinaryWriter writer, object? value)
        {
            writer.Write(value is not null);
            if (value is not null)
                inner.Write(writer, value);
        }

        public override string Describe() => inner.Describe() + "?";
    }

    private sealed class BlockCodec(SaveSchema schema) : SaveCodec
    {
        public override object? Read(BinaryReader reader) => schema.Read(reader);
        public override void Write(BinaryWriter writer, object? value) => schema.Write(writer, (SaveBlock)value!);
        public override string Describe() => $"{schema.Tag} v{schema.Version}";
    }

    private sealed class ListCodec(SaveCodec item) : SaveCodec
    {
        public override object? Read(BinaryReader reader)
        {
            var count = SaveSchema.ReadCount(reader);
            var list = new List<object?>(count);
            for (var i = 0; i < count; i++)
                list.Add(item.Read(reader));
            return list;
        }

        public override void Write(BinaryWriter writer, object? value)
        {
            var list = (List<object?>)value!;
            writer.Write(list.Count);
            foreach (var v in list)
                item.Write(writer, v);
        }

        public override string Describe() => $"list of {item.Describe()}";
    }

    private sealed class MapCodec(SaveCodec key, SaveCodec value, bool separated) : SaveCodec
    {
        public override object? Read(BinaryReader reader)
        {
            var count = SaveSchema.ReadCount(reader);
            var map = new SaveMap();
            for (var i = 0; i < count; i++)
            {
                var k = key.Read(reader)!;
                var v = value.Read(reader);
                if (separated)
                    SaveSchema.Expect(reader, SaveSchema.ElementMarker, "element marker");
                map.Add(new(k, v));
            }
            return map;
        }

        public override void Write(BinaryWriter writer, object? v)
        {
            var map = (SaveMap)v!;
            writer.Write(map.Count);
            foreach (var pair in map)
            {
                key.Write(writer, pair.Key);
                value.Write(writer, pair.Value);
                if (separated)
                    writer.Write(SaveSchema.ElementMarker);
            }
        }

        public override string Describe() => $"map of {key.Describe()} to {value.Describe()}";
    }

    private sealed class RecordCodec((string Name, SaveCodec Codec)[] fields) : SaveCodec
    {
        public override object? Read(BinaryReader reader)
        {
            var record = new SaveRecord();
            foreach (var (name, codec) in fields)
                record.Fields.Add(new(name, codec.Read(reader)));
            return record;
        }

        public override void Write(BinaryWriter writer, object? value)
        {
            var record = (SaveRecord)value!;
            foreach (var (name, codec) in fields)
                codec.Write(writer, record[name]);
        }

        public override string Describe() => "record";
    }
}
