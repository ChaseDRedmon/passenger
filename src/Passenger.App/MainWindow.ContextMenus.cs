using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Passenger.App.Debugging;
using Passenger.Editor;
using E = Passenger.Editor.EditorActions;
using A = Passenger.App.AppActions;

namespace Passenger.App;

/// <summary>Right-click menus as Rider's: the editor's for what is under the click (the caret goes there first, unless the
/// click is in the selection), built from the action catalog with the keymap's shortcuts, items that cannot run
/// disabled; and the menu key (Shift+F10) opening the focused place's menu at its caret or chosen row.</summary>
public partial class MainWindow
{
    /// <summary>The context menu open now, if any.</summary>
    public ContextMenu? OpenContextMenu { get; private set; }

    private void AddContextMenus() => Editors.ForEachEditor(editor => editor.ContextMenuRequested += (_, at) => ShowEditorMenu(editor, at));

    /// <summary>Rider's editor menu: context actions and the clipboard, Find Usages, Refactor, Go To, Generate and
    /// Folding, Reformat and Cleanup, the test at the caret, the paused program's actions, breakpoints, and Git.</summary>
    private void ShowEditorMenu(CodeEditor editor, Point? at)
    {
        if (Editors.Groups.FirstOrDefault(g => g.Editor == editor) is { } group)
        {
            Editors.SetActiveGroup(group);
        }
        if (Editors.Active is not { } active || editor != Editors.CodeEditor)
        {
            return;
        }
        List<Control> items = [];
        void Add(params MenuEntry[] entries) => items.AddRange(entries.Select(FlyoutEntry));
        MenuEntry separator = new(null);
        Add(Item(A.QuickFix), separator, Item(E.Cut), Item(E.Copy), Item(E.Paste), separator,
            Item(A.FindUsages),
            Sub("Refactor", Item(A.RefactorThis), separator, Item(A.Rename), Item(A.ExtractMethod), Item(A.IntroduceVariable), Item(A.Inline)),
            Sub("Go To", Item(A.GoToDeclaration), Item(A.GoToImplementation), separator, Item(A.CallHierarchy), Item(A.TypeHierarchy)),
            Item(A.Generate),
            Sub("Folding", Item(E.ToggleFold), Item(E.Fold), Item(E.Unfold), Item(E.FoldAll), Item(E.UnfoldAll)),
            separator,
            Item(A.Reformat), Item(A.CodeCleanup));
        string text = active.Text.Snapshot.ToString();
        if (Testing.TestsAtCaret.At(text, editor.CaretPosition.Line) is { } test)
        {
            string name = test.Method ?? test.Type[(test.Type.LastIndexOfAny(['.', '+']) + 1)..];
            items.Add(new Separator());
            items.Add(Command($"Run '{name}'", () => RunTestsAt(test.Type, test.Method, debug: false)));
            items.Add(Command($"Debug '{name}'", () => RunTestsAt(test.Type, test.Method, debug: true)));
            items.Add(Command($"Run '{name}' Until Failure", () => RunTestsAt(test.Type, test.Method, debug: false, untilFailure: true)));
        }
        Add(separator);
        if (debug.Session is { State: DebugState.Paused })
        {
            Add(Item(A.RunToCursor), Item(A.SetNextStatement), Item(A.Evaluate), Item(A.AddToWatches), separator);
        }
        Add(Item(A.ToggleBreakpoint));
        if (vcs.Repository is not null)
        {
            Add(separator, Sub("Git", Item(A.Annotate), Item(A.ShowDiff), Item(A.RollbackLines)));
        }
        ContextMenu menu = new() { ItemsSource = items, PlacementTarget = editor };
        if (at is null)
        {
            // From the keyboard: under the caret's line, as Rider opens it.
            Point caret = editor.PointOf(editor.Selections.Primary.Caret);
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
            menu.PlacementRect = new Rect(caret, new Size(1, editor.LineHeight));
        }
        Open(menu, editor);

        static MenuEntry Item(string action) => new(action);
        static MenuEntry Sub(string title, params MenuEntry[] entries) => new(null, title, entries);
    }

    /// <summary>The menu key (Shift+F10): the menu of the place with the focus, at its caret or chosen row.</summary>
    private void ShowFocusedContextMenu()
    {
        switch (FocusManager?.GetFocusedElement())
        {
            case CodeEditor editor:
                editor.RequestContextMenu();
                break;
            case Control control:
                control.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
                break;
        }
    }

    /// <summary>Opens a context menu, remembering it while it is open.</summary>
    private void Open(ContextMenu menu, Control target)
    {
        OpenContextMenu?.Close();
        OpenContextMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (OpenContextMenu == menu)
            {
                OpenContextMenu = null;
            }
        };
        menu.Open(target);
    }

    /// <summary>A menu item that runs something other than an action of the catalog (a test by name).</summary>
    private static MenuItem Command(string header, Action run)
    {
        MenuItem item = new() { Header = header };
        item.Click += (_, _) => run();
        return item;
    }
}
