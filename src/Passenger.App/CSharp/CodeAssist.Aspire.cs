using Passenger.Lsp;
using Passenger.Roslyn.Extension;
using Passenger.Text;
using Passenger.App.Workspace;

namespace Passenger.App.CSharp;

public sealed partial class CodeAssist
{
    private async Task<bool> StartAspireCompletion(LspDocument lsp, int caret, CancellationToken cancel)
    {
        if (editors.Active?.Syntax is null)
        {
            return false;
        }
        OpenDocument document = editors.Active;
        int version = document.Text.Version;
        AspireHelp help;
        try
        {
            help = await lsp.ExtensionAsync<AspireHelp>(Handlers.AspireCodeHelp, new AspireHelpRequest(lsp.PlaceOf(caret)), cancel);
        }
        catch (LanguageServerFailed)
        {
            return false;
        }
        // Not the graph's version, which every diagnostics refresh moves: the names are the server's as it answered.
        if (cancel.IsCancellationRequested || editors.Active != document || document.Text.Version != version)
        {
            return true;
        }
        if (help.Names.Count == 0 || help.Start is not { } start || help.End is not { } end)
        {
            return false;
        }
        LspRange range = new(new LspPosition(start.Line, start.Character), new LspPosition(end.Line, end.Character));
        templatesOffered.Clear();
        postfixOffered.Clear();
        open = new Completion([.. help.Names.Select(name => new CompletionItem
        {
            Label = name,
            Kind = 12,
            Detail = "Aspire resource",
            TextEdit = new LspTextEdit(range, System.Text.Json.JsonSerializer.Serialize(name)[1..^1]),
        })], lsp.OffsetOf(range.Start), [], false)
        {
            ResourceNames = true,
            Tail = document.Text.Snapshot.GetText(caret, Math.Max(0, lsp.OffsetOf(range.End) - caret)),
        };
        Filter();
        return true;
    }

    /// <summary>The WithReference fixes of a service's Aspire warnings on an offset's line, one of Passenger's own fixes
    /// (<see cref="EditorFixes"/>), listed before the server's: each adds the reference in the AppHost, worked out again by
    /// the extension as it is chosen, at the warning's place as it then stands, as one command of global undo.</summary>
    public IReadOnlyList<EditorFix> AspireFixes(OpenDocument file, int offset)
    {
        if (service()?.For(file) is not { Lsp: { } lsp } features)
        {
            return [];
        }
        return [.. features.AspireMissingOn(lsp.PositionOf(offset).Line).DistinctBy(m => (m.Service, m.Resource))
            .Select(m => Fix(m.Range, m.Service, m.Resource))];

        EditorFix Fix(TextRange range, string named, string resource) =>
            new($"Add WithReference({resource}) to {named}", "Aspire AppHost reference", range, string.Empty)
        {
            // Where its warning stands as it is chosen, while it does; the extension says when the reference is no longer missing.
            Again = _ => service()?.For(file)?.AspireMissingOn(null).FirstOrDefault(m => m.Service == named && m.Resource == resource) is { } now
                ? Fix(now.Range, named, resource) : Fix(range, named, resource),
            Elsewhere = (at, patience) => MakeAsync("Add Aspire reference", file,
                current => current.ExtensionAsync<Refactored>(Handlers.AspireReferenceFix,
                    new AspireFixRequest(current.PlaceOf(at.Start), named, resource)),
                _ => $"Added WithReference({resource}) to {named}", patience),
        };
    }

    private async Task<bool> AspireGoToProject(LspDocument document, int offset)
    {
        if (editors.Active?.Syntax is null)
        {
            return false;
        }
        AspireProjectTarget target;
        try
        {
            target = await document.ExtensionAsync<AspireProjectTarget>(
                Handlers.AspireProjectDeclaration, new Caret(document.PlaceOf(offset)));
        }
        catch (LanguageServerFailed)
        {
            // Ordinary declaration navigation still works without Passenger's extension.
            return false;
        }
        if (target.Path is not { } path || !File.Exists(path))
        {
            return false;
        }
        Navigate?.Invoke(this, (Passenger.Core.PathNames.Canonical(path), new TextPosition(0, 0)));
        return true;
    }
}
