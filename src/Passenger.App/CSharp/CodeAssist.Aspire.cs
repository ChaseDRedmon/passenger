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

    private async Task<bool> ShowAspireFixes(OpenDocument document, LspDocument lsp, int offset, string title)
    {
        if (document.Syntax is null)
        {
            return false;
        }
        int version = document.Text.Version;
        AspireHelp help;
        try
        {
            help = await lsp.ExtensionAsync<AspireHelp>(Handlers.AspireCodeHelp, new AspireHelpRequest(null));
        }
        catch (LanguageServerFailed)
        {
            return false;
        }
        if (editors.Active != document || document.Text.Version != version)
        {
            return true;
        }
        int line = lsp.PositionOf(offset).Line;
        AspireMissingReference[] missing = [.. help.Missing.Where(m => m.Start.Line <= line && line <= m.End.Line)];
        if (missing.Length == 0)
        {
            return false;
        }
        ShowModal(new ChoicePopup(title, [.. missing.Select(m => new Choice(
            $"Add WithReference({m.Resource}) to {m.Service}", "Aspire AppHost reference", m))]), offset, title, async choice =>
        {
            if (choice?.Value is not AspireMissingReference selected)
            {
                return;
            }
            // The server works the edge out again as it is chosen, so a diagnostics refresh meanwhile voids no offer.
            if (document.Text.Version != version)
            {
                Message?.Invoke(this, "The Aspire reference was not added: source changed; request the fix again");
                return;
            }
            RefactorOutcome outcome = await MakeAsync("Add Aspire reference", document,
                current => current.ExtensionAsync<Refactored>(Handlers.AspireReferenceFix,
                    new AspireFixRequest(selected.Start, selected.Service, selected.Resource)),
                _ => $"Added WithReference({selected.Resource}) to {selected.Service}", null);
            Message?.Invoke(this, outcome.Problem ?? outcome.Said);
        });
        return true;
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
