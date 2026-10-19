using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Passenger.App.History;
using Passenger.App.Run;
using Passenger.App.Settings;
using Passenger.App.State;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Mcp;

/// <summary>The IDE's own windows for an agent: Settings through the settings store as the dialog's OK changes them, Find in
/// Files over the window's scopes and masks, Local History's versions and its revert, and what the Problems and Build windows
/// list beyond a file's diagnostics and a build's answer.</summary>
public sealed partial class PassengerTools
{
    /// <summary>How long a search waits for the folder's files to be walked, as the folder opens.</summary>
    private static readonly TimeSpan IndexLimit = TimeSpan.FromSeconds(60);

    /// <summary>How long a look at Local History waits for what has happened to be recorded.</summary>
    private static readonly TimeSpan HistoryLimit = TimeSpan.FromSeconds(20);

    // Settings

    [McpServerTool(Name = "settings", Title = "The IDE's settings", ReadOnly = true), Description(
        "Every setting the Settings dialog has, by its name in settings.json (editorFontSize, theme, hotReload, solutionWideAnalysis, " +
        "showBuildResults...), with its value now, its kind, the choices one of several takes and the range a number is brought into. kept " +
        "says whether they are the person's (their window's, kept in settings.json and followed by their other windows) or, in a windowless " +
        "--mcp Passenger, its own defaults, kept nowhere. settings_set changes them.")]
    public Task<SettingsInfo> GetSettings(CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        JsonObject values = SettingsNode(window.Settings);
        return Task.FromResult(new SettingsInfo(window.SettingsKept, [.. SettingProperties.Select(p =>
        {
            Type type = Nullable.GetUnderlyingType(p.Value.PropertyType) ?? p.Value.PropertyType;
            (double Min, double Max)? range = SettingRange(p.Key, p.Value);
            return new SettingInfo(p.Key, JsonSerializer.SerializeToElement(values[p.Key]), SettingKind(p.Value),
                type.IsEnum ? Enum.GetNames(type) : null, range?.Min, range?.Max);
        })]));
    });

    [McpServerTool(Name = "settings_set", Title = "Change settings", Destructive = true), Description(
        "Changes settings as the Settings dialog's OK does, each by its name in settings.json (the settings tool lists them) to a JSON value " +
        "of its kind: true or false, a number, text (null for none), one of its choices by name, or for an object (xamlFormat, shortcuts) " +
        "the members to change. A number is brought into its range as Settings brings one, and the answer says so; a name Settings lacks, " +
        "a value of another kind or a choice it does not offer is refused, and then nothing changes. On a person's window the change is " +
        "theirs, kept in settings.json and followed by their other windows at once; Enable MCP Server and the folders agents join " +
        "unasked (agentFolders) stay theirs alone to change. Returns " +
        "each setting named, as it was and as it is now in force.")]
    public Task<IReadOnlyList<SettingChange>> SetSettings(
        [Description("Each setting to change by its name, with its new value: {\"editorFontSize\": 15, \"theme\": \"Dark\"}")] Dictionary<string, JsonElement> changes,
        CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        if (changes is not { Count: > 0 })
        {
            throw new ArgumentException("Name at least one setting to change, with its new value; settings lists them");
        }
        AppSettings before = window.Settings;
        JsonObject was = SettingsNode(before);
        JsonObject next = SettingsNode(before);
        List<string> named = [];
        foreach ((string given, JsonElement value) in changes)
        {
            string name = SettingProperties.Keys.FirstOrDefault(k => k == given) ?? SettingProperties.Keys.FirstOrDefault(k => k.Equals(given, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Settings has no setting {given}; the settings tool lists those it has");
            PropertyInfo property = SettingProperties[name];
            if (property.Name == nameof(AppSettings.McpServer))
            {
                throw new InvalidOperationException("Enable MCP Server is the person's to change in Settings, never an agent's");
            }
            if (property.Name == nameof(AppSettings.AgentFolders))
            {
                throw new InvalidOperationException("The folders agents join unasked are the person's to change, by the window's question and in Settings, never an agent's");
            }
            next[name] = SettingValue(name, property, next[name], value);
            named.Add(name);
        }
        AppSettings asked = SettingsOf(next);
        window.ApplySettings(asked);
        JsonObject now = SettingsNode(window.Settings);
        JsonObject wanted = SettingsNode(asked);
        return Task.FromResult<IReadOnlyList<SettingChange>>([.. named.Distinct().Select(name =>
        {
            (double Min, double Max)? range = SettingRange(name, SettingProperties[name]);
            string? note = JsonNode.DeepEquals(now[name], wanted[name]) ? null
                : range is { } bounds ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"brought into its range, {bounds.Min} to {bounds.Max}") : "set back to what Settings allows";
            return new SettingChange(name, JsonSerializer.SerializeToElement(was[name]), JsonSerializer.SerializeToElement(now[name]), note);
        })]);
    });

    /// <summary>Every setting by its name in settings.json, as the settings store writes them.</summary>
    private static readonly IReadOnlyDictionary<string, PropertyInfo> SettingProperties = typeof(AppSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
        .ToDictionary(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name), p => p, StringComparer.Ordinal);

    private static JsonObject SettingsNode(AppSettings settings) => (JsonObject)JsonSerializer.SerializeToNode(settings, StateFile.Options)!;

    private static AppSettings SettingsOf(JsonObject node) => node.Deserialize<AppSettings>(StateFile.Options) ?? new AppSettings();

    /// <summary>A setting's kind as an agent writes its value.</summary>
    private static string SettingKind(PropertyInfo property)
    {
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        return type == typeof(bool) ? "boolean"
            : type == typeof(int) ? "integer"
            : type == typeof(double) ? "number"
            : type == typeof(string) ? "text"
            : type.IsEnum ? "choice"
            : "object";
    }

    /// <summary>The range Settings brings a number into, found by asking <see cref="AppSettings.Clamped"/> itself; null for a
    /// setting it leaves be.</summary>
    private static (double Min, double Max)? SettingRange(string name, PropertyInfo property)
    {
        if (property.PropertyType != typeof(int) && property.PropertyType != typeof(double))
        {
            return null;
        }
        double low = Probe(int.MinValue);
        double high = Probe(int.MaxValue);
        return low == int.MinValue && high == int.MaxValue ? null : (low, high);

        double Probe(int value)
        {
            JsonObject node = SettingsNode(new AppSettings());
            node[name] = value;
            return Convert.ToDouble(property.GetValue(SettingsOf(node).Clamped()), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>An agent's value for a setting as settings.json holds it: a choice by its name, an object's members over those
    /// it has; one of another kind refused, saying the kind.</summary>
    private static JsonNode? SettingValue(string name, PropertyInfo property, JsonNode? current, JsonElement value)
    {
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        JsonNode? given = JsonNode.Parse(value.GetRawText());
        if (type.IsEnum)
        {
            // An unknown name would read as no value, which Clamped puts back to the default without a word.
            return value.ValueKind == JsonValueKind.String && Enum.TryParse(type, value.GetString(), ignoreCase: true, out object? chosen) && Enum.IsDefined(type, chosen!)
                ? JsonValue.Create(Enum.GetName(type, chosen!))
                : throw new ArgumentException($"{name} is one of {string.Join(", ", Enum.GetNames(type))}, not {value.GetRawText()}");
        }
        if (current is JsonObject members && given is JsonObject changed)
        {
            JsonObject merged = (JsonObject)members.DeepClone();
            foreach ((string member, JsonNode? to) in changed)
            {
                merged[member] = to?.DeepClone();
            }
            given = merged;
        }
        try
        {
            if (given is null && property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null)
            {
                throw new JsonException();
            }
            _ = given.Deserialize(property.PropertyType, StateFile.Options);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or NotSupportedException)
        {
            throw new ArgumentException($"{name} takes {(SettingKind(property) is "integer" or "object" ? "an" : "a")} {SettingKind(property)}, not {value.GetRawText()}", error);
        }
        return given;
    }

    // Find in Files

    [McpServerTool(Name = "find_in_files", Title = "Find in Files", ReadOnly = true), Description(
        "Every match of text across the folder's files, as Find in Files (Ctrl+Shift+F) finds them: an open file as the editor has it, unsaved " +
        "changes and all, every other text file of the folder on disk (build output, version control, binary and very large files left out), " +
        "at most 1000 (capped says so). matchCase, wholeWords and regex are its options; scope its scopes: solution (every file of the folder, " +
        "the default), project (the project named), directory (the directory named, with its subdirectories unless recursive is false), " +
        "openFiles, currentFile (the file the editor shows) or recentlyViewed; mask its file mask (\"*.cs\", \"*.cs, !*.g.cs\"). Each match " +
        "with its line's text. For a symbol's uses by meaning rather than by text, usages.")]
    public Task<FindInFilesResult> FindInFiles(string text, bool matchCase = false, bool wholeWords = false, bool regex = false,
        [Description("solution, project, directory, openFiles, currentFile or recentlyViewed")] string scope = "solution",
        [Description("For the project scope, the project's name or .csproj")] string? project = null,
        [Description("For the directory scope, the directory, from the open folder unless rooted")] string? directory = null,
        bool recursive = true,
        [Description("Rider's file mask: names by * and ?, separated by commas, a ! before one leaving its files out")] string? mask = null,
        [Description("How many matches to return")] int limit = 200, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (SearchQuery query, SearchScope chosen) = Search(text, matchCase, wholeWords, regex, scope, project, directory, recursive, mask);
        await Until(() => !Session.Index.IsWalking, IndexLimit, cancel);
        IReadOnlyList<SearchMatch> found = await window.FindInFilesAsync(query, chosen, cancel);
        return new FindInFilesResult(found.Count, found.Count >= FileSearch.MostMatches,
            [.. found.Take(Math.Clamp(limit, 1, FileSearch.MostMatches)).Select(m => new Place(Relative(m.Path), m.Line + 1, m.Column + 1, m.LineText.Trim()))]);
    });

    [McpServerTool(Name = "replace_in_files", Title = "Replace in Files", Destructive = true), Description(
        "Replaces every match find_in_files finds (the same text, options, scope and mask) with replacement, as Replace in Files' Replace All " +
        "does: an open file's matches as one undoable step, then saved; a closed one written in its own encoding (or UTF-8 where that cannot " +
        "hold the replacement, which the answer says). A regex's replacement takes its groups ($1, ${name}). Returns how many matches in " +
        "which files.")]
    public Task<string> ReplaceInFiles(string text, string replacement, bool matchCase = false, bool wholeWords = false, bool regex = false,
        [Description("solution, project, directory, openFiles, currentFile or recentlyViewed")] string scope = "solution",
        [Description("For the project scope, the project's name or .csproj")] string? project = null,
        [Description("For the directory scope, the directory, from the open folder unless rooted")] string? directory = null,
        bool recursive = true,
        [Description("Rider's file mask: names by * and ?, separated by commas, a ! before one leaving its files out")] string? mask = null,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (SearchQuery query, SearchScope chosen) = Search(text, matchCase, wholeWords, regex, scope, project, directory, recursive, mask);
        await Until(() => !Session.Index.IsWalking, IndexLimit, cancel);
        IReadOnlyList<SearchMatch> found = await window.FindInFilesAsync(query, chosen, cancel);
        if (found.Count == 0)
        {
            return "Nothing matches, so nothing was replaced";
        }
        if (found.Count >= FileSearch.MostMatches)
        {
            throw new InvalidOperationException($"More than {FileSearch.MostMatches - 1} matches: narrow the scope or the mask, as Replace in Files lists no more");
        }
        List<string> files = [.. found.Select(m => m.Path).Distinct()];
        List<string> said = [.. window.ReplaceInFiles(query, replacement, files)];
        foreach (OpenDocument open in Editors.Documents.Where(d => d.IsModified && files.Contains(d.Path)).ToList())
        {
            if (!Editors.Save(open))
            {
                said.Add($"{Relative(open.Path)} is changed in the editor but could not be saved");
            }
        }
        string replaced = $"Replaced {found.Count} match{(found.Count == 1 ? "" : "es")} in {files.Count} file{(files.Count == 1 ? "" : "s")}: {string.Join(", ", files.Select(Relative))}";
        return said.Count == 0 ? replaced : $"{replaced}; {string.Join("; ", said)}";
    });

    [McpServerTool(Name = "edit", Title = "Edit a file", Destructive = true), Description(
        "Replaces text in one file as the editor does: oldText exactly as the file has it (case, spaces and indentation included; several " +
        "lines with \\n between them), found once, or every time with all true, becomes newText (empty deletes it). The file is opened in " +
        "the editor if it is not, the change is one step of its Undo and kept by Local History, and the file is saved. Returns where it " +
        "changed and those lines as they are now, numbered as editor_text numbers them. Text that is not there, or is there more than " +
        "once without all, changes nothing and says so. For a new file, write it on disk; for a symbol's every use, rename.")]
    public Task<string> Edit(
        [Description("Absolute, or the open folder's")] string path,
        [Description("The text to replace, exactly as the file has it")] string oldText,
        [Description("What takes its place")] string newText,
        [Description("Replace every time oldText is there, rather than refusing when it is there more than once")] bool all = false,
        CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        if (string.IsNullOrEmpty(oldText))
        {
            throw new ArgumentException("Give oldText, the text to replace as the file has it; to add text, give the line before it as oldText and that line with the new text after it as newText");
        }
        OpenDocument document = Document(path);
        TextSnapshot text = document.Text.Snapshot;
        string eol = document.Text.LineEnding;
        string needle = LineEndings.In(oldText, eol);
        List<int> found = [.. text.Occurrences(needle)];
        string name = Relative(document.Path);
        if (found.Count == 0)
        {
            throw new InvalidOperationException($"{name} has no \"{Shorter(oldText)}\" as written (case, spaces and line breaks included), so nothing changed; editor_text shows the file as the editor has it");
        }
        if (found.Count > 1 && !all)
        {
            int[] lines = [.. found.Select(o => text.GetLineFromOffset(o) + 1).Distinct()];
            throw new InvalidOperationException($"{name} has \"{Shorter(oldText)}\" {found.Count} times (lines {string.Join(", ", lines.Take(10))}{(lines.Length > 10 ? ", …" : "")}), so nothing changed: give more of the text around the one to change, or all true for every one");
        }
        string written = LineEndings.In(newText ?? string.Empty, eol);
        List<TextEdit> edits = [.. found.Select(o => new TextEdit(o, needle.Length, written))];
        document.Text.BreakUndoGroup();
        document.Text.Edit(edits, document.Selections, TextEditing.MapThrough(document.Selections, edits));
        document.Text.BreakUndoGroup();
        string saved = Editors.Save(document) ? "saved" : FileText.IsReadOnly(document.Path) ? "not saved: the file is read-only" : "not saved: the system refused";
        // Each edit's lines as they are now: offsets move by what the edits before it added.
        TextSnapshot after = document.Text.Snapshot;
        List<(int First, int Last)> spans = [];
        int moved = 0;
        foreach (TextEdit edit in edits)
        {
            int start = edit.Start + moved;
            moved += written.Length - needle.Length;
            int first = after.GetLineFromOffset(start);
            int last = after.GetLineFromOffset(Math.Min(after.Length, start + written.Length));
            spans.Add((first, last));
        }
        System.Text.StringBuilder said = new($"Changed {name} {(edits.Count == 1 ? $"at line {spans[0].First + 1}" : $"in {edits.Count} places")}, {saved}:");
        int shown = 0;
        int lastShown = -1;
        foreach ((int first, int last) in spans)
        {
            for (int line = Math.Max(first, lastShown + 1); line <= last && shown < MostEditLines; line++, shown++)
            {
                said.Append('\n').Append($"{line + 1,6}\t{after.GetLineText(line)}");
                lastShown = line;
            }
        }
        if (shown >= MostEditLines)
        {
            said.Append("\n…");
        }
        return Task.FromResult(said.ToString());
    });

    /// <summary>How many changed lines edit shows back.</summary>
    private const int MostEditLines = 40;

    private static string Shorter(string text) => text.Length > 120 ? text[..120] + "…" : text;

    /// <summary>Find in Files' query and scope as an agent names them, refused as the popup refuses them.</summary>
    private (SearchQuery Query, SearchScope Scope) Search(string text, bool matchCase, bool wholeWords, bool regex, string scope, string? project, string? directory,
        bool recursive, string? mask)
    {
        if (Session.Folder is null)
        {
            throw new InvalidOperationException("No folder is open");
        }
        SearchQuery query = new(text ?? string.Empty, matchCase, wholeWords, regex);
        if (query.IsEmpty)
        {
            throw new ArgumentException("Give the text to find");
        }
        if (FileSearch.Problem(query) is { } wrong)
        {
            throw new ArgumentException(wrong);
        }
        SearchScope chosen = scope switch
        {
            "solution" => new SearchScope(SearchScopeKind.Solution, null, null, recursive, null, mask),
            "project" => new SearchScope(SearchScopeKind.Project, Project(project ?? throw new ArgumentException("The project scope names its project")).Path, null, recursive, null, mask),
            "directory" => new SearchScope(SearchScopeKind.Directory, null, Resolve(directory ?? throw new ArgumentException("The directory scope names its directory")), recursive, null, mask),
            "openFiles" => new SearchScope(SearchScopeKind.Scope, null, null, recursive, MainWindow.OpenFilesScope, mask),
            "currentFile" => new SearchScope(SearchScopeKind.Scope, null, null, recursive, MainWindow.CurrentFileScope, mask),
            "recentlyViewed" => new SearchScope(SearchScopeKind.Scope, null, null, recursive, MainWindow.RecentlyViewedScope, mask),
            _ => throw new ArgumentException($"No scope '{scope}'; there are solution, project, directory, openFiles, currentFile and recentlyViewed"),
        };
        return chosen.Problem() is { } problem ? throw new ArgumentException(problem) : (query, chosen);
    }

    // Local History

    [McpServerTool(Name = "local_history", Title = "Local History", ReadOnly = true), Description(
        "The Local History of a file or folder (the open folder when none is named), as its window lists it: each change set that touched it, " +
        "the newest first, with its set number, time, title (Change, Create, Delete, Rename, External change, a refactoring's or Git " +
        "operation's name, a label's) and the files it touched. Every save, change made elsewhere, delete and move of the folder's text files " +
        "is kept, for 5 days of activity. With set, what was there before that change set, which local_history_revert puts back: a file's " +
        "text then (exists false where it did not exist), or the folder's files that are not now what they were then.")]
    public Task<LocalHistoryInfo> LocalHistoryOf(string? path = null, [Description("A change set's number, as listed")] long? set = null, CancellationToken cancel = default) =>
        OnUi(cancel, async () =>
    {
        (History.LocalHistory history, string full) = HistoryOf(path);
        await Until(() => !history.IsRecording, HistoryLimit, cancel);
        (IReadOnlyList<HistoryEvent> events, IReadOnlyDictionary<string, string?> known) = history.Snapshot();
        bool isFolder = Directory.Exists(full);
        IReadOnlyList<HistoryRow> rows = await BackgroundWork.Run(() => isFolder ? HistoryRows.OfFolder(events, full) : HistoryRows.OfFile(events, known, full), cancel);
        if (set is not long chosen)
        {
            return new LocalHistoryInfo(Relative(full), [.. rows.Select(Row)]);
        }
        HistoryRow row = rows.FirstOrDefault(r => r.Set == chosen)
            ?? throw new ArgumentException($"No change set {chosen} is in the history of {Relative(full)}; local_history lists those there are");
        if (isFolder)
        {
            IReadOnlyList<HistoryDifference> since = await BackgroundWork.Run(() => HistoryRows.Since(events, known, chosen, full), cancel);
            return new LocalHistoryInfo(Relative(full), [Row(row)])
            {
                Files = [.. since.Select(f => new VcsChange(Relative(f.Path), f.Kind.ToString().ToLowerInvariant(), null))],
            };
        }
        if (row.Version is not { } version)
        {
            return new LocalHistoryInfo(Relative(full), [Row(row)]) { Exists = false };
        }
        byte[] bytes = await BackgroundWork.Run(() => history.Content(version), cancel)
            ?? throw new InvalidOperationException($"What {Relative(full)} held before change set {chosen} is no longer kept");
        return new LocalHistoryInfo(Relative(full), [Row(row)]) { Exists = true, Text = FileText.FromBytes(bytes)?.Text ?? string.Empty };

        HistoryRowInfo Row(HistoryRow r) => new(r.Set, r.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            r.Title, r.IsLabel, [.. r.Files.Select(Relative)]);
    });

    [McpServerTool(Name = "local_history_revert", Title = "Revert to a Local History version", Destructive = true), Description(
        "Puts a file or folder back as it was before a change set of its Local History (local_history lists them), as Revert Selected and " +
        "Later Changes does: an open file's text as one undoable step, then saved; a closed one's bytes as they were; a file that did not " +
        "exist then deleted. The revert is kept in the history in its turn, so it can be reverted too.")]
    public Task<string> LocalHistoryRevert(string path, [Description("The change set's number, as local_history lists it")] long set, CancellationToken cancel = default) =>
        OnUi(cancel, async () =>
    {
        (History.LocalHistory history, string full) = HistoryOf(path);
        await Until(() => !history.IsRecording, HistoryLimit, cancel);
        if (await window.RevertLocalHistoryAsync(full, set) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }
        return $"{Relative(full)} is as it was before change set {set}";
    });

    [McpServerTool(Name = "local_history_label", Title = "Put a Local History label", Destructive = false), Description(
        "Puts a label across the folder's Local History now, as Put Label does: a point local_history lists by its name, to come back to " +
        "with local_history_revert (the label's set) once what follows it is not wanted.")]
    public Task<string> LocalHistoryLabel(string name, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (History.LocalHistory history, _) = HistoryOf(null);
        string label = name.Trim();
        if (label.Length == 0)
        {
            throw new ArgumentException("A label needs a name");
        }
        history.Label(label);
        await Until(() => !history.IsRecording, HistoryLimit, cancel);
        return $"Label {label} put";
    });

    /// <summary>The open folder's Local History and a path in the folder an agent named (the folder itself when none).</summary>
    private (History.LocalHistory History, string Path) HistoryOf(string? path)
    {
        if (Session.LocalHistory is not { } history || Session.Folder is not { } folder)
        {
            throw new InvalidOperationException("No folder is open");
        }
        string full = path is null ? folder : Resolve(path);
        return PathNames.Within(full, folder) ? (history, full) : throw new ArgumentException($"{full} is not in the open folder, whose Local History this is");
    }

    // Problems and the Build window

    [McpServerTool(Name = "problems", Title = "The Problems window", ReadOnly = true), Description(
        "What the Problems window lists beyond one file's diagnostics: toolset (Toolset, Environment: each project's load warnings and " +
        "errors, as the language server loaded it), build (the last build's errors and warnings, whoever started it: Build, the build " +
        "before Run, Debug or Profile, or a test run's), and solution (every file's errors and warnings across the solution, with " +
        "solution-wide analysis on: settings_set solutionWideAnalysis true turns it on; while it is off, solution is left out). A file's own are " +
        "diagnostics', a failed binding's app_binding_errors', a Hot Reload's hot_reload's.")]
    public Task<ProblemsInfo> Problems(CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        if (Session.CSharp is { SolutionWide: true })
        {
            // Its first answers come once the server has loaded the solution and analysed it.
            await Server(cancel);
            await Until(() => Session.CSharp is not { IsAnalysing: true }, ServerLimit, cancel);
        }
        ToolWindows.ProblemsView view = window.BottomPanel.Problems;
        return new ProblemsInfo([.. view.ToolsetProblems.Select(Found)], [.. view.Problems.Select(Found)],
            Session.CSharp?.SolutionProblems is { } solution ? [.. solution.Select(p => new Finding(p.Diagnostic.Severity == 1 ? "error" : "warning", p.Diagnostic.Code?.ToString(),
                p.Diagnostic.Message, Relative(p.Path), p.Diagnostic.Range.Start.Line + 1, p.Diagnostic.Range.Start.Character + 1))] : null);

        Finding Found(Problem p) => new(p.Severity == ProblemSeverity.Error ? "error" : "warning", p.Code, p.Message,
            p.Path is { } at ? Relative(at) : p.Project, p.Position.Line + 1, p.Position.Column + 1);
    });

    [McpServerTool(Name = "build_output", Title = "What the Build window shows", ReadOnly = true), Description(
        "The Build window's console, the last lines: the last build's MSBuild output, whoever started it (Build, Rebuild or Clean through " +
        "ide_action, the build before Run, Debug or Profile, a test run's), as it stands now, while it runs too.")]
    public Task<string> BuildOutput(int lines = 200, CancellationToken cancel = default) => OnUi(cancel, () => Task.FromResult(Tail(Session.Builder.Shown(), Math.Max(1, lines))));
}

