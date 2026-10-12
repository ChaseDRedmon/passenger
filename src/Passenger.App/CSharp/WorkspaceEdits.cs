using System.Text.Json;
using Passenger.App.Editors;
using Passenger.App.Workspace;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App.CSharp;

/// <summary>Applies a workspace edit (a rename, a fix) across files: a file open in a tab is edited
/// through its document, one undo step per file and through the editor when it is the one showing;
/// a file not open is edited on disk, keeping its encoding and line endings; files the edit creates,
/// renames or deletes are made so. An open file's edits were made for its text when the request went
/// out: they are carried through what was typed since, and a file whose edited text changed meanwhile,
/// or that the server answered for another version, is left alone and named in <paramref name="dropped"/>.</summary>
public static class WorkspaceEdits
{
    /// <summary>Every open document followed from now, for a request about to be sent; disposed once its answer is applied.</summary>
    public static Asked Track(EditorArea editors) => new(editors.Documents.ToDictionary(d => d.Path, d => new ChangeTracker(d.Text)));

    public static int Apply(EditorArea editors, JsonElement edit, Asked? asked = null, List<string>? dropped = null)
    {
        int files = 0;
        foreach (FileEdits change in Shapes.WorkspaceEdit(edit))
        {
            if (change.Operation is { } operation)
            {
                Operate(operation);
                files++;
                continue;
            }
            string path = new Uri(change.Uri).LocalPath;
            OpenDocument? open = editors.Documents.FirstOrDefault(d => d.Path == path);
            if (open is not null)
            {
                if (Placed(open, change, asked?.Trackers.GetValueOrDefault(open.Path)) is not { } edits)
                {
                    dropped?.Add(open.Name);
                    continue;
                }
                ApplyTo(editors, open, edits);
            }
            else if (FileText.Load(path) is { } file)
            {
                TextSnapshot snapshot = TextSnapshot.From(file.Text);
                List<TextEdit> edits = Convert(snapshot, change.Edits);
                FileText.Save(path, snapshot.Apply(edits.OrderBy(e => e.Start).ToList()).ToString(), file.Encoding, file.HasBom);
            }
            files++;
        }
        return files;
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
        if (change.Version is { } version && version == open.Text.Version)
        {
            return Convert(open.Text.Snapshot, change.Edits);
        }
        if (asked is null || (change.Version is { } answered && answered != asked.Version))
        {
            return change.Version is null ? Convert(open.Text.Snapshot, change.Edits) : null;
        }
        return asked.Map(Convert(asked.Snapshot, change.Edits)) is { } mapped ? [.. mapped] : null;
    }

    /// <summary>LSP edits as the editor's, in one snapshot's offsets, sorted.</summary>
    public static List<TextEdit> Convert(TextSnapshot snapshot, IReadOnlyList<LspTextEdit> edits) => edits
        .Select(e =>
        {
            int start = snapshot.GetOffset(new TextPosition(e.Range.Start.Line, e.Range.Start.Character));
            int end = snapshot.GetOffset(new TextPosition(e.Range.End.Line, e.Range.End.Character));
            return new TextEdit(start, Math.Max(0, end - start), e.NewText);
        })
        .OrderBy(e => e.Start)
        .ToList();

    private static void Operate(JsonElement operation)
    {
        string kind = operation.GetProperty("kind").GetString() ?? string.Empty;
        switch (kind)
        {
            case "create":
                string created = new Uri(operation.GetProperty("uri").GetString()!).LocalPath;
                Directory.CreateDirectory(Path.GetDirectoryName(created)!);
                if (!File.Exists(created))
                {
                    File.WriteAllText(created, string.Empty);
                }
                break;
            case "rename":
                File.Move(new Uri(operation.GetProperty("oldUri").GetString()!).LocalPath, new Uri(operation.GetProperty("newUri").GetString()!).LocalPath);
                break;
            case "delete":
                string deleted = new Uri(operation.GetProperty("uri").GetString()!).LocalPath;
                if (File.Exists(deleted))
                {
                    File.Delete(deleted);
                }
                break;
        }
    }
}

/// <summary>The open documents followed from a request's sending until its answer is applied.</summary>
public sealed class Asked(Dictionary<string, ChangeTracker> trackers) : IDisposable
{
    public IReadOnlyDictionary<string, ChangeTracker> Trackers => trackers;

    public void Dispose()
    {
        foreach (ChangeTracker tracker in trackers.Values)
        {
            tracker.Dispose();
        }
    }
}
