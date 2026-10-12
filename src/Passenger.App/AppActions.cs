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
    public const string ShowExecutionPoint = "app.debug.showExecutionPoint";
    public const string Evaluate = "app.debug.evaluate";

    // The breakpoints' windows, the window's to show.
    public const string EditBreakpoint = "app.breakpoints.edit";
    public const string ViewBreakpoints = "app.breakpoints.view";

    public static bool IsApp(string action) => action.StartsWith("app.", StringComparison.Ordinal);

    public static Keymap Keymap()
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
                .Bind(ShowExecutionPoint, "alt MULTIPLY")
                .Bind(Evaluate, "shift F9")
                .Bind(EditBreakpoint, "alt F9")
                .Bind(ViewBreakpoints, "meta alt B")
                .Bind(HideToolWindow, "shift ESCAPE")
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
                .Bind(QuickDocumentation, "meta K , I", "meta K , meta I", "control shift F1")
                .Bind(NextError, "alt PAGE_DOWN")
                .Bind(PreviousError, "alt PAGE_UP");
        }
        return keymap
            .Bind(Open, "control O")
            .Bind(Save, "control S")
            .Bind(SaveAll, "control shift S")
            .Bind(CloseTab, "control F4", "control W")
            .Bind(NextTab, "alt RIGHT")
            .Bind(PreviousTab, "alt LEFT")
            .Bind(GoToFile, "control shift T")
            .Bind(GoToLine, "control G")
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
            .Bind(ShowExecutionPoint, "alt MULTIPLY")
            .Bind(Evaluate, "shift F9")
            .Bind(EditBreakpoint, "alt F9")
            .Bind(ViewBreakpoints, "control alt B")
            .Bind(HideToolWindow, "shift ESCAPE")
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
            .Bind(QuickDocumentation, "control K , I", "control K , control I", "control shift F1")
            .Bind(NextError, "alt PAGE_DOWN")
            .Bind(PreviousError, "alt PAGE_UP");
    }
}
