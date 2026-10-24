namespace Passenger.App;

/// <summary>Why an action cannot run for want of what it acts on, named once, so the same want reads the same wherever an
/// action, a tool or a status line says it: the status bar, a greyed menu item's tip and an agent's ide_action alike.</summary>
public static class Refusals
{
    /// <summary>No file is shown in the editor.</summary>
    public const string NoFile = "No file is open";

    /// <summary>The window has no folder open (a solution, a project or a folder of files).</summary>
    public const string NoFolder = "No folder is open";

    /// <summary>The folder open has no solution (a project opened alone, or a folder of files).</summary>
    public const string NoSolution = "No solution is open";

    /// <summary>Why an action on the file shown cannot run: none is shown.</summary>
    public static string? NeedsFile(ActionContext context) => context.File is null ? NoFile : null;

    /// <summary>Why a line of the file shown cannot be acted on: none is shown, or its tab holds no lines (a preview, a workbook).</summary>
    public static string? NeedsLines(ActionContext context) =>
        context.File is not { } file ? NoFile : file.HoldsText ? null : $"{file.Name} is {file.ShownAs}, with no lines";
}
