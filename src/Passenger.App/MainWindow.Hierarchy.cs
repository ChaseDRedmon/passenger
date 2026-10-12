using Passenger.App.ToolWindows;
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
        Dock.Add(new Docking.ToolWindow("hierarchy", "Hierarchy", "Icon.Hierarchy", hierarchy, Docking.ToolAnchor.RightTop));
    }

    /// <summary>Call Hierarchy: who calls the symbol at the caret, and whom it calls; null when shown, else why not.</summary>
    public async Task<string?> ShowCallHierarchyAsync()
    {
        if (await Prepare((lsp, offset) => lsp.PrepareCallHierarchyAsync(offset)) is not ({ } server, { } item))
        {
            return Said("No method, property or event at the caret");
        }
        ShowToolWindow("hierarchy");
        await Hierarchy.ShowAsync($"Calls of {item.Name}", Node(item), new HierarchyDirection("Callers", async of =>
            [.. (await server.IncomingCallsAsync(of)).Select(call => Node(call.From, call.FromRanges.FirstOrDefault()))]),
            new HierarchyDirection("Callees", async of =>
            [.. (await server.OutgoingCallsAsync(of)).Select(call => Node(call.To))]));
        return null;
    }

    /// <summary>Type Hierarchy: the type at the caret's base types and interfaces, and the types that derive from it.</summary>
    public async Task<string?> ShowTypeHierarchyAsync()
    {
        if (await Prepare((lsp, offset) => lsp.PrepareTypeHierarchyAsync(offset)) is not ({ } server, { } item))
        {
            return Said("No type at the caret");
        }
        ShowToolWindow("hierarchy");
        await Hierarchy.ShowAsync($"Hierarchy of {item.Name}", Node(item),
            new HierarchyDirection("Supertypes", async of => [.. (await server.SupertypesAsync(of)).Select(t => Node(t))]),
            new HierarchyDirection("Subtypes", async of => [.. (await server.SubtypesAsync(of)).Select(t => Node(t))]));
        return null;
    }

    private async Task<(LanguageServer? Server, HierarchyItem? Item)> Prepare(Func<LspDocument, int, Task<HierarchyItem?>> prepare)
    {
        if (Editors.Active is not { } active || csharp?.For(active)?.Lsp is not { } lsp || csharp.Session is not { } session)
        {
            return (null, null);
        }
        try
        {
            return (session.Server, await prepare(lsp, Editors.CodeEditor.Selections.Primary.Caret));
        }
        catch (Exception error) when (error is StreamJsonRpc.RemoteInvocationException or StreamJsonRpc.ConnectionLostException)
        {
            return (null, null);
        }
    }

    private string Said(string why)
    {
        StatusText.Text = why;
        return why;
    }

    /// <summary>An item as a row, landing at a place in it (a call) or at its name.</summary>
    private static HierarchyNode Node(HierarchyItem item, LspRange? at = null)
    {
        LspPosition where = (at ?? item.SelectionRange).Start;
        return new HierarchyNode(item, Workspace.PathNames.Canonical(new Uri(item.Uri).LocalPath), new TextPosition(where.Line, where.Character));
    }
}
