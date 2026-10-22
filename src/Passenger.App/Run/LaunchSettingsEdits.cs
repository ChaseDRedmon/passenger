using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Passenger.App.Workspace;

namespace Passenger.App.Run;

/// <summary>A run configuration's changes written into the launch profile it was made from, in its project's
/// <c>Properties/launchSettings.json</c>, as text: a value replaced where it stands, a property added after the profile's
/// last, indented as they are (on their line, for a profile written on one), one taken out with its line, so the other
/// profiles and properties, the comments and trailing commas dotnet run's reader takes, stay byte for byte as they were.</summary>
public static class LaunchSettingsEdits
{
    /// <summary>One value of a profile: a property (<c>commandLineArgs</c>) or, given <see cref="Entry"/>, one entry of an
    /// object property (a variable of <c>environmentVariables</c>); a null value takes it out.</summary>
    public sealed record Change(string Property, string? Entry, JsonNode? Value);

    private static readonly JsonSerializerOptions Written = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>What of a generated configuration's changes from its source (<paramref name="edited"/> over
    /// <paramref name="source"/>) its launch profile can hold, as changes to the profile. What the file has no place for (a
    /// target framework, the console, building first, a program for a profile that runs its project, a variable the
    /// profile's application URL or HTTPS port gives) is left out, to stay kept with the folder.</summary>
    public static IReadOnlyList<Change> For(ProjectModel project, LaunchProfile profile, RunConfiguration source, RunConfiguration edited)
    {
        List<Change> changes = [];
        bool container = profile.RunsInContainer;
        if (!container && edited.Arguments != source.Arguments)
        {
            changes.Add(new Change("commandLineArgs", null, edited.Arguments.Length > 0 ? JsonValue.Create(edited.Arguments) : null));
        }
        if (!container && edited.Program != source.Program && edited.Program.Length > 0 && profile.Is(LaunchProfile.ExecutableCommand))
        {
            changes.Add(new Change("executablePath", null, JsonValue.Create(edited.Program)));
        }
        if (!container && edited.WorkingDirectory != source.WorkingDirectory && edited.WorkingDirectory.Trim().Length > 0)
        {
            changes.Add(new Change("workingDirectory", null, JsonValue.Create(edited.WorkingDirectory)));
        }
        if (edited.LaunchBrowser != source.LaunchBrowser)
        {
            changes.Add(new Change("launchBrowser", null, JsonValue.Create(edited.LaunchBrowser)));
        }
        if (edited.BrowserUrl != source.BrowserUrl && LaunchUrl(profile, edited.BrowserUrl) is { } url)
        {
            changes.Add(new Change("launchUrl", null, url.Length > 0 ? JsonValue.Create(url) : null));
        }
        foreach ((string name, string value) in edited.Environment)
        {
            if (!source.Environment.TryGetValue(name, out string? was) || was != value)
            {
                changes.Add(new Change("environmentVariables", name, JsonValue.Create(value)));
            }
        }
        foreach (string name in source.Environment.Keys.Where(n => !edited.Environment.ContainsKey(n) && profile.EnvironmentVariables.ContainsKey(n)))
        {
            changes.Add(new Change("environmentVariables", name, null));
        }
        if (container && edited.RunArguments != source.RunArguments && RunWords(project, profile, edited.RunArguments) is { } words)
        {
            string key = profile.RunArgumentsKey ?? LaunchProfile.RunArgumentsKeys[profile.Is(LaunchProfile.DockerCommand) ? 0 : 1];
            changes.Add(new Change(key, null, words.Length > 0 ? JsonValue.Create(words) : null));
        }
        return changes;
    }

