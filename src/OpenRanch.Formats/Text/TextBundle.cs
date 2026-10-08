using System.Text;

namespace OpenRanch.Formats.Text;

/// <summary>
/// One language's copy of a message bundle: a text file of <c>key = value</c> lines such as
/// <c>l.pink_slime = Pink Slime</c>. The format is described in docs/formats/text.md.
/// </summary>
public sealed class TextBundle
{
    /// <summary>Key naming another bundle to look in when a key is missing here.</summary>
    public const string ParentKey = "__parent";

    public string Language { get; }
    public string Name { get; }
    public IReadOnlyDictionary<string, string> Entries { get; }

    /// <summary>Lines that are neither comments, blank, nor a single <c>key = value</c> pair.</summary>
    public IReadOnlyList<string> MalformedLines { get; }

    public TextBundle(string language, string name, IReadOnlyDictionary<string, string> entries, IReadOnlyList<string> malformed)
    {
        Language = language;
        Name = name;
        Entries = entries;
        MalformedLines = malformed;
    }

    public string? Parent => Entries.TryGetValue(ParentKey, out var p) && p.Length > 0 ? p : null;

    public string? this[string key] => Entries.TryGetValue(key, out var v) ? v : null;

    public static TextBundle Parse(string language, string name, string text)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var malformed = new List<string>();
        foreach (var line in LogicalLines(text))
        {
            if (line.Length < 2 || line[0] == '#')
                continue;
            var split = SeparatorIndex(line);
            if (split < 0)
            {
                malformed.Add(line.TrimEnd());
                continue;
            }
            var key = Unescape(line.AsSpan(0, split)).Trim();
            var value = Unescape(line.AsSpan(split + 1)).Trim();
            entries[key] = value; // a later line wins
        }
        return new TextBundle(language, name, entries, malformed);
    }

    /// <summary>
    /// Splits the text at '\n'. A line that ends in a backslash carries on into the next one: the
    /// backslash, the line break and the next line's leading spaces and tabs are dropped.
    /// </summary>
    private static IEnumerable<string> LogicalLines(string text)
    {
        var current = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] is '\r' or '\n')
            {
                i += text[i + 1] == '\r' && i + 2 < text.Length && text[i + 2] == '\n' ? 3 : 2;
                while (i < text.Length && text[i] is ' ' or '\t')
                    i++;
                continue;
            }
            if (c == '\n')
            {
                yield return current.ToString();
                current.Clear();
            }
            else
                current.Append(c);
            i++;
        }
        yield return current.ToString();
    }

    /// <summary>The position of the only '=' not escaped by a backslash, or -1 if there isn't exactly one.</summary>
    private static int SeparatorIndex(string line)
    {
        var found = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] != '=' || (i > 0 && line[i - 1] == '\\'))
                continue;
            if (found >= 0)
                return -1;
            found = i;
        }
        return found;
    }

    /// <summary>Escapes: <c>\=</c> is '=', <c>\n</c> a line break, <c>­</c> a soft hyphen, <c>&amp;bsol;</c> a backslash.</summary>
    private static string Unescape(ReadOnlySpan<char> s)
    {
        if (s.IndexOfAny('\\', '&') < 0)
            return s.ToString();
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var rest = s[i..];
            if (rest.StartsWith("\\="))
            {
                sb.Append('=');
                i += 1;
            }
            else if (rest.StartsWith("\\n"))
            {
                sb.Append('\n');
                i += 1;
            }
            else if (rest.StartsWith("\\u00AD"))
            {
                sb.Append('­');
                i += 5;
            }
            else if (rest.StartsWith("&bsol;"))
            {
                sb.Append('\\');
                i += 5;
            }
            else
                sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
