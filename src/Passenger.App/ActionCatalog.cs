using System.Reflection;
using Passenger.Editor;

namespace Passenger.App;

/// <summary>An action as Find Action lists it: its id, the name a person searches for, its group, and its shortcut.</summary>
public sealed record ActionInfo(string Id, string Title, string Group, string? Shortcut);

/// <summary>Every action the window runs, the IDE's and the editor's, named for people: Rider's names where it has one,
/// the id's own words otherwise. Find Action searches it, and the MCP server lists it.</summary>
public static class ActionCatalog
{
    /// <summary>Names that are not just the id's words (Rider's, where the action is Rider's).</summary>
    private static readonly Dictionary<string, string> Titles = new()
    {
        [AppActions.Open] = "Open...",
        [AppActions.Build] = "Build Solution",
        [AppActions.RebuildSolution] = "Rebuild Solution",
        [AppActions.CleanSolution] = "Clean Solution",
        [AppActions.BuildSelected] = "Build Selected Projects",
        [AppActions.RebuildSelected] = "Rebuild Selected Projects",
        [AppActions.CleanSelected] = "Clean Selected Projects",
        [AppActions.RunProgram] = "Run",
        [AppActions.Debug] = "Debug",
        [AppActions.Stop] = "Stop",
        [AppActions.Rerun] = "Rerun",
        [AppActions.GoToEverything] = "Search Everywhere",
        [AppActions.GoToFile] = "Go to File",
        [AppActions.GoToLine] = "Go to Line:Column",
        [AppActions.GoToSymbol] = "Go to Symbol",
        [AppActions.FocusExplorer] = "Explorer",
        [AppActions.FocusEditor] = "Focus Editor",
        [AppActions.HideToolWindow] = "Hide Active Tool Window",
        [AppActions.MaximizeToolWindow] = "Maximize Tool Window",
        [AppActions.HideAllToolWindows] = "Hide All Tool Windows",
        [AppActions.Complete] = "Basic Completion",
        [AppActions.QuickFix] = "Show Context Actions",
        [AppActions.Rename] = "Rename...",
        [AppActions.Reformat] = "Reformat Code",
        [AppActions.CodeCleanup] = "Code Cleanup",
        [AppActions.RefactorThis] = "Refactor This...",
        [AppActions.Generate] = "Generate...",
        [AppActions.ExtractMethod] = "Extract Method...",
        [AppActions.IntroduceVariable] = "Introduce Variable...",
        [AppActions.Inline] = "Inline...",
        [AppActions.QuickDocumentation] = "Quick Documentation",
        [AppActions.NextError] = "Next Highlighted Error",
        [AppActions.PreviousError] = "Previous Highlighted Error",
        [AppActions.Resume] = "Resume Program",
        [AppActions.Pause] = "Pause Program",
        [AppActions.ShowExecutionPoint] = "Show Execution Point",
        [AppActions.Evaluate] = "Evaluate Expression...",
        [AppActions.EditBreakpoint] = "Edit Breakpoint...",
        [AppActions.ViewBreakpoints] = "View Breakpoints...",
        [AppActions.RunTests] = "Run Unit Tests",
        [AppActions.DebugTests] = "Debug Unit Tests",
        [AppActions.RunAllTests] = "Run All Tests",
        [AppActions.RerunTests] = "Rerun Unit Tests",
        [AppActions.RerunFailedTests] = "Rerun Failed Tests",
        [AppActions.StopTests] = "Stop Unit Tests",
        [AppActions.ShowTests] = "Unit Tests",
        [AppActions.ShowStructure] = "Structure",
        [AppActions.ShowBuild] = "Build Tool Window",
        [AppActions.ShowRun] = "Run Tool Window",
        [AppActions.ShowDebug] = "Debug Tool Window",
        [AppActions.ShowProblems] = "Problems",
        [AppActions.ShowTerminal] = "Terminal",
        [AppActions.ShowGit] = "Git",
        [AppActions.Annotate] = "Annotate with Git Blame",
        [AppActions.ShowCommit] = "Commit Tool Window",
        [AppActions.Commit] = "Commit...",
        [AppActions.Push] = "Push...",
        [AppActions.UpdateProject] = "Update Project...",
        [AppActions.Branches] = "Branches...",
        [AppActions.Stash] = "Stash Changes...",
        [AppActions.CallHierarchy] = "Call Hierarchy",
        [AppActions.TypeHierarchy] = "Type Hierarchy",
        [AppActions.Unstash] = "Unstash Changes...",
        [AppActions.RollbackLines] = "Rollback Lines",
        [AppActions.FindAction] = "Find Action...",
        [AppActions.FindInFiles] = "Find in Files...",
        [AppActions.RecentFiles] = "Recent Files",
        [AppActions.ToggleBookmark] = "Toggle Bookmark",
        [AppActions.NextBookmark] = "Next Bookmark",
        [AppActions.PreviousBookmark] = "Previous Bookmark",
        [AppActions.ShowBookmarks] = "Bookmarks",
        [AppActions.ReplaceInFiles] = "Replace in Files...",
        [AppActions.ShowSettings] = "Settings...",
        [AppActions.ShowLog] = "Show Log in Finder",
        [AppActions.ShowNuGet] = "NuGet",
        [AppActions.Profile] = "Profile",
        [AppActions.ShowProfiler] = "Performance Profiler",
        [EditorActions.ToggleLineComment] = "Comment with Line Comment",
        [EditorActions.ToggleFold] = "Expand/Collapse",
        [EditorActions.Fold] = "Collapse",
        [EditorActions.Unfold] = "Expand",
        [EditorActions.FoldAll] = "Collapse All",
        [EditorActions.UnfoldAll] = "Expand All",
    };

