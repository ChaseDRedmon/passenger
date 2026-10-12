using E = Passenger.Editor.EditorActions;
using A = Passenger.App.AppActions;

namespace Passenger.App;

/// <summary>One entry of the main menu: an action (titled as the catalog titles it), a submenu, a separator, or the
/// place the tool windows are listed.</summary>
public sealed record MenuEntry(string? Action, string? Title = null, IReadOnlyList<MenuEntry>? Items = null)
{
    public bool IsSeparator => Action is null && Items is null && Title is null;

    /// <summary>Where View's Tool Windows lists every tool window the dock has.</summary>
    public bool IsToolWindows => Title == MainMenu.ToolWindows && Items is null;
}

/// <summary>Rider's main menu, in its order and with its separators, every item an action of the catalog: File, Edit,
/// View, Navigate, Code, Refactor, Build, Run, Tests, Tools, Git, Window and Help. On macOS it is the menu bar (with
/// Settings and Quit in the application menu, as there); elsewhere a menu behind the toolbar's button.</summary>
public static class MainMenu
{
    public const string ToolWindows = "Tool Windows";

    private static readonly MenuEntry Separator = new(null);

    public static IReadOnlyList<MenuEntry> Menus { get; } =
    [
        Menu("File", [
            Item(A.Open), Item(A.RecentFiles), Separator,
            Item(A.Save), Item(A.SaveAll), Item(A.CloseTab),
            // Settings and Quit are the application menu's on macOS.
            .. OperatingSystem.IsMacOS() ? Array.Empty<MenuEntry>() : [Separator, Item(A.ShowSettings), Separator, Item(A.Exit)]]),
        Menu("Edit",
            Item(E.Undo), Item(E.Redo), Separator,
            Item(E.Cut), Item(E.Copy), Item(E.Paste), Item(E.Duplicate), Item(E.DeleteLine), Separator,
            Item(E.SelectAll), Item(A.ExtendSelection), Item(A.ShrinkSelection), Item(E.SelectNextOccurrence), Item(E.SelectAllOccurrences), Separator,
            Menu("Find", Item(A.Find), Item(A.Replace), Item(A.FindNext), Item(A.FindPrevious), Separator, Item(A.FindInFiles), Item(A.ReplaceInFiles)),
            Separator,
            Item(E.ToggleLineComment), Item(E.MoveLineUp), Item(E.MoveLineDown)),
        Menu("View",
            new MenuEntry(null, ToolWindows), Separator,
            Item(A.RecentFiles), Item(A.QuickDocumentation), Item(A.ParameterInfo), Separator,
            Item(A.MaximizeToolWindow), Item(A.HideAllToolWindows), Item(A.HideToolWindow)),
        Menu("Navigate",
            Item(A.Back), Item(A.Forward), Separator,
            Item(A.GoToEverything), Item(A.GoToFile), Item(A.GoToSymbol), Item(A.GoToLine), Item(A.FileStructure), Separator,
            Item(A.GoToDeclaration), Item(A.GoToImplementation), Item(A.FindUsages), Item(A.CallHierarchy), Item(A.TypeHierarchy), Separator,
            Item(A.NextError), Item(A.PreviousError), Item(A.NextChange), Item(A.PreviousChange), Separator,
            Menu("Bookmarks", Item(A.ToggleBookmark), Item(A.NextBookmark), Item(A.PreviousBookmark), Item(A.ShowBookmarks))),
        Menu("Code",
            Item(A.Complete), Item(A.QuickFix), Item(A.Generate), Separator,
            Item(A.Reformat), Item(A.CodeCleanup), Separator,
            Item(E.ToggleLineComment), Separator,
            Menu("Folding", Item(E.ToggleFold), Item(E.Fold), Item(E.Unfold), Item(E.FoldAll), Item(E.UnfoldAll)),
            Separator,
            Item(A.ToggleSolutionAnalysis)),
        Menu("Refactor",
            Item(A.RefactorThis), Separator,
            Item(A.Rename), Item(A.ExtractMethod), Item(A.IntroduceVariable), Item(A.Inline)),
        Menu("Build",
            Item(A.Build), Item(A.RebuildSolution), Item(A.CleanSolution), Separator,
            Item(A.BuildSelected), Item(A.RebuildSelected), Item(A.CleanSelected)),
        Menu("Run",
            Item(A.RunProgram), Item(A.Debug), Item(A.Profile), Item(A.Rerun), Item(A.Stop), Separator,
            Item(A.Resume), Item(A.Pause), Item(A.StepOver), Item(A.StepInto), Item(A.StepOut), Item(A.RunToCursor),
            Item(A.SetNextStatement), Item(A.ShowExecutionPoint), Item(A.Evaluate), Separator,
            Item(A.ToggleBreakpoint), Item(A.ToggleBreakpointEnabled), Item(A.EditBreakpoint), Item(A.ViewBreakpoints)),
        Menu("Tests",
            Item(A.RunTests), Item(A.DebugTests), Item(A.RunTestsUntilFailure), Item(A.RunAllTests), Separator,
            Item(A.RerunTests), Item(A.RerunFailedTests), Item(A.StopTests), Separator,
            Item(A.ShowTests)),
        Menu("Tools",
            Item(A.ShowNuGet), Item(A.ShowProfiler), Item(A.ShowTerminal)),
        Menu("Git",
            Item(A.Commit), Item(A.Push), Item(A.UpdateProject), Item(A.Fetch), Item(A.Branches), Separator,
            Item(A.Stash), Item(A.Unstash), Separator,
            Item(A.ShowDiff), Item(A.Annotate), Item(A.RollbackLines), Separator,
            Item(A.ShowCommit), Item(A.ShowGit)),
        Menu("Window",
            Item(A.NextTab), Item(A.PreviousTab), Separator,
            Item(A.SplitRight), Item(A.SplitDown), Item(A.MoveToOppositeGroup)),
        Menu("Help",
            Item(A.FindAction), Separator,
            Item(A.ShowLog)),
    ];

    /// <summary>Every action the menus offer, for the catalog's check that each is one it knows.</summary>
    public static IEnumerable<string> Actions => Menus.SelectMany(Flatten).Where(e => e.Action is not null).Select(e => e.Action!);

    private static IEnumerable<MenuEntry> Flatten(MenuEntry entry) => entry.Items is { } items ? [entry, .. items.SelectMany(Flatten)] : [entry];

    private static MenuEntry Item(string action) => new(action);

    private static MenuEntry Menu(string title, params MenuEntry[] items) => new(null, title, items);
}
