using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Editors;

/// <summary>Whether to save a modified file being closed.</summary>
public enum SaveChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>The tabs and the one editor that shows whichever is active. Each tab keeps its own
/// carets and scroll, handed back to the editor when it is shown again.</summary>
public partial class EditorArea : UserControl
{
    private readonly ObservableCollection<OpenDocument> documents = [];
    private OpenDocument? active;

    public EditorArea()
    {
        InitializeComponent();
        Tabs.ItemsSource = documents;
        Editor.SelectionChanged += (_, _) => CaretMoved?.Invoke(this, EventArgs.Empty);
        OpenButton.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ActiveChanged;
    public event EventHandler? CaretMoved;
    public event EventHandler? OpenRequested;

    /// <summary>Asked before a modified file closes; null closes without asking.</summary>
    public Func<OpenDocument, Task<SaveChoice>>? AskToSave { get; set; }

    public CodeEditor CodeEditor => Editor;
    public OpenDocument? Active => active;
    public IReadOnlyList<OpenDocument> Documents => documents;

    /// <summary>The empty area's words once a folder is open: it has files now, just none showing.</summary>
    public void ShowFolderHint(string folder)
    {
        EmptyTitle.Text = Path.GetFileName(folder);
        EmptyHint.Text = "Open a file from the Explorer.";
        OpenButton.IsVisible = false;
    }

    /// <summary>Opens a file in a tab (or shows its tab if open), returning why not if it can't be.</summary>
    public string? Open(string path)
    {
        string full = Path.GetFullPath(path);
        if (documents.FirstOrDefault(d => d.Path == full) is { } open)
        {
            Activate(open);
            return null;
        }
        FileText? file;
        try
        {
            file = FileText.Load(full);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return error.Message;
        }
        if (file is null)
        {
            return $"{Path.GetFileName(full)} looks like a binary file.";
        }
        OpenDocument document = new(full, file);
        documents.Insert(active is null ? documents.Count : documents.IndexOf(active) + 1, document);
        Activate(document);
        return null;
    }

    public void Activate(OpenDocument document)
    {
        if (active is not null)
        {
            active.Selections = Editor.Selections;
            active.Scroll = Editor.ScrollOffset;
            active.IsActive = false;
        }
        active = document;
        document.IsActive = true;
        Editor.LineCommentPrefix = document.Language.LineComment;
        Editor.Indent = new IndentOptions(document.Language.IndentSize);
        Editor.Show(document.Text, document.Selections, document.Scroll);
        Editor.Highlighter = document.Highlighter;
        Find.Attach(Editor);
        ShowEditor(true);
        Editor.Focus();
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> Close(OpenDocument document)
    {
        if (document.IsModified && AskToSave is { } ask)
        {
            switch (await ask(document))
            {
                case SaveChoice.Cancel:
                    return false;
                case SaveChoice.Save:
                    document.Save();
                    break;
            }
        }
        int index = documents.IndexOf(document);
        documents.Remove(document);
        document.Close();
        if (document == active)
        {
            active = null;
            if (documents.Count > 0)
            {
                Activate(documents[Math.Min(index, documents.Count - 1)]);
            }
            else
            {
                Find.Close();
                ShowEditor(false);
                ActiveChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        return true;
    }

    public FindBar FindBar => Find;

    /// <summary>Opens the find bar over the active file, with the replace row if asked.</summary>
    public void ShowFind(bool replace)
    {
        if (active is not null)
        {
            Find.Open(replace);
        }
    }

    public void SaveActive() => active?.Save();

    public void SaveAll()
    {
        foreach (OpenDocument document in documents.Where(d => d.IsModified))
        {
            document.Save();
        }
    }

    /// <summary>The next tab (or previous, for a negative step), wrapping at the ends.</summary>
    public void Cycle(int step)
    {
        if (active is null || documents.Count < 2)
        {
            return;
        }
        int index = (documents.IndexOf(active) + step + documents.Count) % documents.Count;
        Activate(documents[index]);
    }

    private void ShowEditor(bool visible)
    {
        Editor.IsVisible = visible;
        TabBar.IsVisible = documents.Count > 0;
        EmptyState.IsVisible = !visible;
    }

    private void OnTabPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: OpenDocument document })
        {
            return;
        }
        PointerPointProperties pressed = e.GetCurrentPoint(this).Properties;
        if (pressed.IsMiddleButtonPressed)
        {
            _ = Close(document);
        }
        else if (pressed.IsLeftButtonPressed)
        {
            Activate(document);
        }
        e.Handled = true;
    }

    private void OnTabCloseClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: OpenDocument document })
        {
            _ = Close(document);
        }
        e.Handled = true;
    }
}
