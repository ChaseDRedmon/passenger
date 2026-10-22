using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Passenger.App.Aspire;
using Passenger.App.Workspace;
using Passenger.Lsp;
using Passenger.Roslyn.Extension;
using Passenger.Text;

namespace Passenger.App.CSharp;

/// <summary>Aspire's help in C#, from Passenger's extension in the server, asked only in an AppHost's file or a service's
/// (<see cref="AspireRole"/>): any other file's completion, actions and declarations are the server's alone, and cost it
/// no round trip.</summary>
public sealed partial class CodeAssist
{
    /// <summary>The file shown's role, when it is a C# file of the server's.</summary>
    private AspireRole AspireRoleOfShown => editors.Active is { Syntax: not null } shown && service()?.For(shown) is { } features
        ? features.AspireRole : AspireRole.None;

    /// <summary>A resource's names where one is typed in an AppHost or a service: true once they are listed, or the list was
    /// asked for again meanwhile; false for the server's own list, also when the text changed while the extension answered.</summary>
    private async Task<bool> StartAspireCompletion(LspDocument lsp, CancellationToken cancel)
    {
        int caret = Editor.Selections.Primary.Caret;
        if (AspireRoleOfShown == AspireRole.None || editors.Active is not { } document || !MayNameResource(caret))
        {
            return false;
        }
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
        if (cancel.IsCancellationRequested)
        {
            return true;
        }
        // Typed on meanwhile: the server's list for the text as it stands, rather than none.
        if (editors.Active != document || document.Text.Version != version
            || help.Names.Count == 0 || help.Start is not { } start || help.End is not { } end)
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

    /// <summary>Whether the caret may be in a resource's name, which is a string literal's: as the parse of the text as it stands
    /// has it, or anywhere while the parse is behind.</summary>
    private bool MayNameResource(int caret)
    {
        if (Syntax is not { } parsed || !ReferenceEquals(parsed.Text, Editor.Document.Snapshot))
        {
            return true;
        }
        SyntaxToken token = parsed.Root.FindToken(Math.Max(0, caret - 1));
        return token.IsKind(SyntaxKind.StringLiteralToken) && token.SpanStart < caret && caret <= token.Span.End;
    }

    /// <summary>The WithReference fixes of a service's Aspire warnings on an offset's line, one of Passenger's own fixes
    /// (<see cref="EditorFixes"/>), listed before the server's: each adds the reference in the AppHost, worked out again by
    /// the extension as it is chosen, at the warning's place as it then stands, as one command of global undo.</summary>
    public IReadOnlyList<EditorFix> AspireFixes(OpenDocument file, int offset)
    {
        if (service()?.For(file) is not { Lsp: { } lsp } features || features.AspireRole != AspireRole.Service)
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

    /// <summary>AddProject's project, in an AppHost's or a service's file: the project's file, not its generated metadata type.</summary>
    private async Task<bool> AspireGoToProject(LspDocument document, int offset)
    {
        if (AspireRoleOfShown == AspireRole.None)
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
