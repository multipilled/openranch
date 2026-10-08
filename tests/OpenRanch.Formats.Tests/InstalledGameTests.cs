using OpenRanch.Formats.Game;
using OpenRanch.Formats.Saves;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Formats.Tests;

public class InstalledGameTests
{
    // Counts from the Steam release of Slime Rancher 1.4.x, as listed in the rewrite plan.
    [GameFact]
    public void Census_matches_the_known_release()
    {
        var census = AssetCensus.Take(GameFactAttribute.Install!);

        Assert.All(census.Files, f => Assert.Equal("2019.4.29f1", f.UnityVersion));
        Assert.All(census.Files, f => Assert.False(f.HasTypeTrees));
        Assert.Equal(2184, census.Count(UnityClassId.Mesh));
        Assert.Equal(2145, census.Count(UnityClassId.Material));
        Assert.Equal(1930, census.Count(UnityClassId.Texture2D));
        Assert.Equal(953, census.Count(UnityClassId.AudioClip));
        Assert.Equal(310, census.Count(UnityClassId.Shader));
        Assert.Equal(126, census.Count(UnityClassId.AnimationClip));
        Assert.Equal(842, census.Count(UnityClassId.SkinnedMeshRenderer));
        Assert.Equal(904, census.Count(UnityClassId.Sprite));
        Assert.Equal(2847, census.Count(UnityClassId.ParticleSystem));
        Assert.Equal(5811, census.Count(UnityClassId.LodGroup));
        Assert.Equal(77194, census.Count(UnityClassId.MeshFilter));
        Assert.Equal(20424, census.Count(UnityClassId.MeshCollider));
        Assert.Equal(37299, census.Count(UnityClassId.MonoBehaviour));
        // The world is meshes apart from one small terrain patch in the Dry Reef.
        Assert.Equal(1, census.Count(UnityClassId.Terrain));
        Assert.Equal(1, census.Count(UnityClassId.TerrainData));
    }

    [GameFact]
    public void Named_assets_have_readable_names()
    {
        var path = Path.Combine(GameFactAttribute.Install!.DataDirectory, "resources.assets");
        var file = SerializedFile.Open(path);
        using var stream = File.OpenRead(path);
        var names = file.Objects
            .Where(o => o.ClassId == UnityClassId.TextAsset)
            .Select(o => file.TryReadName(o, stream))
            .ToList();

        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.False(string.IsNullOrEmpty(n)));
    }

    [SaveFact]
    public void Every_supported_save_rewrites_byte_for_byte()
    {
        foreach (var path in SaveFactAttribute.SupportedSaves)
        {
            var original = File.ReadAllBytes(path);
            var game = SaveFile.Read(new MemoryStream(original, writable: false));
            var rewritten = SaveFile.ToBytes(game);
            Assert.True(rewritten.AsSpan().SequenceEqual(original), $"{Path.GetFileName(path)} changed when re-written");
        }
    }

    [SaveFact]
    public void Save_summary_agrees_with_player_data()
    {
        foreach (var path in SaveFactAttribute.SupportedSaves)
        {
            var game = SaveFile.Read(new MemoryStream(File.ReadAllBytes(path), writable: false));
            var info = SaveFile.Describe(game);
            Assert.Equal(game.Block("summary").Get<int>("currency"), info.Currency);
            Assert.Equal(game.Block("player").Get<string>("version"), info.GameVersion);
            Assert.False(string.IsNullOrEmpty(info.DisplayName));
        }
    }
}
