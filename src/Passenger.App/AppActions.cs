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
                .Bind(FocusExplorer, "meta 1", "meta alt L");
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
            .Bind(FocusExplorer, "alt 1", "control alt L");
    }
}
