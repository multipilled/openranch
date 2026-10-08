using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Game.World;

/// <summary>
/// Turns the game's meshes, textures and materials into Godot resources, reading them straight from
/// the player's install. Everything is cached for the life of the loader.
/// </summary>
public sealed class WorldAssets
{
    private static readonly Shader WorldShader = GD.Load<Shader>("res://shaders/sr_world.gdshader");
    private static readonly Shader MaskShader = GD.Load<Shader>("res://shaders/sr_mask.gdshader");
    private static readonly Shader RampShader = GD.Load<Shader>("res://shaders/sr_ramp.gdshader");

    private readonly AssetSet _assets;
    private readonly Dictionary<AssetRef, DecodedMesh> _meshes = new();
    private readonly Dictionary<AssetRef, Texture2D?> _textures = new();
    private readonly Dictionary<AssetRef, Material> _materials = new();
    private readonly StandardMaterial3D _fallback = new() { AlbedoColor = new Color(0.6f, 0.6f, 0.6f), Roughness = 0.9f };

    public WorldAssets(AssetSet assets) => _assets = assets;

    public int TextureCount => _textures.Count;
    public int MaterialCount => _materials.Count;

    public DecodedMesh Mesh(AssetRef mesh)
    {
        if (!_meshes.TryGetValue(mesh, out var decoded))
            _meshes[mesh] = decoded = _assets.DecodeMesh(_assets.Read(mesh, MeshData.Read));
        return decoded;
    }

