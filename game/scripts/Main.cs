using System.Linq;
using System.Text;
using Godot;
using OpenRanch.Formats.Game;
using OpenRanch.Formats.Saves;

namespace OpenRanch.Game;

/// <summary>
/// Start screen for now: shows whether Slime Rancher was found and which ranches can be opened.
/// The Ranch itself arrives with milestone 1.
/// </summary>
public partial class Main : Control
{
    public override void _Ready()
    {
        var status = Describe();
        GetNode<Label>("%Status").Text = status;
        GD.Print(status);
    }

    private static string Describe()
    {
        var text = new StringBuilder();
        var install = GameInstall.Find();
        text.AppendLine(install is null
            ? $"Slime Rancher wasn't found. Install it from Steam or set {GameInstall.GameDirVariable}."
            : $"Slime Rancher found at {install.RootDirectory}");

        var saveDir = SaveFile.DefaultSaveDirectory();
        if (!System.IO.Directory.Exists(saveDir))
            return text.AppendLine("No saves yet.").ToString();

        // The game keeps several rotating saves per ranch; show the newest of each.
        var newest = new System.Collections.Generic.Dictionary<string, SaveInfo>();
        foreach (var path in System.IO.Directory.GetFiles(saveDir, "*.sav"))
        {
            using var stream = new System.IO.MemoryStream(System.IO.File.ReadAllBytes(path), writable: false);
            var (tag, version) = SaveFile.PeekHeader(stream);
            if (tag != SaveFile.GameTag || !SaveFile.SupportedVersions.Contains(version))
                continue;
            var info = SaveFile.Describe(SaveFile.Read(stream));
            if (!newest.TryGetValue(info.GameName, out var seen) || info.SavedAt > seen.SavedAt)
                newest[info.GameName] = info;
        }

        text.AppendLine();
        text.AppendLine(newest.Count == 0 ? "No ranches in a supported save format yet." : "Your ranches:");
        foreach (var info in newest.Values.OrderByDescending(i => i.SavedAt))
            text.AppendLine($"  {info.DisplayName}: day {info.Day}, {info.Currency:N0} newbucks, saved {info.SavedAt:yyyy-MM-dd}");
        return text.ToString();
    }
}
