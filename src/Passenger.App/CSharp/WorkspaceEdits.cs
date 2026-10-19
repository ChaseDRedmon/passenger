using System.Text.Json;
using Passenger.App.Editors;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App.CSharp;

/// <summary>Applies a workspace edit (a rename, a fix) across files: an open file is edited through its document, one undo
/// step per file and through the editor when it is the one showing; a file not open is edited on disk, keeping its
/// encoding and line endings; files the edit creates, renames or deletes are made so, an open one's tab going with it. An
/// open file's edits are carried through what was typed since the request; one whose edited text changed meanwhile, or
/// that the server answered for another version, is left alone and named in <c>dropped</c>.</summary>
public static class WorkspaceEdits
{
    /// <summary>Every open document followed from now, for a request about to be sent; disposed once its answer is applied.</summary>
    public static Asked Track(EditorArea editors) => new(editors.Documents.ToDictionary(d => d, d => new ChangeTracker(d.Text)));

    /// <summary>The edit applied; the number of files it changed. Throws <see cref="WorkspaceEditFailed"/> when its
    /// file operations cannot all be made, found before anything changed, or when the disk refuses one on the way, an
    /// operation or a file not open being written.</summary>
    public static int Apply(EditorArea editors, JsonElement edit, Asked? asked = null, List<string>? dropped = null)
    {
        using IDisposable? recording = editors.Recording?.Invoke();
        IReadOnlyList<FileEdits> changes = Shapes.WorkspaceEdit(edit);
        if (Refusal(editors, changes) is { } refused)
        {
            throw new WorkspaceEditFailed(refused);
        }
        int files = 0;
        foreach (FileEdits change in changes)
        {
            if (change.Operation is { } operation)
            {
                try
                {
                    Operate(editors, Resource.Read(operation));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    throw new WorkspaceEditFailed(error.Message);
                }
                files++;
                continue;
            }
            string path = DocumentUris.ToPath(change.Uri);
            OpenDocument? open = editors.Documents.FirstOrDefault(d => d.Path == path);
            if (open is not null)
            {
                if (Placed(open, change, asked?.Trackers.GetValueOrDefault(open)) is not { } edits)
                {
                    dropped?.Add(open.Name);
                    continue;
                }
                ApplyTo(editors, open, edits);
            }
            else
            {
                try
                {
                    if (FileText.Load(path) is { } file)
                    {
                        TextSnapshot snapshot = TextSnapshot.From(file.Text);
                        List<TextEdit> edits = Convert(snapshot, change.Edits, TextDocument.DetectLineEnding(file.Text), FileIndent.Of(path, file.Text));
                        FileText.Save(path, snapshot.Apply(edits.OrderBy(e => e.Start).ToList()).ToString(), file.Encoding, file.HasBom, file.CrBreaks);
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    throw new WorkspaceEditFailed($"{Path.GetFileName(path)} could not be written: {error.Message}");
                }
            }
            files++;
        }
        return files;
    }

    /// <summary>The edit applied as one command of <see cref="EditorArea.Commands"/> under a name (a fix all's title): the
    /// files not open that it writes or makes are followed, so Undo takes all of it back at once, as Rider's global undo
    /// does, where they could otherwise come back only from Local History. The number of files changed.</summary>
    public static int ApplyAsCommand(EditorArea editors, JsonElement edit, string name, Asked? asked = null, List<string>? dropped = null)
    {
        using CommandHistory.Recording recording = editors.Commands.Record(name);
        foreach (FileEdits change in Shapes.WorkspaceEdit(edit))
        {
            if (change.Operation is not { } operation)
            {
                recording.Watch(DocumentUris.ToPath(change.Uri));
            }
            else if (Resource.Read(operation) is { Kind: "create" } made)
            {
                recording.Watch(made.Path);
            }
        }
        return Apply(editors, edit, asked, dropped);
    }

    /// <summary>Edits in an open file's text as it is now: through the active editor when it is the file shown there, so
    /// its carets follow as typing's would, else to the document, one undo step.</summary>
    public static void ApplyTo(EditorArea editors, OpenDocument open, IReadOnlyList<TextEdit> edits)
    {
        if (open == editors.Active)
        {
            editors.CodeEditor.ApplyEdits(edits);
            return;
        }
        SelectionSet carets = open.Selections;
        open.Text.BreakUndoGroup();
        open.Text.Edit(edits, carets, TextEditing.MapThrough(carets, edits));
    }

    /// <summary>An open file's edits in its text as it is now: as answered when its text has not changed since, carried
    /// through what changed when it was asked for the version the request was, null when they no longer fit.</summary>
    private static List<TextEdit>? Placed(OpenDocument open, FileEdits change, ChangeTracker? asked)
    {
        string eol = open.Text.LineEnding;
        IndentOptions indent = open.Indentation.Options;
        if (change.Version is { } version && version == open.Text.Version)
        {
            return Convert(open.Text.Snapshot, change.Edits, eol, indent);
        }
        if (asked is null || (change.Version is { } answered && answered != asked.Version))
        {
            return change.Version is null ? Convert(open.Text.Snapshot, change.Edits, eol, indent) : null;
        }
        return Carried(asked, change.Edits, indent) is { } mapped ? [.. mapped] : null;
    }

    /// <summary>A server's edits answered for the text <paramref name="asked"/> began with, in its document's line breaks
    /// and <paramref name="indent"/>, carried onto the text as it is now; null when the code they change was edited meanwhile.</summary>
    public static IReadOnlyList<TextEdit>? Carried(ChangeTracker asked, IReadOnlyList<LspTextEdit> edits, IndentOptions indent) =>
        asked.Map(Convert(asked.Snapshot, edits, asked.Document.LineEnding, indent));

    /// <summary>LSP edits as the editor's, in one snapshot's offsets (its lines as the server counts them), sorted, their
    /// line breaks the file's own <paramref name="eol"/> (<see cref="LineEndings.Fit"/>): Roslyn writes the system's, CRLF
    /// on Windows, into any file; and the indentation they write in the file's own characters
    /// (<see cref="Indentation.Fit(TextSnapshot, IReadOnlyList{TextEdit}, IndentOptions)"/>), which Roslyn may not know
    /// when no .editorconfig gives it. An edit that comes to nothing once fitted is left out.</summary>
    public static List<TextEdit> Convert(TextSnapshot snapshot, IReadOnlyList<LspTextEdit> edits, string eol, IndentOptions indent)
    {
        ServerLines lines = ServerLines.Of(snapshot);
        return Indentation.Fit(snapshot, LineEndings.Fit(snapshot, edits.Select(e =>
        {
            int start = lines.OffsetOf(e.Range.Start);
            int end = lines.OffsetOf(e.Range.End);
            return new TextEdit(start, Math.Max(0, end - start), e.NewText);
        }), eol), indent);
    }

    /// <summary>Why an edit's file operations cannot all be made, found before any is: each is played over what the
    /// ones before it leave. The open files a rename moves are saved now, so one that cannot be stops it here too.</summary>
    private static string? Refusal(EditorArea editors, IReadOnlyList<FileEdits> changes)
    {
        Dictionary<string, bool> there = [];
        bool Exists(string path) => there.TryGetValue(path, out bool known) ? known : File.Exists(path) || Directory.Exists(path);
        foreach (Resource resource in changes.Select(c => c.Operation).OfType<JsonElement>().Select(Resource.Read))
        {
            bool overwrite = resource.Option("overwrite");
            bool ignored = !overwrite && resource.Option("ignoreIfExists");
            // A new name differing only in case is the same file on macOS and Windows, not one already there.
            bool taken = resource.To is { } to && Exists(to) && !string.Equals(to, resource.Path, StringComparison.OrdinalIgnoreCase);
            switch (resource.Kind)
            {
                case "create" when Exists(resource.Path) && !overwrite && !ignored:
                case "rename" when taken && !ignored && (!overwrite || Directory.Exists(resource.To)):
                    return $"{Path.GetFileName(resource.To ?? resource.Path)} is already there";
                case "rename" when !Exists(resource.Path):
                case "delete" when !Exists(resource.Path) && !resource.Option("ignoreIfNotExists"):
                    return $"{Path.GetFileName(resource.Path)} is not there to {resource.Kind}";
                case "create":
                    there[resource.Path] = true;
                    break;
                case "rename" when !(taken && ignored):
                    there[resource.Path] = false;
                    there[resource.To!] = true;
                    foreach (OpenDocument document in editors.Documents.Where(d => d.IsModified && PathNames.Within(d.Path, resource.Path)))
                    {
                        if (!editors.Save(document))
                        {
                            return $"{document.Name} could not be saved before it moved";
                        }
                    }
                    break;
                case "delete":
                    there[resource.Path] = false;
                    break;
            }
        }
        return null;
    }

    private static void Operate(EditorArea editors, Resource resource)
    {
        bool exists = File.Exists(resource.Path) || Directory.Exists(resource.Path);
        switch (resource.Kind)
        {
            case "create" when !exists || resource.Option("overwrite"):
                Directory.CreateDirectory(Path.GetDirectoryName(resource.Path)!);
                File.WriteAllText(resource.Path, string.Empty);
                editors.Documents.FirstOrDefault(d => d.Path == resource.Path)?.Reload(overUnsaved: true);
                break;
            case "rename" when resource.To is { } to:
                if ((File.Exists(to) || Directory.Exists(to)) && !string.Equals(to, resource.Path, StringComparison.OrdinalIgnoreCase))
                {
                    // Ignored, as its options ask: the refusal stopped every other rename onto something there.
                    if (!resource.Option("overwrite") || Directory.Exists(to))
                    {
                        return;
                    }
                    Delete(editors, to, recursive: false);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                if (editors.MoveFile(resource.Path, to) is { } failed)
                {
                    throw new WorkspaceEditFailed(failed);
                }
                break;
            case "delete" when exists:
                Delete(editors, resource.Path, resource.Option("recursive"));
                break;
        }
    }

    /// <summary>Deletes a file or folder, closing the open files that went with it.</summary>
    private static void Delete(EditorArea editors, string path, bool recursive)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive);
        }
        else
        {
            File.Delete(path);
        }
        foreach (OpenDocument document in editors.Documents.Where(d => PathNames.Within(d.Path, path)).ToList())
        {
            editors.CloseDeleted(document);
        }
    }

    /// <summary>One of an edit's file operations (create, rename or delete): its paths and its options.</summary>
    private sealed record Resource(string Kind, string Path, string? To, JsonElement Options)
    {
        public static Resource Read(JsonElement operation)
        {
            string kind = operation.GetProperty("kind").GetString() ?? string.Empty;
            string path = DocumentUris.ToPath(operation.GetProperty(kind == "rename" ? "oldUri" : "uri").GetString()!);
            string? to = kind == "rename" ? DocumentUris.ToPath(operation.GetProperty("newUri").GetString()!) : null;
            return new Resource(kind, path, to, operation.TryGetProperty("options", out JsonElement options) ? options : default);
        }

        public bool Option(string name) =>
            Options.ValueKind == JsonValueKind.Object && Options.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }
}

/// <summary>A workspace edit that could not be made, and why: a file it renames onto is there already, one it renames
/// or deletes is not, an open file it moves could not be saved first, or the disk refused. An MCP tool says it as its
/// error.</summary>
public sealed class WorkspaceEditFailed(string message) : InvalidOperationException(message);

/// <summary>The open documents followed from a request's sending until its answer is applied.</summary>
public sealed class Asked(Dictionary<OpenDocument, ChangeTracker> trackers) : IDisposable
{
    public IReadOnlyDictionary<OpenDocument, ChangeTracker> Trackers => trackers;

    public void Dispose()
    {
        foreach (ChangeTracker tracker in trackers.Values)
        {
            tracker.Dispose();
        }
    }
}
