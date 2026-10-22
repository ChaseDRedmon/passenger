using Passenger.App.Web;
using Passenger.App.Workspace;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App.CSharp;

/// <summary>Go to Declaration in a compose file: Passenger's own (<see cref="ComposeDeclarations"/>) first, read from the
/// YAML server's outline, then the server's own (an alias to its anchor) where Passenger's finds nothing. An image goes to
/// its registry's page in the browser, as a link would.</summary>
public sealed partial class CodeAssist
{
    /// <summary>The solution's projects, which a Dockerfile's project is found among.</summary>
    public Func<IReadOnlyList<ProjectModel>>? Projects { get; set; }

    /// <summary>Goes where Passenger's own declarations in a compose file lead; false when they find nothing, for the
    /// server's to be asked.</summary>
    private async Task<bool> ComposeGoTo(WebDocument compose, int offset)
    {
        if (compose.Lsp is not { } lsp || editors.Active is not { } document)
        {
            return false;
        }
        try
        {
            ComposeAnswer answer = await ComposeDeclarationAsync(lsp, document, offset, ServerPlaces.Texts(editors), Projects?.Invoke() ?? []);
            if (editors.Active != document)
            {
                return true;
            }
            if (answer.Page is { } page)
            {
                Message?.Invoke(this, $"{answer.Said}: {page}");
                Browse?.Invoke(this, page);
                return true;
            }
            switch (answer.Places.Count)
            {
                case 0 when answer.Said == ComposeDeclarations.NoDeclaration:
                    return false;
                case 0:
                    Message?.Invoke(this, answer.Said);
                    return true;
                case 1:
                    Navigate?.Invoke(this, (answer.Places[0].Path, answer.Places[0].Position));
                    return true;
            }
            Func<string, TextSnapshot?> texts = ServerPlaces.Texts(editors);
            Go([.. answer.Places.Select(p => new LspLocation(DocumentUris.FromPath(p.Path), new LspRange(
                ServerPlaces.ServerPositionOf(texts(p.Path), p.Position), ServerPlaces.ServerPositionOf(texts(p.Path), p.Position))))],
                offset, "Declarations", ComposeDeclarations.NoDeclaration, [.. answer.Places.Select(p => (string?)p.Name)]);
            return true;
        }
        catch (LanguageServerFailed failed)
        {
            Failed("Go to Declaration", failed);
            return true;
        }
    }

    /// <summary>Where Passenger's own Go to Declaration goes from an offset of a compose file, the outline asked of the
    /// YAML server for the text as it stands now; shared with the agents' definition.</summary>
    internal static async Task<ComposeAnswer> ComposeDeclarationAsync(LspDocument lsp, OpenDocument document, int offset,
        Func<string, TextSnapshot?> texts, IReadOnlyList<ProjectModel> projects)
    {
        TextSnapshot text = document.Text.Snapshot;
        IReadOnlyList<DocumentSymbol> outline = await lsp.OutlineAsync();
        return ComposeDeclarations.Find(document.Path, text, outline, offset, texts, projects);
    }

    /// <summary>The registry page of the image a Dockerfile's FROM names where an offset is on it; null elsewhere.</summary>
    internal static Uri? FromImageAt(TextSnapshot text, int offset)
    {
        int line = text.GetLineFromOffset(Math.Clamp(offset, 0, text.Length));
        int column = Math.Clamp(offset, 0, text.Length) - text.GetLineStart(line);
        return FromLine().Match(text.GetLineText(line)) is { Success: true } from
            && column >= from.Groups["image"].Index && column <= from.Groups["image"].Index + from.Groups["image"].Length
            ? ComposeDeclarations.RegistryPage(from.Groups["image"].Value)
            : null;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*FROM\s+(?:--\S+\s+)*(?<image>[^\s]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex FromLine();

    /// <summary>The names a compose file offers where one is typed (<see cref="ComposeNames"/>: its services, volumes,
    /// networks, configs and secrets, read from the YAML server's outline of the text as it stands), as completion lists
    /// them before the server's own, with where the name they complete begins; null where none are typed.</summary>
    private async Task<(int Start, IReadOnlyList<CompletionItem> Items)?> ComposeCompletionAsync(WebDocument web, LspDocument lsp, TextSnapshot text, int caret)
    {
        // The outline is asked for only where a name of the file's is typed.
        if (editors.Active is not { } document || !IsComposeFile(web, document) || ComposeNames.Spot(text, caret) is not { } spot
            || ComposeNames.Takes(spot) is not (_, int start))
        {
            return null;
        }
        IReadOnlyList<DocumentSymbol> outline = await lsp.OutlineAsync();
        IReadOnlyList<ComposeName> names = ComposeNames.At(document.Path, text, outline, spot, ServerPlaces.Texts(editors));
        if (names.Count == 0)
        {
            return null;
        }
        return (start, [.. names.Select(n => new CompletionItem
        {
            Label = n.Name,
            Kind = ComposeItemKind,
            Detail = n.File is null ? n.Kind : $"{n.Kind} in {n.File}",
            // Before the server's items alike matched, which sort by their own words.
            SortText = " " + n.Name,
        })]);
    }

    /// <summary>The kind a compose file's own names are listed as: a variable's, the file's own declarations.</summary>
    private const int ComposeItemKind = 6;

    /// <summary>Whether a file is a compose file the YAML server reads, whose declarations Passenger finds first.</summary>
    private static bool IsComposeFile(WebDocument web, OpenDocument document) =>
        web.Kind == WebServerKind.Yaml && ComposeDeclarations.IsCompose(document.Path);
}
