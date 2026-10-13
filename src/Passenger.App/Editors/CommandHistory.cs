using System.Text;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Editors;

/// <summary>Rider's undo of a command that changed more than one file or moved one, as a rename does: Undo where the
/// command is the last change (an editor of one of its files, or the Solution view) takes all of it back at once, the
/// files it moved going back and every file's text as it was, and Redo makes all of it again, each only while every file
/// is still as the command left it. A command that changed one file's text alone is that file's own undo step.</summary>
public sealed class CommandHistory
{
    /// <summary>The most commands kept, the oldest going first, as IntelliJ's registry has undo.globalUndoLimit.</summary>
    public const int Limit = 10;

    private readonly EditorArea editors;
    private readonly List<Command> done = [];
    private readonly List<Command> undone = [];

    internal CommandHistory(EditorArea editors) => this.editors = editors;

    /// <summary>Follows what a command changes from now until the recording is disposed: the open files' text, the files
    /// not open it is told it writes (<see cref="Recording.Watch"/>) and the files moved. Kept as a command of its own
    /// when it changed more than one file or moved one.</summary>
    public Recording Record(string name) => new(this, editors, name);

    /// <summary>The command Undo takes back there: in a file's editor, the one whose change is that file's next undo
    /// step; in the Solution view (no file), the latest. Null when Undo is the file's own.</summary>
    public Command? ToUndo(OpenDocument? document) => Find(done, document, redo: false);

    /// <summary>The command Redo makes again there, as <see cref="ToUndo"/> finds it.</summary>
    public Command? ToRedo(OpenDocument? document) => Find(undone, document, redo: true);

    /// <summary>Forgets every command, as a folder closing does.</summary>
    public void Clear()
    {
        done.Clear();
        undone.Clear();
    }

    /// <summary>Why a command cannot be taken back (or made again) now, naming the first file changed or moved since, as
    /// Rider says the files affected that have been changed; null when it can.</summary>
    public string? Blocked(Command command, bool redo)
    {
        foreach ((string from, string to) in command.Moves)
        {
            (string there, string free) = redo ? (from, to) : (to, from);
            if (!Exists(there))
            {
                return $"{Path.GetFileName(there)} is no longer there";
            }
            if (Exists(free) && !string.Equals(free, there, StringComparison.OrdinalIgnoreCase))
            {
                return $"{Path.GetFileName(free)} is already there";
            }
        }
        foreach (TextChange text in command.Texts)
        {
            if (IsOpen(text.Document))
            {
                if ((redo ? text.Document!.Text.NextRedo : text.Document!.Text.NextUndo) != text.Step)
                {
                    return $"{text.Document.Name} has been changed since";
                }
            }
            else
            {
                string at = redo ? text.Path : command.Moved(text.Path);
                string expected = (redo ? text.Before : text.After).ToString();
                if (!File.Exists(at) || FileText.Load(at)?.Text != expected)
                {
                    return $"{Path.GetFileName(at)} has been changed since";
                }
            }
        }
        return null;
    }

    /// <summary>Takes a command back: the files it moved go back, then every file's text is as it was. Null once done,
    /// else why not, from the disk.</summary>
    public string? Undo(Command command)
    {
        foreach ((string from, string to) in command.Moves.Reverse())
        {
            if (editors.MoveFile(to, from) is { } failed)
            {
                return failed;
            }
        }
        foreach (TextChange text in command.Texts)
        {
            if (Put(text, redo: false) is { } failed)
            {
                return failed;
            }
        }
        done.Remove(command);
        undone.Add(command);
        return null;
    }

    /// <summary>Makes a command again: every file's text as it left it, then the files it moved move again.</summary>
    public string? Redo(Command command)
    {
        foreach (TextChange text in command.Texts)
        {
            if (Put(text, redo: true) is { } failed)
            {
                return failed;
            }
        }
        foreach ((string from, string to) in command.Moves)
        {
            if (editors.MoveFile(from, to) is { } failed)
            {
                return failed;
            }
        }
        undone.Remove(command);
        done.Add(command);
        return null;
    }

    private void Keep(Command command)
    {
        undone.Clear();
        done.Add(command);
        if (done.Count > Limit)
        {
            done.RemoveAt(0);
        }
    }

