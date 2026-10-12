using System.Text.Json;
using Passenger.App.Editors;
using Passenger.App.Workspace;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App.CSharp;

/// <summary>Applies a workspace edit (a rename, a fix) across files: a file open in a tab is edited
/// through its document, one undo step per file and through the editor when it is the one showing;
/// a file not open is edited on disk, keeping its encoding and line endings; files the edit creates,
/// renames or deletes are made so.</summary>
public static class WorkspaceEdits
{
    public static int Apply(EditorArea editors, JsonElement edit)
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
                List<TextEdit> edits = Convert(open.Text.Snapshot, change.Edits);
                if (open == editors.Active)
                {
                    editors.CodeEditor.ApplyEdits(edits);
                }
                else
                {
                    SelectionSet carets = open.Selections;
                    open.Text.BreakUndoGroup();
                    open.Text.Edit(edits, carets, TextEditing.MapThrough(carets, edits));
                    open.Selections = TextEditing.MapThrough(carets, edits);
                }
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
