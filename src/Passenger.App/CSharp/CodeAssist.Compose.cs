using Passenger.App.Web;
using Passenger.App.Workspace;
using Passenger.Lsp;
using Passenger.Text;
using Passenger.Xaml.Features;

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

    /// <summary>Rename's box for the compose file's own name at the caret (<see cref="ComposeRename"/>), placed at it; once a new
    /// one is typed, <see cref="RenameComposeAsync"/>. False where no such name is, or the text moved on as the outline came,
    /// for the server's rename (an anchor's).</summary>
    private async Task<bool> ShowComposeRename(WebDocument web, OpenDocument document, int caret)
    {
        if (web.Lsp is not { } lsp)
        {
            return false;
        }
        int version = document.Text.Version;
        ComposeRenaming? renaming = await ComposeRenamingAsync(lsp, document, caret);
        if (renaming is null || editors.Active != document || document.Text.Version != version)
        {
            return false;
        }
        RenameBox box = new(renaming.Name);
        ChangeTracker opened = new(Editor.Document);
        ShowModal(box, renaming.At.Start, async name =>
        {
            int now = opened.MapOffset(renaming.At.Start);
            if (name is null || name == renaming.Name)
            {
                return;
            }
            RefactorOutcome made = await RenameComposeAsync(document, now, renaming.Name, name);
            Message?.Invoke(this, made.Problem ?? made.Said);
        });
        modalClosed = opened.Dispose;
        return true;
    }

    /// <summary>What Rename renames at an offset of a compose file, the outline asked of the YAML server for the text as it
    /// stands now; null where no name of the file's own is.</summary>
    internal static async Task<ComposeRenaming?> ComposeRenamingAsync(LspDocument lsp, OpenDocument document, int offset)
    {
        TextSnapshot text = document.Text.Snapshot;
        return ComposeRename.At(document.Path, text, await lsp.OutlineAsync(), offset);
    }

    /// <summary>A compose file's own name renamed from a place of it, worked out again on the text as it stands: refused when
    /// the place no longer names <paramref name="old"/>, for a name Compose would not take or one its kind has already,
    /// else made in every file that writes it (<see cref="ComposeRename.Files"/>), open or not, as one command of global
    /// undo; shared with the agents' rename.</summary>
    internal async Task<RefactorOutcome> RenameComposeAsync(OpenDocument document, int offset, string old, string name)
    {
        if (WebOf(document) is not { Lsp: { } lsp })
        {
            return RefactorOutcome.Refused($"{old} was not renamed: the YAML language server does not run for {document.Name}");
        }
        ComposeRenaming? renaming;
        try
        {
            renaming = await ComposeRenamingAsync(lsp, document, offset);
        }
        catch (LanguageServerFailed failed)
        {
            return RefactorOutcome.Refused($"{old} was not renamed: {failed.Reason}");
        }
        if (renaming is null || renaming.Name != old)
        {
            return RefactorOutcome.Refused($"{old} was not renamed: the file no longer names it there");
        }
        if (ComposeRename.Problem(renaming, name, ReadOpenOrDisk) is { } problem)
        {
            return RefactorOutcome.Refused(problem);
        }
        List<XamlFileChange> changes = [];
        List<RefactoredFile> files = [];
        foreach (string file in ComposeRename.Files(document.Path, renaming))
        {
            string text = ReadOpenOrDisk(file);
            if (ComposeRename.Edits(file, text, renaming, name) is { Count: > 0 } edits)
            {
                changes.Add(new XamlFileChange(file, now => ComposeRename.Edits(file, now, renaming, name)));
                TextSnapshot written = TextSnapshot.From(text);
                files.Add(new RefactoredFile(file, "changed", [.. edits.Select(e => written.GetLineFromOffset(e.Start) + 1).Distinct()]));
            }
        }
        if (ApplyFileChanges($"Rename {old} to {name}", changes) is { } refused)
        {
            return RefactorOutcome.Refused(refused);
        }
        return new RefactorOutcome($"Renamed the {renaming.Kind} {old} to {name} in {Passenger.Core.Plural.Of(changes.Count, "file")}", null, files, []);
    }

    /// <summary>The kind a compose file's own names are listed as: a variable's, the file's own declarations.</summary>
    private const int ComposeItemKind = 6;

    /// <summary>Whether a file is a compose file the YAML server reads, whose declarations Passenger finds first.</summary>
    private static bool IsComposeFile(WebDocument web, OpenDocument document) =>
        web.Kind == WebServerKind.Yaml && ComposeDeclarations.IsCompose(document.Path);
}