    /// <summary>The <c>launchUrl</c> that makes a browser address: relative to the profile's first application URL (a
    /// container's published address) where it begins so, empty for that address itself, else as written when it is a
    /// whole address; null when the profile cannot make it.</summary>
    private static string? LaunchUrl(LaunchProfile profile, string url)
    {
        string? root = profile.RunsInContainer ? Containers.ContainerConfigurations.ServiceUrl
            : profile.ApplicationUrl?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (root is not null && (url == root || url == root.TrimEnd('/') + "/"))
        {
            return string.Empty;
        }
        if (root is not null && url.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal))
        {
            return url[(root.TrimEnd('/').Length + 1)..];
        }
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? whole) && whole.Scheme is "http" or "https"
            || (profile.RunsInContainer && url.Contains('{', StringComparison.Ordinal)) ? url : null;
    }

    /// <summary>A container profile's own <c>docker run</c> words: what follows the words its ports and env files make,
    /// which the configuration's begin with; null when they no longer begin so.</summary>
    private static string? RunWords(ProjectModel project, LaunchProfile profile, string edited)
    {
        string made = Containers.ContainerConfigurations.From(project, profile with { RunArguments = null }).RunArguments;
        return made.Length == 0 ? edited.Trim()
            : edited == made ? string.Empty
            : edited.StartsWith(made + " ", StringComparison.Ordinal) ? edited[(made.Length + 1)..].Trim()
            : null;
    }

    /// <summary>Writes the changes into a profile of a launchSettings.json as a save writes (<see cref="FileText.Save"/>):
    /// its encoding, byte order mark and line breaks as they were. Null once written, else why not.</summary>
    public static string? Write(string path, string profile, IReadOnlyList<Change> changes)
    {
        FileText file = FileText.Load(path) ?? throw new IOException($"{Path.GetFileName(path)} is not a text file");
        (string? text, string? problem) = Changed(file.Text, profile, changes);
        if (text is null)
        {
            return problem;
        }
        if (text != file.Text)
        {
            FileText.Save(path, text, file.Encoding, file.HasBom, file.CrBreaks);
        }
        return null;
    }

    /// <summary>The text with the profile's values changed, every other character as it was; null with why when it does not
    /// read as JSON or has no such profile.</summary>
    public static (string? Text, string? Problem) Changed(string text, string profile, IReadOnlyList<Change> changes)
    {
        foreach (Change change in changes)
        {
            Node root;
            try
            {
                root = new Reader(text).Document();
            }
            catch (FormatException unreadable)
            {
                return (null, $"launchSettings.json does not read as JSON: {unreadable.Message}");
            }
            if (root.Member("profiles")?.Value is not { Kind: Kind.Object } profiles || profiles.Member(profile)?.Value is not { Kind: Kind.Object } found)
            {
                return (null, $"launchSettings.json has no profile {profile}");
            }
            Layout layout = new(text, Unit(text, root));
            text = change.Entry is null ? layout.Set(found, change.Property, change.Value) : layout.SetEntry(found, change.Property, change.Entry, change.Value);
        }
        return (text, null);
    }

    /// <summary>One level of the file's indentation: what its root's first property stands in from the root's own line,
    /// else two spaces, as dotnet new writes launchSettings.json.</summary>
    private static string Unit(string text, Node root)
    {
        if (root.Members.FirstOrDefault() is { } first && !Layout.SameLine(text, root.Start, first.Start))
        {
            string inner = Layout.IndentOf(text, first.Start);
            string outer = Layout.IndentOf(text, root.Start);
            if (inner.Length > outer.Length && inner.StartsWith(outer, StringComparison.Ordinal))
            {
                return inner[outer.Length..];
            }
        }
        return "  ";
    }

    /// <summary>The changes made in the text as it is laid out: its line break, its unit of indentation, and each object's
    /// own way of holding its members (each on a line of its own, or on the object's line).</summary>
    private sealed class Layout
    {
        private readonly string eol;
        private readonly string unit;
        private string text;

        public Layout(string text, string unit)
        {
            this.text = text;
            this.unit = unit;
            eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        }

        public string Set(Node holder, string name, JsonNode? value)
        {
            Member? member = holder.Member(name);
            if (value is null)
            {
                return member is null ? text : Remove(holder, member);
            }
            string written = value.ToJsonString(Written);
            return member is null ? Insert(holder, name, written) : Replace(member.Value.Start, member.Value.End, written);
        }

        public string SetEntry(Node holder, string name, string entry, JsonNode? value)
        {
            Member? member = holder.Member(name);
            if (member?.Value is { Kind: Kind.Object } entries)
            {
                return Set(entries, entry, value);
            }
            if (value is null)
            {
                return text;
            }
            string one = $"{Quoted(entry)}: {value.ToJsonString(Written)}";
            string written = Lined(holder)
                ? $"{{{eol}{MemberIndent(holder)}{unit}{one}{eol}{MemberIndent(holder)}}}"
                : $"{{ {one} }}";
            return member is null ? Insert(holder, name, written) : Replace(member.Value.Start, member.Value.End, written);
        }

        /// <summary>A member put after the object's last, on a line of its own indented as the others where they have
        /// lines of their own, else on the object's line; the last's trailing comma, if it has one, kept as the new one's.</summary>
        private string Insert(Node holder, string name, string written)
        {
            string member = $"{Quoted(name)}: {written}";
            if (holder.Members.Count == 0)
            {
                return SameLine(text, holder.Start, holder.Close)
                    ? Replace(holder.Start + 1, holder.Close, $" {member} ")
                    : Replace(holder.Start + 1, holder.Start + 1, $"{eol}{IndentOf(text, holder.Start)}{unit}{member}");
            }
            Member last = holder.Members[^1];
            int after = last.Comma >= 0 ? last.Comma + 1 : last.Value.End;
            string comma = last.Comma >= 0 ? string.Empty : ",";
            string trailing = last.Comma >= 0 ? "," : string.Empty;
            return Lined(holder)
                ? Replace(after, after, $"{comma}{eol}{IndentOf(text, last.Start)}{member}{trailing}")
                : Replace(after, after, $"{comma} {member}{trailing}");
        }

        /// <summary>A member taken out with its comma, and where it has a line of its own, with that line; the last without a
        /// comma of its own takes the one before it; the only one leaves the object empty.</summary>
        private string Remove(Node holder, Member member)
        {
            int index = holder.Members.IndexOf(member);
            if (holder.Members.Count == 1)
            {
                return Replace(holder.Start + 1, holder.Close, string.Empty);
            }
            if (member.Comma < 0)
            {
                return Replace(holder.Members[index - 1].Comma, member.Value.End, string.Empty);
            }
            int start = member.Start;
            int end = member.Comma + 1;
            int lineStart = LineStart(text, member.Start);
            if (text.AsSpan(lineStart, member.Start - lineStart).IsWhiteSpace())
            {
                int rest = end;
                while (rest < text.Length && text[rest] is ' ' or '\t')
                {
                    rest++;
                }
                if (rest == text.Length || text[rest] is '\r' or '\n')
                {
                    // Alone on its line: the line goes, its break with it.
                    start = lineStart;
                    end = rest < text.Length && text[rest] == '\r' && rest + 1 < text.Length && text[rest + 1] == '\n' ? rest + 2 : Math.Min(rest + 1, text.Length);
                    return Replace(start, end, string.Empty);
                }
            }
            while (end < text.Length && text[end] is ' ' or '\t')
            {
                end++;
            }
            return Replace(start, end, string.Empty);
        }

        private string Replace(int start, int end, string with) => text = string.Concat(text.AsSpan(0, start), with, text.AsSpan(end));

        /// <summary>Whether an object holds its members on lines of their own (its first is not on its brace's line).</summary>
        private bool Lined(Node holder) => holder.Members.Count > 0 ? !SameLine(text, holder.Start, holder.Members[0].Start) : !SameLine(text, holder.Start, holder.Close);

        /// <summary>Where a member of an object stands: as its others do, else a level in from the object's line.</summary>
        private string MemberIndent(Node holder) => holder.Members.Count > 0 ? IndentOf(text, holder.Members[^1].Start) : IndentOf(text, holder.Start) + unit;

        public static bool SameLine(string text, int from, int to) => text.AsSpan(from, to - from).IndexOfAny('\r', '\n') < 0;

        public static int LineStart(string text, int at)
        {
            int before = text.LastIndexOfAny(['\r', '\n'], Math.Max(0, at - 1));
            return at == 0 || before < 0 ? 0 : before + 1;
        }

        /// <summary>The spaces and tabs a line begins with, the line of the character at an index.</summary>
        public static string IndentOf(string text, int at)
        {
            int start = LineStart(text, at);
            int end = start;
            while (end < text.Length && text[end] is ' ' or '\t')
            {
                end++;
            }
            return text[start..end];
        }

        private static string Quoted(string name) => JsonSerializer.Serialize(name, Written);
    }

    private enum Kind
    {
        Object,
        Array,
        Other,
    }

    /// <summary>A value where the text holds it: from its first character to just past its last, an object's members and
    /// closing brace.</summary>
    private sealed class Node(Kind kind, int start)
    {
        public Kind Kind { get; } = kind;

        public int Start { get; } = start;

        public int End { get; set; }

        public int Close { get; set; }

        public List<Member> Members { get; } = [];

        /// <summary>The last member of that name, the one a reader takes.</summary>
        public Member? Member(string name) => Members.LastOrDefault(m => m.Name == name);
    }

    /// <summary>An object's member: its name, where its name's quote stands, its value and the comma after it (-1 for none).</summary>
    private sealed class Member(string name, int start, Node value)
    {
        public string Name { get; } = name;

        public int Start { get; } = start;

        public Node Value { get; } = value;

        public int Comma { get; set; } = -1;
    }

    /// <summary>JSON as dotnet run reads launchSettings.json (comments and trailing commas allowed), into where each value
    /// stands; a text that does not read is a <see cref="FormatException"/>.</summary>
    private sealed class Reader(string text)
    {
        private int at;

        public Node Document()
        {
            if (text.Length > 0 && text[0] == '﻿')
            {
                at = 1;
            }
            Node root = Value();
            Trivia();
            return at < text.Length ? throw new FormatException($"more follows its value at {at}") : root;
        }

        private Node Value()
        {
            Trivia();
            if (at >= text.Length)
            {
                throw new FormatException("it ends where a value should be");
            }
            return text[at] switch
            {
                '{' => Object(),
                '[' => Array(),
                '"' => String(),
                _ => Literal(),
            };
        }

        private Node Object()
        {
            Node node = new(Kind.Object, at++);
            while (true)
            {
                Trivia();
                if (at >= text.Length)
                {
                    throw new FormatException("an object is not closed");
                }
                if (text[at] == '}')
                {
                    node.Close = at++;
                    node.End = at;
                    return node;
                }
                if (text[at] != '"')
                {
                    throw new FormatException($"a property's name is not quoted at {at}");
                }
                int start = at;
                Node key = String();
                string name;
                try
                {
                    name = JsonSerializer.Deserialize<string>(text.AsSpan(key.Start, key.End - key.Start)) ?? string.Empty;
                }
                catch (JsonException unreadable)
                {
                    throw new FormatException($"a property's name does not read at {start}", unreadable);
                }
                Trivia();
                if (at >= text.Length || text[at] != ':')
                {
                    throw new FormatException($"a property's name is not followed by ':' at {at}");
                }
                at++;
                Member member = new(name, start, Value());
                node.Members.Add(member);
                Trivia();
                if (at < text.Length && text[at] == ',')
                {
                    member.Comma = at++;
                }
                else if (at >= text.Length || text[at] != '}')
                {
                    throw new FormatException($"a property is followed by neither ',' nor '}}' at {at}");
                }
            }
        }

        private Node Array()
        {
            Node node = new(Kind.Array, at++);
            while (true)
            {
                Trivia();
                if (at >= text.Length)
                {
                    throw new FormatException("an array is not closed");
                }
                if (text[at] == ']')
                {
                    node.Close = at++;
                    node.End = at;
                    return node;
                }
                Value();
                Trivia();
                if (at < text.Length && text[at] == ',')
                {
                    at++;
                }
                else if (at >= text.Length || text[at] != ']')
                {
                    throw new FormatException($"an item is followed by neither ',' nor ']' at {at}");
                }
            }
        }

        private Node String()
        {
            Node node = new(Kind.Other, at++);
            while (at < text.Length && text[at] != '"')
            {
                at += text[at] == '\\' ? 2 : 1;
            }
            if (at >= text.Length)
            {
                throw new FormatException("a string is not closed");
            }
            node.End = ++at;
            return node;
        }

        private Node Literal()
        {
            Node node = new(Kind.Other, at);
            while (at < text.Length && !char.IsWhiteSpace(text[at]) && text[at] is not (',' or '}' or ']' or '/'))
            {
                at++;
            }
            if (at == node.Start)
            {
                throw new FormatException($"no value at {at}");
            }
            node.End = at;
            return node;
        }

        /// <summary>Passes over whitespace and comments.</summary>
        private void Trivia()
        {
            while (at < text.Length)
            {
                if (char.IsWhiteSpace(text[at]))
                {
                    at++;
                }
                else if (text[at] == '/' && at + 1 < text.Length && text[at + 1] == '/')
                {
                    while (at < text.Length && text[at] is not ('\r' or '\n'))
                    {
                        at++;
                    }
                }
                else if (text[at] == '/' && at + 1 < text.Length && text[at + 1] == '*')
                {
                    int end = text.IndexOf("*/", at + 2, StringComparison.Ordinal);
                    at = end < 0 ? throw new FormatException("a comment is not closed") : end + 2;
                }
                else
                {
                    return;
                }
            }
        }
    }
}
