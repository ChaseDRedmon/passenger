using System.Text;
using System.Text.RegularExpressions;
using Passenger.Core;

namespace Passenger.App.Workspace;

/// <summary>Rider's Add Reference and Remove on a project reference, and its items following a file moved, made to the
/// project file's text: a reference goes beside the project's others, or in an item group of its own before
/// &lt;/Project&gt;, written as dotnet add reference writes it (relative, with backslashes); taking it out takes the group
/// it leaves empty, so the file is as it was. The rest of the file keeps its bytes, its byte order mark, line endings and
/// indentation: dotnet add reference reformats what it writes back. Paths are canonical. A reference's edit returns null
/// when it was done, else why not; a file that cannot be read or written throws.</summary>
public static partial class ProjectEdits
{
    public static string? AddReference(string project, string referenced)
    {
        ProjectText file = new(project);
        if (file.References().Any(r => PathNames.Same(r.Path, referenced)))
        {
            return $"{Path.GetFileNameWithoutExtension(project)} already references {Path.GetFileNameWithoutExtension(referenced)}";
        }
        string include = Path.GetRelativePath(Path.GetDirectoryName(project)!, referenced).Replace('/', '\\');
        string text = file.Text;
        string eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string indent = IndentUnit(text);
        string element = $"<ProjectReference Include=\"{include}\" />";
        // Beside the project's other references, in an item group of no condition.
        if (file.References().LastOrDefault(r => r.Group is { Conditioned: false }) is { Group: not null } last)
        {
            int lineEnd = text.IndexOf('\n', last.End);
            int at = lineEnd < 0 ? text.Length : lineEnd + 1;
            file.Write(text.Insert(at, $"{LineIndent(text, last.Start)}{element}{eol}"));
            return null;
        }
        int close = text.LastIndexOf("</Project>", StringComparison.Ordinal);
        if (close < 0)
        {
            return $"{Path.GetFileName(project)} is not a project file";
        }
        int closeLine = text.LastIndexOf('\n', Math.Max(0, close - 1)) + 1;
        // A file that leaves a blank line between its groups gets one after the new group too.
        bool spaced = closeLine >= 2 && text[..closeLine].TrimEnd('\r', '\n').Length < text[..closeLine].Length - eol.Length;
        string group = $"{indent}<ItemGroup>{eol}{indent}{indent}{element}{eol}{indent}</ItemGroup>{eol}{(spaced ? eol : string.Empty)}";
        file.Write(text.Insert(closeLine, group));
        return null;
    }

    public static string? RemoveReference(string project, string referenced)
    {
        ProjectText file = new(project);
        if (file.References().FirstOrDefault(r => PathNames.Same(r.Path, referenced)) is not { } reference)
        {
            return $"{Path.GetFileNameWithoutExtension(project)} does not reference {Path.GetFileNameWithoutExtension(referenced)}";
        }
        string text = file.Text;
        (int start, int end) = WholeLines(text, reference.Start, reference.End);
        if (reference.Group is { } group && (text[group.OpenEnd..start] + text[end..group.CloseStart]).Trim().Length == 0)
        {
            // The group is left empty: it goes too, with the blank line that set it apart from the next.
            (int groupStart, int groupEnd) = WholeLines(text, group.OpenStart, group.CloseEnd);
            if (IsBlankLineBefore(text, groupStart) && BlankLineAt(text, groupEnd) is int blankEnd)
            {
                groupEnd = blankEnd;
            }
            file.Write(text.Remove(groupStart, groupEnd - groupStart));
            return null;
        }
        file.Write(text.Remove(start, end - start));
        return null;
    }

