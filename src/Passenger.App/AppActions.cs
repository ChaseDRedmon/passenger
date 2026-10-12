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
