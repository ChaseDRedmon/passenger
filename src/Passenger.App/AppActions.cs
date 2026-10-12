using Passenger.Editor;

namespace Passenger.App;

/// <summary>The IDE's own actions, beside the editor's (<see cref="EditorActions"/>), and the one
/// keymap that binds both: Rider's Visual Studio keymaps for these too.</summary>
public static class AppActions
{
    public const string Open = "app.open";
    public const string Save = "app.save";
    public const string SaveAll = "app.saveAll";
    public const string CloseTab = "app.closeTab";
    public const string NextTab = "app.nextTab";
    public const string PreviousTab = "app.previousTab";
    public const string GoToFile = "app.goToFile";
    public const string GoToLine = "app.goToLine";
    public const string GoToSymbol = "app.goToSymbol";
    public const string GoToEverything = "app.goToEverything";
    public const string FileStructure = "app.fileStructure";
    public const string FindAction = "app.findAction";
    public const string FindInFiles = "app.findInFiles";
    public const string RecentFiles = "app.recentFiles";

    // Bookmarks: Rider's, on the Visual Studio keymaps' Ctrl+K chords.
    public const string ToggleBookmark = "app.bookmarks.toggle";
    public const string NextBookmark = "app.bookmarks.next";
    public const string PreviousBookmark = "app.bookmarks.previous";
    public const string ShowBookmarks = "app.bookmarks.show";
    public const string ReplaceInFiles = "app.replaceInFiles";
    public const string ShowSettings = "app.settings";
    public const string ShowNuGet = "app.showNuGet";
    public const string Profile = "app.profile";
    public const string ShowProfiler = "app.showProfiler";
    public const string Find = "app.find";
    public const string Replace = "app.replace";
    public const string FindNext = "app.findNext";
    public const string FindPrevious = "app.findPrevious";
    public const string FocusExplorer = "app.focusExplorer";
    public const string Build = "app.build";
    public const string RunProgram = "app.run";
    public const string Debug = "app.debug";
    public const string Stop = "app.stop";
    public const string Rerun = "app.rerun";
    public const string ShowBuild = "app.showBuild";
    public const string ShowRun = "app.showRun";
    public const string ShowProblems = "app.showProblems";
    public const string ShowDebug = "app.showDebug";
    public const string HideToolWindow = "app.hideToolWindow";
    public const string Back = "app.back";
    public const string Forward = "app.forward";

    // Code actions: what the language server answers, run by CodeAssist on the active editor.
    public const string Complete = "app.code.complete";
    public const string QuickFix = "app.code.quickFix";
    public const string Rename = "app.code.rename";
    public const string GoToDeclaration = "app.code.goToDeclaration";
    public const string GoToImplementation = "app.code.goToImplementation";
    public const string FindUsages = "app.code.findUsages";
    public const string Reformat = "app.code.reformat";
    public const string CodeCleanup = "app.code.cleanup";
    public const string RefactorThis = "app.code.refactorThis";
    public const string Generate = "app.code.generate";
    public const string ExtractMethod = "app.code.extractMethod";
    public const string IntroduceVariable = "app.code.introduceVariable";
    public const string Inline = "app.code.inline";
    public const string ParameterInfo = "app.code.parameterInfo";
    public const string QuickDocumentation = "app.code.quickDocumentation";
    public const string NextError = "app.code.nextError";
    public const string PreviousError = "app.code.previousError";
    public const string FocusEditor = "app.focusEditor";

    // The debugger's: run by the Debugger on the active editor and the session.
    public const string ToggleBreakpoint = "app.debug.toggleBreakpoint";
    public const string ToggleBreakpointEnabled = "app.debug.toggleBreakpointEnabled";
    public const string Resume = "app.debug.resume";
    public const string Pause = "app.debug.pause";
    public const string StepOver = "app.debug.stepOver";
    public const string StepInto = "app.debug.stepInto";
    public const string StepOut = "app.debug.stepOut";
    public const string RunToCursor = "app.debug.runToCursor";
    public const string SetNextStatement = "app.debug.setNextStatement";
    public const string ExtendSelection = "app.editor.extendSelection";
    public const string ShrinkSelection = "app.editor.shrinkSelection";
    public const string ShowExecutionPoint = "app.debug.showExecutionPoint";
    public const string Evaluate = "app.debug.evaluate";

