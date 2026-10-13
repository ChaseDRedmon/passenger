using Passenger.App.ToolWindows;
using Passenger.Core;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App;

/// <summary>Call Hierarchy and Type Hierarchy, as Rider's: the symbol at the caret in the Hierarchy window, its callers
/// or callees, its supertypes or subtypes, from the language server.</summary>
public partial class MainWindow
{
    private HierarchyView? hierarchy;

    public HierarchyView Hierarchy => hierarchy!;

    private void AddHierarchy()
    {
        hierarchy = new HierarchyView();
        hierarchy.GoToRequested += (_, node) => NavigateTo(node.Path, node.Where);
        hierarchy.Failed += (_, why) => StatusText.Text = why;
        Dock.Add(new Docking.ToolWindow("hierarchy", "Hierarchy", "Icon.Hierarchy", hierarchy, Docking.ToolAnchor.RightTop));
        // What the server finds at the caret, or why it found nothing, the status bar says as it answers.
        actions!.Add(new IdeAction(AppActions.CallHierarchy, "Call Hierarchy", "IDE", context => _ = ShowCallHierarchyAsync())
        {
            WhyNot = NeedsFile,
            Menus = [new MenuPlace("Navigate", 2, 3)],
        });
        actions.Add(new IdeAction(AppActions.TypeHierarchy, "Type Hierarchy", "IDE", context => _ = ShowTypeHierarchyAsync())
        {
            WhyNot = NeedsFile,
            Menus = [new MenuPlace("Navigate", 2, 4)],
        });
    }

    /// <summary>Call Hierarchy: who calls the symbol at the caret, and whom it calls; null when shown, else why not.</summary>
    public async Task<string?> ShowCallHierarchyAsync()
    {
        (HierarchyItem? item, string? why) = await Prepare("Call Hierarchy", "No method, property or event at the caret", (lsp, offset) => lsp.PrepareCallHierarchyAsync(offset));
        if (item is null)
        {
            return why;
        }
        ShowToolWindow("hierarchy");
        await Hierarchy.ShowAsync($"Calls of {item.Name}", Node(item), new HierarchyDirection("Callers", async of =>
            [.. (await RunningServer().IncomingCallsAsync(of)).Select(call => Node(call.From, call.FromRanges.FirstOrDefault()))]),
            new HierarchyDirection("Callees", async of =>
            [.. (await RunningServer().OutgoingCallsAsync(of)).Select(call => Node(call.To))]));
        return null;
    }

    /// <summary>Type Hierarchy: the type at the caret's base types and interfaces, and the types that derive from it.</summary>
    public async Task<string?> ShowTypeHierarchyAsync()
    {
        (HierarchyItem? item, string? why) = await Prepare("Type Hierarchy", "No type at the caret", (lsp, offset) => lsp.PrepareTypeHierarchyAsync(offset));
        if (item is null)
        {
            return why;
        }
        ShowToolWindow("hierarchy");
        await Hierarchy.ShowAsync($"Hierarchy of {item.Name}", Node(item),
            new HierarchyDirection("Supertypes", async of => [.. (await RunningServer().SupertypesAsync(of)).Select(t => Node(t))]),
            new HierarchyDirection("Subtypes", async of => [.. (await RunningServer().SubtypesAsync(of)).Select(t => Node(t))]));
        return null;
    }

    /// <summary>The symbol at the caret as a hierarchy's root, or why there is none, said in the status bar: nothing
    /// there, the server not ready, or its failure.</summary>
    private async Task<(HierarchyItem? Item, string? Why)> Prepare(string what, string none, Func<LspDocument, int, Task<HierarchyItem?>> prepare)
    {
        if (Editors.Active is not { } active || csharp?.For(active) is not { } features)
        {
            return (null, Said(none));
        }
        if (features.Lsp is not { } lsp || !csharp.IsReady)
        {
            return (null, Said(csharp.Unready(what)));
        }
        try
        {
            return await prepare(lsp, Editors.CodeEditor.Selections.Primary.Caret) is { } item ? (item, null) : (null, Said(none));
        }
        catch (LanguageServerFailed failed)
        {
            return (null, Said($"{what} failed: {failed.Reason}"));
        }
    }

    /// <summary>The server running now, asked for as each level of a hierarchy opens: the window may have been shown by
    /// one since started again.</summary>
    private LanguageServer RunningServer() =>
        csharp?.Session?.Server ?? throw new LanguageServerFailed("the C# language server is not running");

    private string Said(string why)
    {
        StatusText.Text = why;
        return why;
    }

    /// <summary>An item as a row, landing at a place in it (a call) or at its name.</summary>
    private static HierarchyNode Node(HierarchyItem item, LspRange? at = null)
    {
        LspPosition where = (at ?? item.SelectionRange).Start;
        return new HierarchyNode(item, DocumentUris.ToPath(item.Uri), new TextPosition(where.Line, where.Character));
    }
}
