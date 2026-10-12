using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Passenger.App.Editors;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App;

public partial class MainWindow : Window
{
    /// <summary>Room at the toolbar's left for macOS's window buttons once the toolbar is the title bar.</summary>
    private const double MacWindowButtons = 72;

    private readonly KeymapDispatcher keys = new(AppActions.Keymap());
    private string? folder;

    public MainWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsMacOS())
        {
            // The toolbar is the title bar on macOS, as in Rider. Windows keeps the system's title bar
            // until the shell draws its own chrome there.
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = Toolbar.Height;
        }
        // One keymap for the whole window, seen before any control: IDE actions run from anywhere,
        // editor actions go to the editor when it has the focus.
        AddHandler(KeyDownEvent, OnKeyDownFirst, RoutingStrategies.Tunnel);
        Explorer.FileOpened += (_, path) => OpenFile(path);
        Editors.OpenRequested += (_, _) => _ = PickFolder();
        Editors.ActiveChanged += (_, _) => UpdateStatus();
        Editors.CaretMoved += (_, _) => UpdateCaret();
        Editors.AskToSave = AskToSave;
        UpdateStatus();
    }

    public EditorArea EditorArea => Editors;

    /// <summary>Opens what the command line named: a folder, files in it, a caret position.</summary>
    public void Start(LaunchOptions options)
    {
        if (options.Folder is { } path)
        {
            OpenFolder(path);
        }
        foreach (string file in options.Files)
        {
            OpenFile(file);
        }
        if (options.Caret is { } caret && Editors.Active is not null)
        {
            Editors.CodeEditor.GoTo(caret);
        }
    }

    public void OpenFolder(string path)
    {
        folder = Path.GetFullPath(path);
        string name = Path.GetFileName(folder);
        Explorer.Load(folder);
        Editors.ShowFolderHint(folder);
        ProjectName.Text = name;
        ProjectBadgeText.Text = Initials(name);
        ProjectBadge.IsVisible = true;
        UpdateStatus();
    }

    public void OpenFile(string path)
    {
        string full = Path.IsPathRooted(path) || folder is null ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(folder, path));
        if (Editors.Open(full) is { } problem)
        {
            StatusText.Text = problem;
            return;
        }
        Explorer.Reveal(full);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExtendedIntoWindowDecorationsProperty)
        {
            double left = IsExtendedIntoWindowDecorations && OperatingSystem.IsMacOS() ? MacWindowButtons : 8;
            ToolbarContent.Margin = new Thickness(left, 0, 8, 0);
        }
    }

    private void OnKeyDownFirst(object? sender, KeyEventArgs e)
    {
        string? action = keys.Press(e, out bool consumed, out bool shifted);
        if (action is null)
        {
            e.Handled |= consumed;
            return;
        }
        if (AppActions.IsApp(action))
        {
            if (!shifted)
            {
                RunAppAction(action);
                e.Handled = true;
            }
            return;
        }
        CodeEditor editor = Editors.CodeEditor;
        if (editor.IsFocused && (!shifted || EditorActions.IsMovement(action)) && editor.TryExecute(action, shifted))
        {
            e.Handled = true;
        }
    }

    private void RunAppAction(string action)
    {
        switch (action)
        {
            case AppActions.Open:
                _ = PickFolder();
                break;
            case AppActions.Save:
                Editors.SaveActive();
                break;
            case AppActions.SaveAll:
                Editors.SaveAll();
                break;
            case AppActions.CloseTab:
                if (Editors.Active is { } active)
                {
                    _ = Editors.Close(active);
                }
                break;
            case AppActions.NextTab:
                Editors.Cycle(1);
                break;
            case AppActions.PreviousTab:
                Editors.Cycle(-1);
                break;
            case AppActions.FocusExplorer:
                Explorer.Focus();
                break;
        }
    }

    private async Task PickFolder()
    {
        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open" });
        if (picked.Count == 1 && picked[0].TryGetLocalPath() is { } path)
        {
            OpenFolder(path);
        }
    }

    private async Task<SaveChoice> AskToSave(OpenDocument document)
    {
        SaveDialog dialog = new(document.Name);
        return await dialog.ShowDialog<SaveChoice>(this);
    }

    private void UpdateStatus()
    {
        OpenDocument? active = Editors.Active;
        string where = folder is null ? "No solution" : Path.GetFileName(folder);
        if (active is not null)
        {
            where = folder is not null && active.Path.StartsWith(folder, StringComparison.Ordinal)
                ? Path.GetRelativePath(folder, active.Path)
                : active.Path;
        }
        StatusText.Text = where;
        Title = active is null ? where : $"{(folder is null ? "" : Path.GetFileName(folder) + " – ")}{active.Name}";
        LineEndingText.Text = active is null ? "" : active.Text.LineEnding == "\r\n" ? "CRLF" : "LF";
        EncodingText.Text = active?.EncodingName ?? "";
        IndentText.Text = active is null ? "" : $"{active.Language.IndentSize} spaces";
        LanguageText.Text = active?.Language.Name ?? "";
        UpdateCaret();
    }

    private void UpdateCaret()
    {
        if (Editors.Active is null)
        {
            CaretText.Text = "";
            return;
        }
        CodeEditor editor = Editors.CodeEditor;
        TextPosition caret = editor.CaretPosition;
        int carets = editor.Selections.Count;
        int selected = editor.Selections.Items.Sum(s => s.Length);
        string position = $"{caret.Line + 1}:{caret.Column + 1}";
        CaretText.Text = carets > 1 ? $"{carets} carets" : selected > 0 ? $"{position} ({selected} chars)" : position;
    }

    private void OnToolbarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsExtendedIntoWindowDecorations || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        BeginMoveDrag(e);
    }

    private void OnOpenClicked(object? sender, RoutedEventArgs e) => _ = PickFolder();

    /// <summary>The project badge's two letters, as Rider draws them: the first two words' initials,
    /// or a lone word's first two letters.</summary>
    private static string Initials(string name)
    {
        string[] words = name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        string letters = words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)],
            _ => $"{words[0][0]}{words[1][0]}",
        };
        return letters.ToUpperInvariant();
    }
}