    /// <summary>A file's text as it was before the command or after it: an open file's through its own undo (the editor
    /// showing it putting its carets back), a closed one's written.</summary>
    private string? Put(TextChange text, bool redo)
    {
        if (IsOpen(text.Document))
        {
            OpenDocument document = text.Document!;
            if (document == editors.Active)
            {
                editors.CodeEditor.TryExecute(redo ? EditorActions.Redo : EditorActions.Undo);
            }
            else if ((redo ? document.Text.Redo() : document.Text.Undo()) is { } carets)
            {
                document.Selections = carets;
            }
            return null;
        }
        try
        {
            FileText? now = FileText.Load(text.Path);
            FileText.Save(text.Path, (redo ? text.After : text.Before).ToString(), now?.Encoding ?? new UTF8Encoding(false), now?.HasBom ?? false, now?.CrBreaks ?? false);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return $"{Path.GetFileName(text.Path)} could not be written: {error.Message}";
        }
    }

    private bool IsOpen(OpenDocument? document) => document is not null && editors.Documents.Contains(document);

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static Command? Find(List<Command> commands, OpenDocument? document, bool redo)
    {
        for (int i = commands.Count - 1; i >= 0; i--)
        {
            Command command = commands[i];
            if (document is null
                || command.Texts.Any(t => t.Document == document && (redo ? document.Text.NextRedo : document.Text.NextUndo) == t.Step))
            {
                return command;
            }
        }
        return null;
    }

    /// <summary>What a command did: its name ("Rename Foo to Bar"), each file's text before and after it (at the path the
    /// file had then; an open file's with the undo step that made it) and the files it moved, in order.</summary>
    public sealed class Command(string name, IReadOnlyList<TextChange> texts, IReadOnlyList<(string From, string To)> moves)
    {
        public string Name { get; } = name;

        public IReadOnlyList<TextChange> Texts { get; } = texts;

        public IReadOnlyList<(string From, string To)> Moves { get; } = moves;

        /// <summary>Where a path is once the command's moves are made.</summary>
        public string Moved(string path)
        {
            foreach ((string from, string to) in Moves)
            {
                path = PathNames.Moved(path, from, to);
            }
            return path;
        }
    }

    /// <summary>One file's text changed by a command: the open file (null for one changed on disk) and the undo step its
    /// change is there.</summary>
    public sealed record TextChange(OpenDocument? Document, string Path, TextSnapshot Before, TextSnapshot After, object? Step);

    /// <summary>A command being made: what it changes is followed until it is disposed, then kept when it is one of
    /// several files or a move.</summary>
    public sealed class Recording : IDisposable
    {
        private readonly CommandHistory history;
        private readonly EditorArea editors;
        private readonly string name;
        private readonly Dictionary<OpenDocument, (string Path, object? Step, TextSnapshot Text)> open;
        private readonly Dictionary<string, TextSnapshot?> closed = [];
        private readonly List<(string From, string To)> moves = [];
        private bool ended;

        internal Recording(CommandHistory history, EditorArea editors, string name)
        {
            this.history = history;
            this.editors = editors;
            this.name = name;
            open = editors.Documents.ToDictionary(d => d, d => (d.Path, d.Text.NextUndo, d.Text.Snapshot));
            editors.Moved += OnMoved;
        }

        /// <summary>A file not open the command is about to write: its text now, for the undo to put back.</summary>
        public void Watch(string path)
        {
            if (!editors.Documents.Any(d => d.Path == path) && !closed.ContainsKey(path))
            {
                closed[path] = File.Exists(path) && FileText.Load(path) is { } file ? TextSnapshot.From(file.Text) : null;
            }
        }

        public void Dispose()
        {
            if (ended)
            {
                return;
            }
            ended = true;
            editors.Moved -= OnMoved;
            Command made = new(name, [], [.. moves]);
            List<TextChange> texts = [];
            foreach ((OpenDocument document, (string path, object? step, TextSnapshot text)) in open)
            {
                if (editors.Documents.Contains(document) && document.Text.NextUndo is { } now && now != step)
                {
                    texts.Add(new TextChange(document, path, text, document.Text.Snapshot, now));
                }
            }
            foreach ((string path, TextSnapshot? before) in closed)
            {
                string at = made.Moved(path);
                if (before is not null && File.Exists(at) && FileText.Load(at)?.Text is { } after && after != before.ToString())
                {
                    texts.Add(new TextChange(null, path, before, TextSnapshot.From(after), null));
                }
            }
            if (texts.Count > 1 || moves.Count > 0)
            {
                history.Keep(new Command(name, texts, made.Moves));
            }
        }

        private void OnMoved(object? sender, FileMove move) => moves.Add((move.From, move.To));
    }
}
