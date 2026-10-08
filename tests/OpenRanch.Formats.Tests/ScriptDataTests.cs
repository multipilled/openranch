using OpenRanch.Formats.Game;
using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Formats.Tests;

public class ScriptDataTests
{
    // Layouts are worked out from the game's assemblies; a wrong layout would leave bytes unread or
    // run past the end, so every object reading to its exact end is a strong check.
    [GameFact]
    public void Every_script_component_reads_to_its_exact_end()
    {
        var install = GameFactAttribute.Install!;
        using var assets = new AssetSet(install);
        using var types = new ManagedTypes(install.ManagedDirectory);
        var reader = new MonoBehaviourReader(assets, types);
        var total = 0;
        var broken = new List<string>();
        foreach (var path in AssetCensus.SerializedFilePaths(install.DataDirectory))
        {
            var file = assets.File(Path.GetFileName(path))!;
            foreach (var info in file.Objects.Where(o => o.ClassId == UnityClassId.MonoBehaviour))
            {
                total++;
                var mb = reader.Read(new AssetRef(file, info));
                if (!mb.ReadCleanly)
                    broken.Add($"{mb.ScriptClass ?? "?"} in {Path.GetFileName(path)}");
            }
        }
        Assert.Equal(37299, total);
        Assert.Empty(broken.Take(10));
    }

    [GameFact]
    public void Slime_definitions_carry_diets()
    {
        var install = GameFactAttribute.Install!;
        using var assets = new AssetSet(install);
        using var types = new ManagedTypes(install.ManagedDirectory);
        var reader = new MonoBehaviourReader(assets, types);
        var file = assets.File("resources.assets")!;
        var slimes = file.Objects.Where(o => o.ClassId == UnityClassId.MonoBehaviour)
            .Select(o => reader.Read(new AssetRef(file, o)))
            .Where(m => m.ScriptClass == "SlimeDefinition")
            .ToList();

        Assert.True(slimes.Count > 20, $"only {slimes.Count} slime definitions");
        Assert.All(slimes, s => Assert.NotNull(s.Data!.Object("Diet")));
        Assert.Contains(slimes, s => s.Data!.Get<bool>("IsLargo"));
    }
}
