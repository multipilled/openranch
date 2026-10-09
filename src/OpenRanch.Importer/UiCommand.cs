using System.Globalization;
using System.Numerics;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Ui;
using OpenRanch.Formats.Unity;
using OpenRanch.Formats.Unity.Managed;

namespace OpenRanch.Importer;

/// <summary>
/// `openranch-import ui FILE [--root NAME] [--depth N] [--fields]`: prints the uGUI hierarchies in one of the game's
/// serialized files (level2 is the main menu scene, level3 the world with the HUD) with each object's rect and its
/// UI components, to find where the original's menus and HUD keep their layout.
/// </summary>
public static class UiCommand
{
    public static int Run(string[] args, GameInstall install)
    {
        var fileName = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "level2";
        var rootName = Option(args, "--root");
        var depth = int.TryParse(Option(args, "--depth"), out var d) ? d : 99;
        var fields = args.Contains("--fields");
        using var scripts = new GameScripts(install);
        var reader = new UiReader(scripts.Assets, scripts.Reader);
        var file = scripts.Assets.File(fileName) ?? throw new IOException($"No data file {fileName}.");
        var shown = 0;
        foreach (var root in reader.Roots(file))
        {
            var start = rootName is null ? root : root.Walk().FirstOrDefault(n => n.Name == rootName);
            if (start is null)
                continue;
            Print(scripts.Assets, start, 0, depth, fields);
            shown++;
        }
        Console.WriteLine($"{shown} hierarchies");
        return 0;
    }

    private static void Print(AssetSet assets, UiNode node, int indent, int depth, bool fields)
    {
        var pad = new string(' ', indent * 2);
        var rect = node.Rect is { } r
            ? $" a({F(r.AnchorMin)}-{F(r.AnchorMax)}) p{F(r.AnchoredPosition)} s{F(r.SizeDelta)} pv{F(r.Pivot)}" +
              (r.Transform.LocalScale != Vector3.One ? $" sc{r.Transform.LocalScale.X:0.##}" : "")
            : "";
        Console.WriteLine($"{pad}{node.Name}{(node.Active ? "" : " [off]")}{rect}");
        foreach (var c in node.Components)
        {
            if (c.ClassId is UnityClassId.RectTransform or UnityClassId.Transform or 222)
                continue;
            var label = c.ScriptClass is { } s ? s[(s.LastIndexOf('.') + 1)..] : UnityClassId.NameOf(c.ClassId);
            Console.WriteLine($"{pad}  - {label}{(c.Enabled ? "" : " (disabled)")} {Summary(assets, c, fields)}");
        }
        if (indent >= depth)
            return;
        foreach (var child in node.Children)
            Print(assets, child, indent + 1, depth, fields);
    }

    private static string Summary(AssetSet assets, UiComponent c, bool all)
    {
        if (c.Data is null)
            return "";
        string[] keys = all ? c.Data.Fields.Select(f => f.Key).ToArray() : (c.ScriptClass ?? "") switch
        {
            var s when s.EndsWith(".Image", StringComparison.Ordinal) => ["m_Sprite", "m_Type", "m_Color", "m_FillMethod", "m_FillAmount", "m_PreserveAspect"],
            var s when s.EndsWith(".RawImage", StringComparison.Ordinal) => ["m_Texture", "m_Color", "m_UVRect"],
            var s when s.EndsWith("TextMeshProUGUI", StringComparison.Ordinal) => ["m_text", "m_fontAsset", "m_fontColor", "m_fontSize", "m_fontSizeMax", "m_enableAutoSizing", "m_textAlignment", "m_fontStyle", "m_enableWordWrapping"],
            var s when s.EndsWith(".Text", StringComparison.Ordinal) => ["m_Text", "m_FontData", "m_Color"],
            var s when s.EndsWith("CanvasScaler", StringComparison.Ordinal) => ["m_UiScaleMode", "m_ReferenceResolution", "m_ScreenMatchMode", "m_MatchWidthOrHeight"],
            var s when s.EndsWith("LayoutGroup", StringComparison.Ordinal) => ["m_Padding", "m_ChildAlignment", "m_Spacing", "m_StartCorner", "m_StartAxis", "m_CellSize", "m_Constraint", "m_ConstraintCount", "m_ChildControlWidth", "m_ChildControlHeight", "m_ChildForceExpandWidth", "m_ChildForceExpandHeight"],
            var s when s.EndsWith("LayoutElement", StringComparison.Ordinal) => ["m_IgnoreLayout", "m_MinWidth", "m_MinHeight", "m_PreferredWidth", "m_PreferredHeight"],
            var s when s.EndsWith("XlateText", StringComparison.Ordinal) || s.Contains("Xlate", StringComparison.Ordinal) => c.Data.Fields.Select(f => f.Key).ToArray(),
            _ => c.Data.Fields.Where(f => f.Value is PPtr or string or int or float or bool).Take(all ? 999 : 6).Select(f => f.Key).ToArray(),
        };
        return string.Join(" ", keys.Where(c.Data.Has).Select(k => $"{k}={Describe(assets, c.Ref.File, c.Data[k])}"));
    }

    private static string Describe(AssetSet assets, SerializedFile file, object? value) => value switch
    {
        null => "null",
        string s => $"\"{(s.Length > 60 ? s[..60] + "..." : s).Replace("\n", "\\n")}\"",
        PPtr p => p.IsNull ? "-" : NameOf(assets, file, p),
        Vector4 v => $"({v.X:0.###},{v.Y:0.###},{v.Z:0.###},{v.W:0.###})",
        Vector2 v => F(v),
        float f => f.ToString("0.###", CultureInfo.InvariantCulture),
        SerializedObject o => "{" + string.Join(",", o.Fields.Take(6).Select(f => $"{f.Key}={Describe(assets, file, f.Value)}")) + "}",
        List<object?> list => $"[{string.Join(",", list.Take(8).Select(v => Describe(assets, file, v)))}{(list.Count > 8 ? $",..{list.Count}" : "")}]",
        _ => value.ToString() ?? "",
    };

    /// <summary>The name of a referenced asset: most asset classes begin with m_Name; scripts carry it after their header.</summary>
    public static string NameOf(AssetSet assets, SerializedFile file, PPtr p)
    {
        if (assets.Resolve(file, p) is not { } a)
            return $"?{p.FileId}:{p.PathId}";
        var r = assets.Reader(a);
        try
        {
            switch (a.ClassId)
            {
                case UnityClassId.MonoBehaviour:
                    var (go, _, _, name) = MonoBehaviourReader.ReadHeader(r);
                    if (name.Length > 0)
                        return $"mb'{name}'";
                    return assets.Resolve(a.File, go) is { } g ? $"mb@{assets.Read(g, GameObjectData.Read).Name}" : "mb";
                case UnityClassId.GameObject:
                    return $"go'{GameObjectData.Read(r).Name}'";
                case UnityClassId.RectTransform or UnityClassId.Transform:
                    var owner = PPtr.Read(r);
                    return assets.Resolve(a.File, owner) is { } o ? $"tr'{assets.Read(o, GameObjectData.Read).Name}'" : "tr";
                default:
                    return $"{UnityClassId.NameOf(a.ClassId)}'{r.ReadAlignedString()}'";
            }
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException)
        {
            return UnityClassId.NameOf(a.ClassId);
        }
    }

    private static string F(Vector2 v) => $"({v.X:0.##},{v.Y:0.##})";

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