/// <summary>The settings as an agent reads them: whether they are the person's, and each setting.</summary>
public sealed record SettingsInfo(bool Kept, IReadOnlyList<SettingInfo> Settings);

/// <summary>A setting by its name in settings.json: its value, its kind, the choices it takes or the range it is brought into.</summary>
public sealed record SettingInfo(string Name, JsonElement Value, string Kind, IReadOnlyList<string>? Choices, double? Min, double? Max);

/// <summary>A setting an agent changed: as it was, as it is now in force, and why that is not what was asked, when it is not.</summary>
public sealed record SettingChange(string Name, JsonElement Before, JsonElement After, string? Note);

/// <summary>Find in Files' matches: how many it found (at most 1000, capped then), and those returned.</summary>
public sealed record FindInFilesResult(int Count, bool Capped, IReadOnlyList<Place> Matches);

/// <summary>A file's or folder's Local History; with a change set chosen, what was there before it.</summary>
public sealed record LocalHistoryInfo(string Path, IReadOnlyList<HistoryRowInfo> Rows)
{
    /// <summary>A file's text before the set.</summary>
    public string? Text { get; init; }

    /// <summary>Whether the file existed before the set.</summary>
    public bool? Exists { get; init; }

    /// <summary>A folder's files that are not now what they were before the set: added, deleted or modified since.</summary>
    public IReadOnlyList<VcsChange>? Files { get; init; }
}

/// <summary>A change set of Local History as its window lists it.</summary>
public sealed record HistoryRowInfo(long Set, string Time, string Title, bool Label, IReadOnlyList<string> Files);

/// <summary>What the Problems window lists beyond a file's diagnostics: the projects' load problems, the last build's, and the
/// solution's with solution-wide analysis on (null while it is off).</summary>
public sealed record ProblemsInfo(IReadOnlyList<Finding> Toolset, IReadOnlyList<Finding> Build, IReadOnlyList<Finding>? Solution);