    // Unit tests: run where the caret or the Unit Tests window's selection is, as Rider's context actions do.
    public const string RunTests = "app.tests.run";
    public const string DebugTests = "app.tests.debug";
    public const string RunAllTests = "app.tests.runAll";
    public const string RerunTests = "app.tests.rerun";
    public const string RerunFailedTests = "app.tests.rerunFailed";
    public const string StopTests = "app.tests.stop";
    public const string ShowTests = "app.showTests";
    public const string ShowStructure = "app.showStructure";

    // Editor splits: Rider's, from the tab's menu (no keys in its Visual Studio keymaps).
    public const string SplitRight = "app.editor.splitRight";
    public const string SplitDown = "app.editor.splitDown";
    public const string MoveToOppositeGroup = "app.editor.moveToOppositeGroup";

    // The terminal: Rider's, a shell per tab in the open folder.
    public const string ShowTerminal = "app.showTerminal";

    // The tool windows' frame.
    public const string MaximizeToolWindow = "app.window.maximize";
    public const string HideAllToolWindows = "app.window.hideAll";

    // The breakpoints' windows, the window's to show.
    public const string EditBreakpoint = "app.breakpoints.edit";
    public const string ViewBreakpoints = "app.breakpoints.view";

    // Version control: Rider's Git actions over the open folder's working tree.
    public const string ShowCommit = "app.vcs.showCommit";
    public const string Commit = "app.vcs.commit";
    public const string Push = "app.vcs.push";
    public const string Branches = "app.vcs.branches";
    public const string NextChange = "app.vcs.nextChange";
    public const string PreviousChange = "app.vcs.previousChange";
    public const string RollbackLines = "app.vcs.rollbackLines";
    public const string ShowDiff = "app.vcs.showDiff";
    public const string ShowGit = "app.vcs.showGit";
    public const string Annotate = "app.vcs.annotate";
    public const string UpdateProject = "app.vcs.update";
    public const string Fetch = "app.vcs.fetch";

    public static bool IsApp(string action) => action.StartsWith("app.", StringComparison.Ordinal);

    /// <summary>The person's own shortcuts over the Visual Studio keymap's, by action (an empty list unbinds it), as
    /// Settings keeps them.</summary>
    public static IReadOnlyDictionary<string, string[]> UserShortcuts
    {
        get => userShortcuts;
        set
        {
            userShortcuts = value;
            inForce = null;
        }
    }

    private static IReadOnlyDictionary<string, string[]> userShortcuts = new Dictionary<string, string[]>();
    private static Keymap? inForce;

    /// <summary>The keymap in force: the platform's Visual Studio keymap with the person's shortcuts over it. Made once
    /// until those change (the profiler found every tooltip and menu parsing the whole keymap again at startup); a
    /// caller reads it and never binds into it.</summary>
    public static Keymap Keymap() => inForce ??= Keymap(UserShortcuts);

    /// <summary>The keymap with these shortcuts over the platform's; one that does not read is passed over.</summary>
    public static Keymap Keymap(IReadOnlyDictionary<string, string[]> overrides)
    {
        Keymap keymap = Base();
        foreach ((string action, string[] shortcuts) in overrides)
        {
            List<Shortcut> parsed = [];
            foreach (string text in shortcuts)
            {
                try
                {
                    parsed.Add(Shortcut.Parse(text));
                }
                catch (FormatException)
                {
                }
            }
            keymap.Set(action, parsed);
        }
        return keymap;
    }