    /// <summary>
    /// Builds a mesh with one surface per material. Each surface gathers the given sub-meshes, copying
    /// only the vertices they use. Unity and Godot both treat clockwise triangles as front faces, and
    /// mirroring Z already turns Unity's clockwise into Godot's clockwise, so the order is kept.
    /// <paramref name="mirrored"/> reverses it for multimesh instances whose transform mirrors them
    /// (a multimesh can't flip culling per instance).
    /// </summary>
    public ArrayMesh BuildMesh(AssetRef meshRef, IEnumerable<(int SubMesh, AssetRef? Material)> parts, bool mirrored)
    {
        var decoded = Mesh(meshRef);
        var mesh = new ArrayMesh();
        foreach (var group in parts.GroupBy(p => p.Material))
        {
            var arrays = SurfaceArrays(decoded, group.Select(p => p.SubMesh).Distinct(), mirrored);
            if (arrays is null)
                continue;
            mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, Material(group.Key));
        }
        return mesh;
    }

    /// <summary>Triangle faces of every sub-mesh in Godot coordinates, for collision.</summary>
    public Vector3[] Faces(AssetRef meshRef, Transform3D transform)
    {
        var d = Mesh(meshRef);
        var faces = new List<Vector3>();
        if (d.Positions is null)
            return [];
        Vector3 P(int i) => transform * new Vector3(d.Positions[i * 3], d.Positions[i * 3 + 1], -d.Positions[i * 3 + 2]);
        for (var s = 0; s < d.SubMeshes.Count; s++)
        {
            if (d.SubMeshes[s].Topology != 0)
                continue;
            var indices = d.SubMeshIndices(s, out var baseVertex);
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                faces.Add(P(indices[i] + (int)baseVertex));
                faces.Add(P(indices[i + 2] + (int)baseVertex));
                faces.Add(P(indices[i + 1] + (int)baseVertex));
            }
        }
        return faces.ToArray();
    }

    private static Godot.Collections.Array? SurfaceArrays(DecodedMesh d, IEnumerable<int> subMeshes, bool mirrored)
    {
        if (d.Positions is null)
            return null;
        var vertices = new List<Vector3>();
        var normals = d.Normals is null ? null : new List<Vector3>();
        var tangents = d.Tangents is null ? null : new List<float>();
        var colors = d.Colors is null || d.ColorDimension < 3 ? null : new List<Color>();
        var uv0 = d.Uv0 is null || d.Uv0Dimension < 2 ? null : new List<Vector2>();
        var uv1 = d.Uv1 is null || d.Uv1Dimension < 2 ? null : new List<Vector2>();
        var indices = new List<int>();

        foreach (var s in subMeshes)
        {
            if (s >= d.SubMeshes.Count || d.SubMeshes[s].Topology != 0)
                continue;
            var sub = d.SubMeshes[s];
            var tri = d.SubMeshIndices(s, out var baseVertex);
            var first = (int)(baseVertex + sub.FirstVertex);
            var count = (int)sub.VertexCount;
            if (count == 0 || tri.Length == 0)
                continue;
            var offset = vertices.Count;
            for (var v = first; v < first + count; v++)
            {
                vertices.Add(new Vector3(d.Positions[v * 3], d.Positions[v * 3 + 1], -d.Positions[v * 3 + 2]));
                normals?.Add(new Vector3(d.Normals![v * 3], d.Normals[v * 3 + 1], -d.Normals[v * 3 + 2]));
                if (tangents is not null)
                {
                    tangents.Add(d.Tangents![v * 4]);
                    tangents.Add(d.Tangents[v * 4 + 1]);
                    tangents.Add(-d.Tangents[v * 4 + 2]);
                    tangents.Add(-d.Tangents[v * 4 + 3]);
                }
                if (colors is not null)
                {
                    var n = d.ColorDimension;
                    colors.Add(new Color(d.Colors![v * n], d.Colors[v * n + 1], d.Colors[v * n + 2], n > 3 ? d.Colors[v * n + 3] : 1f));
                }
                uv0?.Add(new Vector2(d.Uv0![v * d.Uv0Dimension], d.Uv0[v * d.Uv0Dimension + 1]));
                uv1?.Add(new Vector2(d.Uv1![v * d.Uv1Dimension], d.Uv1[v * d.Uv1Dimension + 1]));
            }
            var shift = (int)baseVertex - first + offset;
            for (var i = 0; i + 2 < tri.Length; i += 3)
            {
                indices.Add(tri[i] + shift);
                if (mirrored)
                {
                    indices.Add(tri[i + 2] + shift);
                    indices.Add(tri[i + 1] + shift);
                }
                else
                {
                    indices.Add(tri[i + 1] + shift);
                    indices.Add(tri[i + 2] + shift);
                }
            }
        }
        if (indices.Count == 0)
            return null;

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);
        arrays[(int)Godot.Mesh.ArrayType.Vertex] = vertices.ToArray();
        if (normals is not null)
            arrays[(int)Godot.Mesh.ArrayType.Normal] = normals.ToArray();
        if (tangents is not null)
            arrays[(int)Godot.Mesh.ArrayType.Tangent] = tangents.ToArray();
        if (colors is not null)
            arrays[(int)Godot.Mesh.ArrayType.Color] = colors.ToArray();
        if (uv0 is not null)
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = uv0.ToArray();
        if (uv1 is not null)
            arrays[(int)Godot.Mesh.ArrayType.TexUV2] = uv1.ToArray();
        arrays[(int)Godot.Mesh.ArrayType.Index] = indices.ToArray();
        return arrays;
    }

    public Texture2D? Texture(AssetRef? textureRef)
    {
        if (textureRef is not { ClassId: UnityClassId.Texture2D } asset)
            return null;
        if (_textures.TryGetValue(asset, out var cached))
            return cached;

        Texture2D? result = null;
        try
        {
            var tex = _assets.Read(asset, Texture2DData.Read);
            var (format, data, mips) = TextureFormats.GpuData(tex, _assets.TextureBytes(tex));
            var image = MakeImage(tex.Width, tex.Height, format, data, mips);
            if (image is not null)
                result = ImageTexture.CreateFromImage(image);
        }
        catch (Exception e) when (e is System.IO.InvalidDataException or NotSupportedException)
        {
            GD.PushWarning($"Texture {asset.PathId} skipped: {e.Message}");
        }
        _textures[asset] = result;
        return result;
    }

    private static Image? MakeImage(int width, int height, int format, byte[] data, int mips)
    {
        Image.Format godotFormat;
        switch (format)
        {
            case TextureFormats.Dxt1: godotFormat = Image.Format.Dxt1; break;
            case TextureFormats.Dxt5: godotFormat = Image.Format.Dxt5; break;
            case TextureFormats.Bc4: godotFormat = Image.Format.RgtcR; break;
            case TextureFormats.Bc5: godotFormat = Image.Format.RgtcRg; break;
            case TextureFormats.Bc7: godotFormat = Image.Format.BptcRgba; break;
            case TextureFormats.Rgba32: godotFormat = Image.Format.Rgba8; break;
            case TextureFormats.Rgb24: godotFormat = Image.Format.Rgb8; break;
            case TextureFormats.Argb32:
                godotFormat = Image.Format.Rgba8;
                data = (byte[])data.Clone();
                for (var i = 0; i + 3 < data.Length; i += 4)
                    (data[i], data[i + 1], data[i + 2], data[i + 3]) = (data[i + 1], data[i + 2], data[i + 3], data[i]);
                break;
            case TextureFormats.Alpha8:
                godotFormat = Image.Format.La8;
                var la = new byte[data.Length * 2];
                for (var i = 0; i < data.Length; i++)
                {
                    la[i * 2] = 255;
                    la[i * 2 + 1] = data[i];
                }
                data = la;
                break;
            default:
                return null;
        }

        // Godot wants either one level or the full chain down to 1x1.
        var fullChain = (int)Math.Floor(Math.Log2(Math.Max(width, height))) + 1;
        var withMips = mips == fullChain && data.Length >= RequiredSize(width, height, true, godotFormat);
        var size = RequiredSize(width, height, withMips, godotFormat);
        if (data.Length < size)
            return null;
        var image = Image.CreateFromData(width, height, withMips, godotFormat, data.AsSpan(0, size).ToArray());
        if (!withMips && !image.IsCompressed())
            image.GenerateMipmaps();
        return image;
    }

    /// <summary>Bytes for an image and, optionally, its full mip chain. Block formats round up to 4x4 blocks.</summary>
    private static int RequiredSize(int width, int height, bool mipmaps, Image.Format format)
    {
        var (blockBytes, pixelBytes) = format switch
        {
            Image.Format.Dxt1 or Image.Format.RgtcR => (8, 0),
            Image.Format.Dxt5 or Image.Format.RgtcRg or Image.Format.BptcRgba => (16, 0),
            Image.Format.Rgba8 => (0, 4),
            Image.Format.Rgb8 => (0, 3),
            Image.Format.La8 => (0, 2),
            _ => (0, 4),
        };
        var total = 0;
        int w = width, h = height;
        while (true)
        {
            total += blockBytes > 0 ? ((w + 3) / 4) * ((h + 3) / 4) * blockBytes : w * h * pixelBytes;
            if (!mipmaps || (w == 1 && h == 1))
                break;
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        return total;
    }

    private static Variant V(GodotObject? o) => o is null ? default : o!;

    public Material Material(AssetRef? materialRef)
    {
        if (materialRef is not { ClassId: UnityClassId.Material } asset)
            return _fallback;
        if (_materials.TryGetValue(asset, out var cached))
            return cached;
        var mat = _assets.Read(asset, MaterialData.Read);
        var result = Convert(asset, mat);
        _materials[asset] = result;
        return result;
    }

    private bool MainTextureHasAlpha(AssetRef owner, MaterialData mat) =>
        mat.Texture("_MainTex") is { } slot
        && _assets.Resolve(owner.File, slot.Texture) is { ClassId: UnityClassId.Texture2D } tex
        && _assets.Read(tex, Texture2DData.Read).Format is TextureFormats.Dxt5 or TextureFormats.Dxt5Crunched
            or TextureFormats.Rgba32 or TextureFormats.Argb32 or TextureFormats.Bgra32 or TextureFormats.Rgba4444 or TextureFormats.Bc7;

    private Texture2D? Slot(AssetRef owner, MaterialData mat, string name) =>
        mat.Texture(name) is { } slot ? Texture(_assets.Resolve(owner.File, slot.Texture)) : null;

    private static Vector4 St(MaterialData mat, string name) =>
        mat.Texture(name) is { } slot ? new Vector4(slot.Scale.X, slot.Scale.Y, slot.Offset.X, slot.Offset.Y) : new Vector4(1, 1, 0, 0);

    private Material Convert(AssetRef owner, MaterialData mat)
    {
        var tint = UnityConvert.Color(mat.Color("_Color", System.Numerics.Vector4.One));

        bool HasSlot(string name) => mat.Textures.Any(t => t.Name == name);

        // Light shafts, force fields and similar effects glow on top of the scene.
        if ((HasSlot("_Stripes") || HasSlot("_TurbulanceWaves")) && !HasSlot("_MainTex"))
        {
            var glow = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                // Force fields (they also carry a colour ramp and mask) are faint in the original.
                AlbedoColor = (HasSlot("_ColorRamp") ? tint * 0.2f : tint) with { A = 1 },
                AlbedoTexture = Slot(owner, mat, "_Stripes"),
                DisableFog = true,
            };
            return glow;
        }

        // Water surfaces: a plain translucent stand-in until the water shader exists.
        if (HasSlot("_WavesNormal") || HasSlot("_Foam"))
        {
            return new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.16f, 0.45f, 0.55f, 0.65f),
                Roughness = 0.08f,
                MetallicSpecular = 0.6f,
            };
        }

        if (HasSlot("_PrimaryTex") || HasSlot("_Topper_MainTex"))
        {
            var m = new ShaderMaterial { Shader = WorldShader };
            var primarySlot = HasSlot("_PrimaryTex") && mat.Texture("_PrimaryTex") is not null ? "_PrimaryTex" : "_Topper_MainTex";
            m.SetShaderParameter("primary_tex", V(Slot(owner, mat, primarySlot)));
            m.SetShaderParameter("primary_st", St(mat, primarySlot));
            var topper = primarySlot == "_PrimaryTex" ? Slot(owner, mat, "_Topper_MainTex") : null;
            m.SetShaderParameter("has_topper", topper is not null);
            m.SetShaderParameter("topper_tex", V(topper));
            m.SetShaderParameter("topper_st", St(mat, "_Topper_MainTex"));
            m.SetShaderParameter("topper_coverage", mat.Float("_TopperCoverage", 0.5f));
            m.SetShaderParameter("use_mesh_uvs", mat.Float("_UseMeshUVs", 0) > 0.5f);
            m.SetShaderParameter("tint", tint);

            void Layer(string slot, string texParam, string stParam, string? flag = null)
            {
                var tex = Slot(owner, mat, slot);
                m.SetShaderParameter(texParam, V(tex));
                m.SetShaderParameter(stParam, St(mat, slot));
                if (flag is not null)
                    m.SetShaderParameter(flag, tex is not null);
            }
            if (primarySlot == "_PrimaryTex" && mat.Float("_EnableDetailTex", 1) > 0.5f)
                Layer("_DetailTex", "detail_tex", "detail_st", "has_detail");
            Layer("_DetailNoiseMask", "noise_mask", "noise_st");
            if (topper is not null && mat.Float("_TopperEnableDetailTex", 1) > 0.5f)
                Layer("_TopperDetailTex", "topper_detail_tex", "topper_detail_st", "has_topper_detail");
            Layer("_TopperDepth", "topper_depth", "topper_depth_st");
            Layer("_VerticalRamp", "vertical_ramp", "vertical_ramp_st", "has_vertical_ramp");
            m.SetShaderParameter("ramp_top", UnityConvert.Color(mat.Color("_RampTop", System.Numerics.Vector4.One)));
            m.SetShaderParameter("top_ramp_offset", mat.Float("_TopRampOffset", 35));
            m.SetShaderParameter("top_ramp_scale", mat.Float("_TopRampScale", 30));
            m.SetShaderParameter("sea_ramp", UnityConvert.Color(mat.Color("_SeaLevelRampLower", System.Numerics.Vector4.One)));
            m.SetShaderParameter("sea_ramp_offset", mat.Float("_SeaLevelRampOffset", -3));
            m.SetShaderParameter("sea_ramp_scale", mat.Float("_SeaLevelRampScale", 1));
            return m;
        }

        if (mat.Textures.Any(t => t.Name == "_ColorMask"))
        {
            var m = new ShaderMaterial { Shader = MaskShader };
            m.SetShaderParameter("color_mask", V(Slot(owner, mat, "_ColorMask")));
            m.SetShaderParameter("ambient_occlusion", V(Slot(owner, mat, "_AmbientOcclusion")));
            var strokes = Slot(owner, mat, "_PaintStrokes");
            m.SetShaderParameter("has_strokes", strokes is not null);
            m.SetShaderParameter("paint_strokes", V(strokes));
            m.SetShaderParameter("strokes_st", St(mat, "_PaintStrokes"));
            m.SetShaderParameter("strokes_triplanar", mat.Keywords.Contains("_PAINTMASKTRIPLANAR_ON"));
            m.SetShaderParameter("ao_uv2", mat.Keywords.Contains("_AOUV1_ON"));
            var overrideTex = Slot(owner, mat, "_Override");
            m.SetShaderParameter("has_override", overrideTex is not null);
            m.SetShaderParameter("override_tex", V(overrideTex));
            m.SetShaderParameter("override_uv2", mat.Keywords.Contains("_OVERRIDEUV1_ON"));
            m.SetShaderParameter("glow_ramp", V(Slot(owner, mat, "_GlowRamp")));
            m.SetShaderParameter("glow_multiplier", mat.Float("_GlowMultiplier", 1));
            // Region colours _Color00 to _Color71: the first digit is the region, the second dark (0) or light (1).
            Godot.Collections.Array<Vector4> Colors(int shade) => new(Enumerable.Range(0, 8).Select(i =>
            {
                var c = UnityConvert.Color(mat.Color($"_Color{i}{shade}", System.Numerics.Vector4.Zero)).SrgbToLinear();
                return new Vector4(c.R, c.G, c.B, 1);
            }));
            m.SetShaderParameter("dark_colors", Colors(0));
            m.SetShaderParameter("light_colors", Colors(1));
            return m;
        }

        // Vegetables: vertex-colour regions coloured from small ramp textures.
        if (mat.Keywords.Contains("_VERTEXCOLORMASK_ON") && HasSlot("_RampBlack"))
        {
            var m = new ShaderMaterial { Shader = RampShader };
            m.SetShaderParameter("ramp_red", V(Slot(owner, mat, "_RampRed")));
            m.SetShaderParameter("ramp_green", V(Slot(owner, mat, "_RampGreen")));
            m.SetShaderParameter("ramp_blue", V(Slot(owner, mat, "_RampBlue")));
            m.SetShaderParameter("ramp_black", V(Slot(owner, mat, "_RampBlack")));
            m.SetShaderParameter("ambient_occlusion", V(Slot(owner, mat, "_AmbientOcclusion")));
            return m;
        }

        // House windows fake a room behind the glass; show the room texture under a glossy tinted pane.
        if (HasSlot("_Interior") && HasSlot("_MatCap"))
        {
            var st = St(mat, "_Interior");
            return new StandardMaterial3D
            {
                AlbedoTexture = Slot(owner, mat, "_Interior"),
                AlbedoColor = (tint * 2.5f).Clamp() with { A = 1 },
                Uv1Scale = new Vector3(st.X, st.Y, 1),
                Roughness = 0.15f,
                MetallicSpecular = 0.8f,
                DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Lambert,
            };
        }

        // Sparkle overlays (the slime portal's shimmer): a faint additive glitter.
        if (HasSlot("_Sparkles") && !HasSlot("_MainTex"))
        {
            return new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                AlbedoTexture = Slot(owner, mat, "_Sparkles"),
                AlbedoColor = new Color(0.35f, 0.35f, 0.35f),
                DisableFog = true,
            };
        }

        var standard = new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.85f, MetallicSpecular = 0.3f, DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Lambert };
        // Untextured metal trims carry only a colour and a shininess.
        if (mat.Floats.ContainsKey("_Shininess") && !mat.Textures.Any(t => !t.Texture.IsNull))
        {
            standard.Metallic = 0.7f;
            standard.Roughness = 0.35f;
        }
        var main = Slot(owner, mat, "_MainTex") ?? Slot(owner, mat, "_Diffuse") ?? Slot(owner, mat, "_Albedo");
        if (main is not null)
        {
            standard.AlbedoTexture = main;
            var st = St(mat, mat.Texture("_MainTex") is not null ? "_MainTex" : "_Diffuse");
            standard.Uv1Scale = new Vector3(st.X, st.Y, 1);
            standard.Uv1Offset = new Vector3(st.Z, st.W, 0);
        }
        // Grass and flower cards: a single texture whose alpha holds the blade shapes, seen from both sides.
        if (mat.Textures.Count == 1 && mat.Textures[0].Name == "_MainTex" && MainTextureHasAlpha(owner, mat))
        {
            standard.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
            standard.AlphaScissorThreshold = 0.5f;
            standard.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        }
        else if (mat.Keywords.Contains("_ALPHATEST_ON") || mat.Floats.ContainsKey("_Cutoff") && mat.Tags.GetValueOrDefault("RenderType") == "TransparentCutout")
        {
            standard.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
            standard.AlphaScissorThreshold = mat.Float("_Cutoff", 0.5f);
        }
        else if (mat.Keywords.Contains("_ALPHABLEND_ON") || mat.Tags.GetValueOrDefault("RenderType") == "Transparent")
        {
            standard.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        }
        return standard;
    }
}
