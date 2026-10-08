using System.Globalization;
using OpenRanch.Formats.Audio;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Saves;
using OpenRanch.Formats.Scene;
using OpenRanch.Formats.Text;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Importer;

public static class Program
{
    private const string Usage = """
        openranch-import: reads your own copy of Slime Rancher. It never changes game files or saves.

        Commands:
          inventory [--game DIR]           Count every asset in the game's data files by type
          saves [--dir DIR] [--verify]     List your saves; --verify re-writes each supported save in memory
                                           and checks the bytes match the original exactly
          zone [NAME] [--game DIR]         Read one area of the world (default zoneRANCH): what it draws and
                                           collides with, decoding every mesh and texture it uses
          texture NAME --png OUT [--game DIR]
                                           Write a preview PNG of a texture (to a path outside the repo)
          scripts [--game DIR]             Read every script component with layouts worked out from the
                                           game's assemblies, and report how many read cleanly
          sounds [--export DIR] [--game DIR]
                                           Convert every sound clip to Ogg Vorbis or WAV in memory and check
                                           each file; --export writes them to a folder outside the repo.
                                           Nothing is played.
          text [--lang en] [--key KEY] [--bundle NAME] [--game DIR]
                                           Read the game's text in every language; --key shows one message

        The game is found through OPENRANCH_GAME_DIR or your Steam libraries.
        Saves default to %USERPROFILE%\AppData\LocalLow\Monomi Park\Slime Rancher.
        """;

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            return args.FirstOrDefault() switch
            {
                "inventory" => Inventory(args[1..]),
                "saves" => Saves(args[1..]),
                "zone" => Zone(args[1..]),
                "texture" => Texture(args[1..]),
                "scripts" => Scripts(args[1..]),
                "sounds" => Sounds(args[1..]),
                "text" => Text(args[1..]),
                _ => PrintUsage(),
            };
        }
        catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or SaveFormatException)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }

    private static int PrintUsage()
    {
        Console.WriteLine(Usage);
        return 2;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static GameInstall RequireInstall(string[] args)
    {
        var dir = Option(args, "--game");
        var install = dir is null ? GameInstall.Find() : GameInstall.Open(dir);
        return install ?? throw new IOException(
            $"Slime Rancher wasn't found. Pass --game <folder> or set {GameInstall.GameDirVariable}.");
    }

    private static int Inventory(string[] args)
    {
        var install = RequireInstall(args);
        var census = AssetCensus.Take(install);
        Console.WriteLine($"Game: {install.RootDirectory}");
        Console.WriteLine($"Unity {census.Files[0].UnityVersion}, serialized format {census.Files[0].FormatVersion}");
        Console.WriteLine();
        Console.WriteLine($"{"File",-28} {"Objects",10} {"Size (MB)",10}");
        foreach (var file in census.Files)
            Console.WriteLine($"{Path.GetFileName(file.Path),-28} {file.Objects.Count,10:N0} {new FileInfo(file.Path).Length / 1048576.0,10:N1}");
        Console.WriteLine();
        Console.WriteLine($"{census.Files.Count} files, {census.TotalObjects:N0} objects");
        Console.WriteLine();
        Console.WriteLine($"{"Type",-26} {"Count",10}");
        foreach (var (classId, count) in census.CountsByClass.OrderByDescending(p => p.Value).ThenBy(p => p.Key))
            Console.WriteLine($"{UnityClassId.NameOf(classId),-26} {count,10:N0}");
        return 0;
    }

    private static int Saves(string[] args)
    {
        var dir = Option(args, "--dir") ?? SaveFile.DefaultSaveDirectory();
        var verify = args.Contains("--verify");
        if (!Directory.Exists(dir))
            throw new IOException($"No save folder at {dir}.");

        var files = Directory.GetFiles(dir, "*.sav").Order(StringComparer.Ordinal).ToList();
        int supported = 0, matched = 0;
        foreach (var path in files)
        {
            // Saves are read into memory once and never opened for writing.
            var original = File.ReadAllBytes(path);
            var name = Path.GetFileName(path);
            using var stream = new MemoryStream(original, writable: false);
            var (tag, version) = SaveFile.PeekHeader(stream);
            if (tag != SaveFile.GameTag || !SaveFile.SupportedVersions.Contains(version))
            {
                Console.WriteLine($"{name}: {tag} v{version}, not supported yet");
                continue;
            }

            supported++;
            var game = SaveFile.Read(stream);
            var info = SaveFile.Describe(game);
            var line = $"{name}: v{version} \"{info.DisplayName}\" game {info.GameVersion}, day {info.Day}, " +
                       $"{info.Currency:N0} newbucks, {info.ActorCount} actors, {info.PlotCount} plots";
            if (verify)
            {
                var rewritten = SaveFile.ToBytes(game);
                var same = rewritten.AsSpan().SequenceEqual(original);
                if (same)
                    matched++;
                line += same ? ", re-writes byte for byte" : $", MISMATCH {Mismatch(original, rewritten)}";
            }
            Console.WriteLine(line);
        }

        Console.WriteLine();
        Console.WriteLine($"{files.Count} saves, {supported} in a supported format" +
                          (verify ? $", {matched} of {supported} re-write byte for byte" : ""));
        return verify && matched != supported ? 1 : 0;
    }

    private static int Zone(string[] args)
    {
        var install = RequireInstall(args);
        var name = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && a != Option(args, "--game")) ?? "zoneRANCH";
        using var assets = new AssetSet(install);
        var scene = assets.File("level3") ?? throw new IOException("The world scene (level3) is missing.");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var zone = ZoneExtractor.Extract(assets, scene, name);
        var s = zone.Stats;
        Console.WriteLine($"{name}: {s.Nodes:N0} objects ({s.SkippedInactive:N0} inactive), {s.Renderers:N0} renderers " +
                          $"({zone.Renderers.Count(r => r.StaticBatched):N0} static-batched, {s.SkippedLowerLods:N0} lower-LOD left out), " +
                          $"{s.Colliders:N0} solid colliders, {s.Triggers:N0} triggers, read in {clock.ElapsedMilliseconds} ms");

        clock.Restart();
        var meshes = zone.Renderers.Select(r => r.Mesh).Concat(zone.Colliders.Where(c => c.Mesh is not null).Select(c => c.Mesh!.Value))
            .Distinct().ToList();
        long vertices = 0, triangles = 0;
        foreach (var m in meshes)
        {
            var decoded = assets.DecodeMesh(assets.Read(m, MeshData.Read));
            vertices += decoded.VertexCount;
            triangles += decoded.Indices.Length / 3;
        }
        Console.WriteLine($"{meshes.Count} meshes decoded: {vertices:N0} vertices, {triangles:N0} triangles, in {clock.ElapsedMilliseconds} ms");

        var materials = zone.Renderers.SelectMany(r => r.Materials).OfType<AssetRef>().Distinct().ToList();
        var textures = new HashSet<AssetRef>();
        foreach (var m in materials)
        {
            var mat = assets.Read(m, MaterialData.Read);
            foreach (var slot in mat.Textures.Where(t => !t.Texture.IsNull))
                if (assets.Resolve(m.File, slot.Texture) is { ClassId: UnityClassId.Texture2D } tex)
                    textures.Add(tex);
        }
        var formats = textures.Select(t => assets.Read(t, Texture2DData.Read))
            .GroupBy(t => t.Format).OrderByDescending(g => g.Count())
            .Select(g => $"{TextureFormats.NameOf(g.Key)} {g.Count()}");
        Console.WriteLine($"{materials.Count} materials, {textures.Count} textures: {string.Join(", ", formats)}");

        clock.Restart();
        long texels = 0;
        var failures = 0;
        foreach (var t in textures)
        {
            var tex = assets.Read(t, Texture2DData.Read);
            try
            {
                var (_, data, mips) = TextureFormats.GpuData(tex, assets.TextureBytes(tex));
                texels += (long)tex.Width * tex.Height;
                if (data.Length == 0 || mips < 1)
                    failures++;
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException)
            {
                failures++;
                Console.WriteLine($"  {tex.Name}: {e.Message}");
            }
        }
        Console.WriteLine($"{textures.Count - failures} of {textures.Count} textures ready for the GPU ({texels / 1e6:N1} M texels) in {clock.ElapsedMilliseconds} ms");
        return failures == 0 ? 0 : 1;
    }

    private static int Texture(string[] args)
    {
        var install = RequireInstall(args);
        var name = args.FirstOrDefault() ?? throw new IOException("Name a texture.");
        var output = Option(args, "--png") ?? throw new IOException("Pass --png <file>.");
        using var assets = new AssetSet(install);
        foreach (var path in AssetCensus.SerializedFilePaths(install.DataDirectory))
        {
            var file = assets.File(Path.GetFileName(path))!;
            foreach (var info in file.Objects.Where(o => o.ClassId == UnityClassId.Texture2D))
            {
                var asset = new AssetRef(file, info);
                if (file.TryReadName(info) != name)
                    continue;
                var tex = assets.Read(asset, Texture2DData.Read);
                var (format, data, _) = TextureFormats.GpuData(tex, assets.TextureBytes(tex));
                BlockDecoder.WritePng(output, BlockDecoder.ToRgba(format, data, tex.Width, tex.Height), tex.Width, tex.Height);
                Console.WriteLine($"{name}: {tex.Width}x{tex.Height} {TextureFormats.NameOf(tex.Format)} from {Path.GetFileName(path)} -> {output}");
                return 0;
            }
        }
        Console.Error.WriteLine($"No texture named {name}.");
        return 1;
    }

    private static int Scripts(string[] args)
    {
        var install = RequireInstall(args);
        using var assets = new AssetSet(install);
        using var types = new OpenRanch.Formats.Unity.Managed.ManagedTypes(install.ManagedDirectory);
        var reader = new OpenRanch.Formats.Unity.Managed.MonoBehaviourReader(assets, types);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var perScript = new Dictionary<string, (int Clean, int Broken)>();
        int total = 0, clean = 0, unknown = 0;
        foreach (var path in AssetCensus.SerializedFilePaths(install.DataDirectory))
        {
            var file = assets.File(Path.GetFileName(path))!;
            foreach (var info in file.Objects.Where(o => o.ClassId == UnityClassId.MonoBehaviour))
            {
                total++;
                var mb = reader.Read(new AssetRef(file, info));
                if (mb.ScriptClass is null)
                {
                    unknown++;
                    continue;
                }
                var (c, b) = perScript.GetValueOrDefault(mb.ScriptClass);
                perScript[mb.ScriptClass] = mb.ReadCleanly ? (c + 1, b) : (c, b + 1);
                if (mb.ReadCleanly)
                    clean++;
            }
        }
        Console.WriteLine($"{total:N0} script components, {clean:N0} read cleanly ({100.0 * clean / total:F1}%), " +
                          $"{unknown:N0} with no script found, {perScript.Count} script types, in {clock.ElapsedMilliseconds} ms");
        if (Option(args, "--dump") is { } dumpClass)
        {
            var shown = 0;
            foreach (var path in AssetCensus.SerializedFilePaths(install.DataDirectory))
            {
                var file = assets.File(Path.GetFileName(path))!;
                foreach (var info in file.Objects.Where(o => o.ClassId == UnityClassId.MonoBehaviour))
                {
                    var mb = reader.Read(new AssetRef(file, info));
                    if (mb.ScriptClass != dumpClass || shown++ >= 3)
                        continue;
                    Console.WriteLine($"--- {dumpClass} '{mb.Name}' in {Path.GetFileName(path)}");
                    foreach (var (key, value) in mb.Data!.Fields)
                        Console.WriteLine($"  {key} = {Describe(value)}");
                }
            }
        }

        var broken = perScript.Where(p => p.Value.Broken > 0).OrderByDescending(p => p.Value.Broken).ToList();
        Console.WriteLine($"{broken.Count} script types with objects that don't read cleanly:");
        foreach (var (name, (c, b)) in broken.Take(25))
            Console.WriteLine($"  {name}: {b} broken, {c} clean");
        return 0;
    }

    private static int Sounds(string[] args)
    {
        var install = RequireInstall(args);
        var export = Option(args, "--export");
        if (export is not null)
        {
            export = Path.GetFullPath(export);
            if (InsideGitCheckout(export))
                throw new IOException($"{export} is inside a git checkout; export sounds to a folder outside the repo.");
            Directory.CreateDirectory(export);
        }

        using var assets = new AssetSet(install);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var sounds = GameSounds.Load(assets, install);
        var clips = sounds.Clips;
        Console.WriteLine($"{clips.Count} sound clips read in {clock.ElapsedMilliseconds} ms");
        foreach (var g in clips.GroupBy(c => Path.GetFileName(c.Clip.Resource.Path)).OrderBy(g => g.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {g.Key}: {g.Count()} clips, {g.Sum(c => (long)c.Clip.Resource.Size) / 1048576.0:N1} MB");
        Console.WriteLine("  load types: " + string.Join(", ", clips.GroupBy(c => c.Clip.LoadType).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}")));
        Console.WriteLine("  imported as: " + string.Join(", ", clips.GroupBy(c => c.Clip.CompressionFormat).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}")));

        // One clip at a time keeps the machine responsive; nothing is played.
        clock.Restart();
        var failures = new List<string>();
        var codecs = new Dictionary<string, int>();
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        long bytesOut = 0;
        double seconds = 0;
        foreach (var clip in clips)
        {
            try
            {
                var sound = sounds.Decode(clip);
                var info = SoundFileInfo.Read(sound.Format, sound.Data);
                if (info.Channels != clip.Clip.Channels || info.Frequency != clip.Clip.Frequency)
                    throw new InvalidDataException($"file is {info.Channels} ch {info.Frequency} Hz, clip says {clip.Clip.Channels} ch {clip.Clip.Frequency} Hz");
                if (Math.Abs(info.Seconds - clip.Clip.Length) > 0.05 + clip.Clip.Length * 0.01)
                    throw new InvalidDataException($"file lasts {info.Seconds:F3} s, clip says {clip.Clip.Length:F3} s");
                var key = $"{sound.Codec} -> {sound.Extension}";
                codecs[key] = codecs.GetValueOrDefault(key) + 1;
                bytesOut += sound.Data.Length;
                seconds += info.Seconds;
                if (export is not null)
                {
                    var stem = SafeFileName(clip.Name);
                    var n = names[stem] = names.GetValueOrDefault(stem) + 1;
                    var file = n == 1 ? $"{stem}.{sound.Extension}" : $"{stem}~{n}.{sound.Extension}";
                    File.WriteAllBytes(Path.Combine(export, file), sound.Data);
                }
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException or IOException)
            {
                failures.Add($"  {clip.Name} ({Path.GetFileName(clip.Asset.File.Path)} #{clip.Asset.PathId}): {e.Message}");
            }
        }
        var ok = clips.Count - failures.Count;
        Console.WriteLine($"{ok} of {clips.Count} clips converted and checked ({100.0 * ok / Math.Max(1, clips.Count):F1}%) " +
                          $"in {clock.ElapsedMilliseconds} ms: {seconds / 60:N1} minutes of sound, {bytesOut / 1048576.0:N1} MB");
        Console.WriteLine("  " + string.Join(", ", codecs.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")));
        foreach (var f in failures.Take(25))
            Console.WriteLine(f);
        if (export is not null)
            Console.WriteLine($"Wrote {ok} files to {export}");
        return failures.Count == 0 ? 0 : 1;
    }

    private static bool InsideGitCheckout(string directory)
    {
        for (var dir = new DirectoryInfo(directory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
                return true;
        return false;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return safe.Length == 0 ? "unnamed" : safe;
    }

    private static int Text(string[] args)
    {
        var install = RequireInstall(args);
        var lang = Option(args, "--lang") ?? GameText.DefaultLanguage;
        using var assets = new AssetSet(install);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var text = GameText.Load(assets);
        Console.WriteLine($"{text.Bundles.Count} bundles in {text.Languages.Count} languages read in {clock.ElapsedMilliseconds} ms");
        foreach (var language in text.Languages)
        {
            var bundles = text.Bundles.Where(b => b.Language == language).ToList();
            var malformed = bundles.Sum(b => b.MalformedLines.Count);
            Console.WriteLine($"  {language}: {bundles.Count} bundles, {bundles.Sum(b => b.Entries.Count):N0} messages" +
                              (malformed > 0 ? $", {malformed} lines that aren't key = value" : ""));
        }

        if (Option(args, "--key") is { } key)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (Option(args, "--bundle") is { } bundle)
                Console.WriteLine($"{lang} {bundle}: {key} = {text.Get(lang, bundle, key) ?? "(missing)"}");
            else
            {
                var found = text.Find(lang, key).ToList();
                if (found.Count == 0)
                    Console.WriteLine($"{lang}: no bundle has {key}");
                foreach (var (b, value) in found)
                    Console.WriteLine($"{lang} {b.Name}: {key} = {value}");
            }
        }
        else
            Console.WriteLine($"{lang} bundles: {string.Join(", ", text.BundleNames(lang))}");
        return 0;
    }

    private static string Describe(object? value, int depth = 0) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        PPtr p => p.IsNull ? "(none)" : $"ref({p.FileId}:{p.PathId})",
        List<object?> list => depth > 1 ? $"[{list.Count} items]" : $"[{string.Join(", ", list.Take(6).Select(v => Describe(v, depth + 1)))}{(list.Count > 6 ? $", ... {list.Count} total" : "")}]",
        OpenRanch.Formats.Unity.Managed.SerializedObject o => depth > 1 ? $"{{{o.TypeName}}}" : "{" + string.Join(", ", o.Fields.Take(8).Select(f => $"{f.Key}={Describe(f.Value, depth + 1)}")) + "}",
        float f => f.ToString("0.###"),
        _ => value.ToString() ?? "",
    };

    private static string Mismatch(byte[] a, byte[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
            if (a[i] != b[i])
                return $"at byte {i}";
        return $"in length ({a.Length} vs {b.Length} bytes)";
    }
}
