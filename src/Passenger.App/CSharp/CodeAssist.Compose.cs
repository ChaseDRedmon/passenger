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

    /// <summary>Whether a file is a compose file the YAML server reads, whose declarations Passenger finds first.</summary>
    private static bool IsComposeFile(WebDocument web, OpenDocument document) =>
        web.Kind == WebServerKind.Yaml && ComposeDeclarations.IsCompose(document.Path);
}