    /// <summary>The project following files and folders that moved or were renamed, as Rider's project model does: an
    /// item that names a file by itself (its Include or Update, no wildcards) names where it went, and a DependentUpon or
    /// LastGenOutput naming one (relative to its item's folder) names it there. Whether the file changed.</summary>
    public static bool MoveItems(string project, IReadOnlyList<(string From, string To)> moves)
    {
        ProjectText file = new(project);
        string folder = Path.GetDirectoryName(project)!;
        // A file moved, or one inside a folder that was.
        string Moved(string path) => moves.FirstOrDefault(m => PathNames.Within(path, m.From)) is { From: not null } move ? PathNames.Moved(path, move.From, move.To) : path;
        List<(int Start, int End)> comments = [.. Comment().Matches(file.Text).Select(m => (m.Index, m.Index + m.Length))];
        string text = ItemElement().Replace(file.Text, item =>
        {
            string items = item.Groups["items"].Value;
            if (comments.Any(c => item.Index >= c.Start && item.Index < c.End) || items.IndexOfAny(['*', '?', ';', '$', '@', '%']) >= 0)
            {
                return item.Value;
            }
            string was = Path.GetFullPath(Path.Combine(folder, items.Replace('\\', '/')));
            string now = Moved(was);
            // What the item names beside it, relative to its folder: where that is now, from where the item is now.
            string element = RelatedName().Replace(item.Value, related =>
            {
                Capture value = related.Groups["value"];
                string named = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(was)!, value.Value.Trim().Replace('\\', '/')));
                string target = Moved(named);
                return target == named && now == was ? related.Value
                    : Spliced(related, value, Written(Path.GetRelativePath(Path.GetDirectoryName(now)!, target), value.Value));
            });
            return now == was ? element : ItemAttribute().Replace(element, attribute =>
                Spliced(attribute, attribute.Groups["items"], Written(Path.GetRelativePath(folder, now), items)), 1);
        });
        if (text == file.Text)
        {
            return false;
        }
        file.Write(text);
        return true;
    }

    /// <summary>A path as a project writes it, with the separators the value it replaces had.</summary>
    private static string Written(string relative, string was) => was.Contains('\\') ? relative.Replace('/', '\\') : relative.Replace('\\', '/');

    /// <summary>A match's text with one of its groups' text replaced.</summary>
    private static string Spliced(Match match, Capture group, string replacement) =>
        string.Concat(match.Value.AsSpan(0, group.Index - match.Index), replacement, match.Value.AsSpan(group.Index - match.Index + group.Length));

    /// <summary>The line or lines a span stands on, their indentation and line break included, when nothing else is on
    /// them; the span alone otherwise.</summary>
    private static (int Start, int End) WholeLines(string text, int start, int end)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        if (start > 0 && text[lineStart..start].Trim().Length > 0)
        {
            return (start, end);
        }
        int lineEnd = text.IndexOf('\n', end);
        int stop = lineEnd < 0 ? text.Length : lineEnd + 1;
        return text[end..(lineEnd < 0 ? text.Length : lineEnd)].Trim().Length > 0 ? (start, end) : (lineStart, stop);
    }

    private static bool IsBlankLineBefore(string text, int lineStart)
    {
        if (lineStart < 2)
        {
            return false;
        }
        int previous = text.LastIndexOf('\n', lineStart - 2) + 1;
        return text[previous..(lineStart - 1)].Trim().Length == 0;
    }

    /// <summary>The end of a blank line beginning at a place, line break included, or null when the line there is not blank.</summary>
    private static int? BlankLineAt(string text, int at)
    {
        int lineEnd = text.IndexOf('\n', at);
        return lineEnd >= 0 && text[at..lineEnd].Trim().Length == 0 ? lineEnd + 1 : null;
    }

    /// <summary>The indentation one level of the file takes: its first indented element's.</summary>
    private static string IndentUnit(string text) => IndentedLine().Match(text) is { Success: true } line ? line.Groups[1].Value : "  ";

    private static string LineIndent(string text, int at)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        return text[lineStart..at];
    }

    [GeneratedRegex(@"\n([ \t]+)<")]
    private static partial Regex IndentedLine();

    [GeneratedRegex(@"<ProjectReference\b[^>]*?\bInclude\s*=\s*""(?<include>[^""]*)""[^>]*?(/>|>.*?</ProjectReference\s*>)", RegexOptions.Singleline)]
    private static partial Regex ReferenceElement();

    [GeneratedRegex(@"<ItemGroup\b(?<attributes>[^>]*?)>(?<body>.*?)</ItemGroup\s*>", RegexOptions.Singleline)]
    private static partial Regex ItemGroup();

    /// <summary>An item that names files: its start tag's Include or Update, and its end.</summary>
    [GeneratedRegex(@"<(?<tag>[A-Za-z_][\w.-]*)(?=[^>]*\b(?:Include|Update)\s*=)[^>]*?\b(?:Include|Update)\s*=\s*""(?<items>[^""]*)""[^>]*?(?:/>|>.*?</\k<tag>\s*>)", RegexOptions.Singleline)]
    private static partial Regex ItemElement();

    [GeneratedRegex(@"\b(?:Include|Update)\s*=\s*""(?<items>[^""]*)""")]
    private static partial Regex ItemAttribute();

    /// <summary>A file an item names beside it: what it depends on, or what generates from it, as an attribute or an element.</summary>
    [GeneratedRegex(@"\b(?:DependentUpon|LastGenOutput)\s*=\s*""(?<value>[^""]+)""|<(?<name>DependentUpon|LastGenOutput)>(?<value>[^<]+)</\k<name>\s*>")]
    private static partial Regex RelatedName();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    /// <summary>An item group's tags, by where they start and end in the file.</summary>
    private sealed record Group(int OpenStart, int OpenEnd, int CloseStart, int CloseEnd, bool Conditioned);

    /// <summary>A project reference in the file: the project it names, where its element is, and the group holding it.</summary>
    private sealed record Reference(string Path, int Start, int End, Group? Group);

    /// <summary>A project file read for one change: its text without a byte order mark, which goes back as it came.</summary>
    private sealed class ProjectText
    {
        private readonly string path;
        private readonly bool bom;

        public ProjectText(string path)
        {
            this.path = path;
            byte[] bytes = File.ReadAllBytes(path);
            bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
            Text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        }

        public string Text { get; }

        /// <summary>The project's references outside comments, in the file's order.</summary>
        public List<Reference> References()
        {
            string folder = Path.GetDirectoryName(path)!;
            List<(int Start, int End)> comments = [.. Comment().Matches(Text).Select(m => (m.Index, m.Index + m.Length))];
            bool Commented(int at) => comments.Any(c => at >= c.Start && at < c.End);
            List<Group> groups = [.. ItemGroup().Matches(Text).Where(m => !Commented(m.Index)).Select(m => new Group(
                m.Index,
                m.Groups["body"].Index,
                m.Groups["body"].Index + m.Groups["body"].Length,
                m.Index + m.Length,
                m.Groups["attributes"].Value.Contains("Condition", StringComparison.OrdinalIgnoreCase)))];
            return [.. ReferenceElement().Matches(Text).Where(m => !Commented(m.Index)).Select(m => new Reference(
                PathNames.Canonical(Path.Combine(folder, m.Groups["include"].Value.Replace('\\', '/'))),
                m.Index,
                m.Index + m.Length,
                groups.FirstOrDefault(g => m.Index >= g.OpenEnd && m.Index < g.CloseStart)))];
        }

        /// <summary>Writes the project back, waiting out another program reading it, as a project load does the moment the
        /// files the project names have moved (<see cref="FileText.WhileHeld"/>).</summary>
        public void Write(string text)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            FileText.WhileHeld(() => File.WriteAllBytes(path, bom ? [.. Encoding.UTF8.Preamble, .. bytes] : bytes));
        }
    }
}