    private static readonly string[] SmallWords = ["to", "of", "in", "on", "and", "a", "by", "with", "at"];

    /// <summary>Every action, in the order Find Action lists them before anything is typed.</summary>
    public static IReadOnlyList<ActionInfo> All(Keymap keymap) =>
        [.. Ids().Select(id => new ActionInfo(id, Title(id), Group(id), keymap.ShortcutsFor(id) is { Count: > 0 } keys ? keys[0].Display() : null))
            .OrderBy(a => a.Group, StringComparer.Ordinal).ThenBy(a => a.Title, StringComparer.Ordinal)];

    /// <summary>The actions whose name, group or id has every word typed, the names that start with the query first.</summary>
    public static IReadOnlyList<ActionInfo> Find(string query, Keymap keymap)
    {
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return All(keymap);
        }
        return [.. All(keymap)
            .Where(a => words.All(w => a.Title.Contains(w, StringComparison.OrdinalIgnoreCase) || a.Group.Contains(w, StringComparison.OrdinalIgnoreCase) || a.Id.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(a => a.Title.StartsWith(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 0 : words.All(w => a.Title.Contains(w, StringComparison.OrdinalIgnoreCase)) ? 1 : 2)
            .ThenBy(a => a.Title.Length)];
    }

    /// <summary>The ids of every action: the constants of the IDE's actions and the editor's.</summary>
    public static IEnumerable<string> Ids() =>
        new[] { typeof(AppActions), typeof(EditorActions) }
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Distinct();

    public static string Title(string id)
    {
        if (id == AppActions.ShowLog && !OperatingSystem.IsMacOS())
        {
            return "Show Log in Explorer";
        }
        if (Titles.TryGetValue(id, out string? title))
        {
            return title;
        }
        string word = id[(id.LastIndexOf('.') + 1)..];
        List<string> words = [];
        int start = 0;
        for (int i = 1; i <= word.Length; i++)
        {
            if (i == word.Length || char.IsUpper(word[i]))
            {
                words.Add(word[start..i]);
                start = i;
            }
        }
        return string.Join(' ', words.Select((w, i) => i > 0 && SmallWords.Contains(w.ToLowerInvariant()) ? w.ToLowerInvariant() : char.ToUpperInvariant(w[0]) + w[1..]));
    }

    public static string Group(string id) => id switch
    {
        _ when id.StartsWith("editor.", StringComparison.Ordinal) => "Editor",
        _ when id.StartsWith("app.code.", StringComparison.Ordinal) => "Code",
        _ when id.StartsWith("app.debug.", StringComparison.Ordinal) || id.StartsWith("app.breakpoints.", StringComparison.Ordinal) => "Debug",
        _ when id.StartsWith("app.tests.", StringComparison.Ordinal) => "Unit Tests",
        _ when id.StartsWith("app.vcs.", StringComparison.Ordinal) => "Git",
        _ when id.StartsWith("app.editor.", StringComparison.Ordinal) => "Editor Tabs",
        _ when id.StartsWith("app.bookmarks.", StringComparison.Ordinal) => "Bookmarks",
        _ when id is AppActions.ShowLog => "Help",
        _ when id.StartsWith("app.window.", StringComparison.Ordinal) || id.StartsWith("app.show", StringComparison.Ordinal) || id is AppActions.FocusExplorer or AppActions.HideToolWindow => "Tool Windows",
        _ when id.StartsWith("app.goTo", StringComparison.Ordinal) || id is AppActions.FileStructure or AppActions.Back or AppActions.Forward => "Navigate",
        _ when id.StartsWith("app.build.", StringComparison.Ordinal) => "Build",
        _ when id is AppActions.Build or AppActions.RunProgram or AppActions.Debug or AppActions.Stop or AppActions.Rerun or AppActions.Profile => "Run",
        _ => "IDE",
    };
}