    private static Keymap Base()
    {
        Keymap keymap = DefaultKeymaps.ForPlatform();
        if (OperatingSystem.IsMacOS())
        {
            return keymap
                .Bind(Open, "meta O")
                .Bind(Save, "meta S")
                .Bind(SaveAll, "meta shift S")
                .Bind(CloseTab, "meta W", "meta F4")
                .Bind(NextTab, "meta shift CLOSE_BRACKET", "control RIGHT")
                .Bind(PreviousTab, "meta shift OPEN_BRACKET", "control LEFT")
                .Bind(GoToFile, "meta shift T")
                .Bind(GoToLine, "meta G")
                .Bind(GoToSymbol, "meta shift alt T")
                .Bind(GoToEverything, "meta T")
                .Bind(FindAction, "meta shift A")
                .Bind(FindInFiles, "meta shift F")
                .Bind(RecentFiles, "control COMMA")
                .Bind(ToggleBookmark, "meta K , K", "meta K , meta K", "meta F2")
                .Bind(NextBookmark, "meta K , N", "meta K , meta N")
                .Bind(PreviousBookmark, "meta K , P", "meta K , meta P")
                .Bind(ShowBookmarks, "meta K , W", "meta K , meta W", "control BACK_QUOTE")
                .Bind(ReplaceInFiles, "meta shift H")
                .Bind(ShowSettings, "meta COMMA")
                .Bind(ShowNuGet, "meta 7", "meta shift 7")
                .Bind(FileStructure, "alt BACK_SLASH")
                .Bind(Find, "meta F")
                .Bind(Replace, "control H")
                .Bind(FindNext, "F3", "meta alt PAGE_DOWN")
                .Bind(FindPrevious, "shift F3", "meta alt PAGE_UP")
                .Bind(FocusExplorer, "meta 1", "meta alt L")
                .Bind(Build, "meta shift B")
                .Bind(RunProgram, "control F5")
                .Bind(Debug, "alt F5")
                .Bind(Stop, "shift F5")
                .Bind(Rerun, "alt shift F5")
                .Bind(ShowBuild, "meta 0")
                .Bind(ShowRun, "meta 4")
                .Bind(ShowDebug, "meta 5")
                .Bind(ShowProblems, "meta 6")
                .Bind(ToggleBreakpoint, "F9")
                .Bind(ToggleBreakpointEnabled, "meta F9")
                .Bind(Resume, "F5")
                .Bind(Pause, "meta PAUSE", "shift meta P")
                .Bind(StepOver, "F10")
                .Bind(StepInto, "F11", "meta F11")
                .Bind(StepOut, "shift F11")
                .Bind(RunToCursor, "meta F10")
                .Bind(SetNextStatement, "meta shift F10")
                .Bind(ExtendSelection, "meta alt RIGHT")
                .Bind(ShrinkSelection, "meta alt LEFT")
                .Bind(ShowExecutionPoint, "alt MULTIPLY")
                .Bind(Evaluate, "shift F9")
                .Bind(EditBreakpoint, "alt F9")
                .Bind(ViewBreakpoints, "meta alt B")
                .Bind(RunTests, "meta U , R", "meta U , meta R")
                .Bind(DebugTests, "meta U , D", "meta U , meta D")
                .Bind(RerunTests, "meta U , U", "meta U , meta U")
                .Bind(RerunFailedTests, "meta U , F", "meta U , meta F")
                .Bind(ShowTests, "meta 8")
                .Bind(ShowStructure, "meta alt F", "control alt F")
                .Bind(MaximizeToolWindow, "meta shift QUOTE")
                .Bind(HideAllToolWindows, "meta shift F12")
                .Bind(HideToolWindow, "shift ESCAPE")
                .Bind(Commit, "meta alt shift K")
                .Bind(Push, "meta shift K")
                .Bind(NextChange, "shift meta alt N")
                .Bind(PreviousChange, "shift meta alt P")
                .Bind(RollbackLines, "meta alt Z")
                .Bind(ShowGit, "meta 9")
                .Bind(ShowTerminal, "meta control 1")
                .Bind(UpdateProject, "meta alt W")
                .Bind(Back, "meta MINUS")
                .Bind(Forward, "meta shift MINUS")
                .Bind(Complete, "control SPACE", "control J")
                .Bind(QuickFix, "alt ENTER")
                .Bind(Rename, "meta R , R", "meta R , meta R")
                .Bind(GoToDeclaration, "F12")
                .Bind(GoToImplementation, "alt END")
                .Bind(FindUsages, "shift F12")
                .Bind(Reformat, "meta alt ENTER", "meta K , F", "meta K , meta F", "meta K , D", "meta K , meta D")
                .Bind(ParameterInfo, "control P")
                .Bind(CodeCleanup, "meta E , C", "meta E , meta C", "meta E , F", "meta E , meta F")
                .Bind(RefactorThis, "meta shift R")
                .Bind(Generate, "meta N")
                .Bind(ExtractMethod, "meta R , M", "meta R , meta M")
                .Bind(IntroduceVariable, "meta R , V", "meta R , meta V")
                .Bind(Inline, "meta R , I", "meta R , meta I")
                .Bind(QuickDocumentation, "meta K , I", "meta K , meta I", "control shift F1")
                .Bind(NextError, "alt PAGE_DOWN")
                .Bind(PreviousError, "alt PAGE_UP");
        }
        return keymap
            .Bind(Open, "control O")
            .Bind(Save, "control S")
            .Bind(SaveAll, "control shift S")
            .Bind(CloseTab, "control F4")
            .Bind(NextTab, "alt RIGHT")
            .Bind(PreviousTab, "alt LEFT")
            .Bind(GoToFile, "control shift T")
            .Bind(GoToLine, "control G")
            .Bind(GoToSymbol, "control shift alt T")
            .Bind(GoToEverything, "control T")
            .Bind(FindAction, "control shift A")
            .Bind(FindInFiles, "control shift F")
            .Bind(RecentFiles, "control COMMA")
            .Bind(ToggleBookmark, "control K , K", "control K , control K")
            .Bind(NextBookmark, "control K , N", "control K , control N")
            .Bind(PreviousBookmark, "control K , P", "control K , control P")
            .Bind(ShowBookmarks, "control K , W", "control K , control W", "control BACK_QUOTE")
            .Bind(ReplaceInFiles, "control shift H")
            .Bind(ShowSettings, "control alt S")
            .Bind(ShowNuGet, "alt 7", "alt shift 7")
            .Bind(FileStructure, "alt BACK_SLASH")
            .Bind(Find, "control F")
            .Bind(Replace, "control H")
            .Bind(FindNext, "F3", "control alt PAGE_DOWN")
            .Bind(FindPrevious, "shift F3", "control alt PAGE_UP")
            .Bind(FocusExplorer, "alt 1", "control alt L")
            .Bind(Build, "control shift B")
            .Bind(RunProgram, "control F5")
            .Bind(Debug, "alt F5")
            .Bind(Stop, "shift F5")
            .Bind(Rerun, "control shift F5")
            .Bind(ShowBuild, "alt 0")
            .Bind(ShowRun, "alt 4")
            .Bind(ShowDebug, "alt 5")
            .Bind(ShowProblems, "alt 6")
            .Bind(ToggleBreakpoint, "F9")
            .Bind(ToggleBreakpointEnabled, "control F9")
            .Bind(Resume, "F5")
            .Bind(Pause, "control PAUSE", "shift control P")
            .Bind(StepOver, "F10")
            .Bind(StepInto, "F11")
            .Bind(StepOut, "shift F11")
            .Bind(RunToCursor, "control F10")
            .Bind(SetNextStatement, "control shift F10")
            .Bind(ExtendSelection, "control W", "shift alt EQUALS", "control alt RIGHT")
            .Bind(ShrinkSelection, "control shift W", "shift alt MINUS", "control alt LEFT")
            .Bind(ShowExecutionPoint, "alt MULTIPLY")
            .Bind(Evaluate, "shift F9")
            .Bind(EditBreakpoint, "alt F9")
            .Bind(ViewBreakpoints, "control alt B")
            .Bind(RunTests, "control U , R", "control U , control R")
            .Bind(DebugTests, "control U , D", "control U , control D")
            .Bind(RerunTests, "control U , U", "control U , control U")
            .Bind(RerunFailedTests, "control U , F", "control U , control F")
            .Bind(ShowTests, "alt 8")
            .Bind(ShowStructure, "control alt F")
            .Bind(MaximizeToolWindow, "control shift QUOTE")
            .Bind(HideAllToolWindows, "control shift F12")
            .Bind(HideToolWindow, "shift ESCAPE")
            .Bind(Commit, "control alt K")
            .Bind(Push, "control shift K")
            .Bind(NextChange, "shift control alt N")
            .Bind(PreviousChange, "shift control alt P")
            .Bind(RollbackLines, "control alt Z")
            .Bind(ShowGit, "alt 9")
            .Bind(ShowTerminal, "control alt 1")
            .Bind(UpdateProject, "control alt W")
            .Bind(Back, "control MINUS")
            .Bind(Forward, "control shift MINUS")
            .Bind(Complete, "control SPACE", "control J")
            .Bind(QuickFix, "alt ENTER", "control PERIOD")
            .Bind(Rename, "control R , R", "control R , control R")
            .Bind(GoToDeclaration, "F12")
            .Bind(GoToImplementation, "alt END")
            .Bind(FindUsages, "shift F12")
            .Bind(Reformat, "control alt ENTER", "control K , F", "control K , control F", "control K , D", "control K , control D")
            .Bind(ParameterInfo, "shift control SPACE")
            .Bind(CodeCleanup, "control E , C", "control E , control C", "control E , F", "control E , control F")
            .Bind(RefactorThis, "control shift R")
            .Bind(Generate, "alt INSERT", "control N")
            .Bind(ExtractMethod, "control R , M", "control R , control M")
            .Bind(IntroduceVariable, "control R , V", "control R , control V")
            .Bind(Inline, "control R , I", "control R , control I")
            .Bind(QuickDocumentation, "control K , I", "control K , control I", "control shift F1")
            .Bind(NextError, "alt PAGE_DOWN")
            .Bind(PreviousError, "alt PAGE_UP");
    }
}
