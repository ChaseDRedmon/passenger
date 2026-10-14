using System.Text;
using System.Text.Json;
using Passenger.App.Web;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App.CSharp;

/// <summary>ESLint's fixes and Prettier's formatting, as Rider's JavaScript code quality tools give them: Show Context
/// Actions lists ESLint's fixes for the caret's line first ("ESLint: Fix 'prefer-const'"), Fix ESLint Problems fixes the
/// whole file as <c>eslint --fix</c> would, Reformat Code formats with Prettier the files its glob covers (Reformat with
/// Prettier any file it reads), and an explicit save runs both first as Settings ask. Each is one undoable edit, the caret
/// kept where the code it was in went.</summary>
public sealed partial class CodeAssist
{
    /// <summary>A page to open in the browser: an ESLint rule's.</summary>
    public event EventHandler<Uri>? Browse;

    private EslintDocument? LintOf(OpenDocument document) => Web?.Invoke()?.Eslint.For(document);

    /// <summary>Reformat with Prettier and Fix ESLint Problems, which Rider puts in the editor's menu.</summary>
    private void AddLintingActions(ActionRegistry actions)
    {
        actions.Add(new IdeAction(AppActions.ReformatWithPrettier, "Reformat with Prettier", "Code", context => _ = ReformatWithPrettier())
        {
            WhyNot = context => context.File is null ? "No file is open"
                : context.Editor.IsReadOnly ? "The file is read-only"
                : Web?.Invoke() is not { } web ? "No folder is open"
                : web.Prettier.PackageFor(context.File.Path) is null ? $"No prettier package formats {context.File.Name}: install it in the project, or name one in {PrettierService.Place}"
                : null,
        });
        actions.Add(new IdeAction(AppActions.FixEslintProblems, "Fix ESLint Problems", "Code", context => _ = FixEslintProblems())
        {
            WhyNot = context => context.File is null ? "No file is open"
                : context.Editor.IsReadOnly ? "The file is read-only"
                : LintOf(context.File) is null ? $"ESLint does not check {context.File.Name}: it needs the project's eslint package and a file Settings' Run for files covers"
                : null,
        });
    }

    /// <summary>Whether Reformat Code formats a file with Prettier ("Run on 'Reformat Code' action").</summary>
    private bool PrettierReformats(OpenDocument? file) => file is not null && Web?.Invoke()?.Prettier.OnReformat(file.Path) == true;

    /// <summary>ESLint's fixes for the problems on the caret's line; none when ESLint does not check the file.</summary>
    private async Task<IReadOnlyList<EslintFix>> LintFixes(OpenDocument document, int caret)
    {
        if (LintOf(document) is not { } lint)
        {
            return [];
        }
        try
        {
            return await lint.FixesAsync(caret);
        }
        catch (LanguageServerFailed)
        {
            // The server's own actions are still offered.
            return [];
        }
    }

    /// <summary>One of ESLint's fixes chosen: made by its server's command, which hands the window its edit; its rule's page
    /// opened for the documentation.</summary>
    private async Task RunLintFix(OpenDocument document, EslintFix fix)
    {
        if (LintOf(document) is not { } lint)
        {
            Message?.Invoke(this, $"{fix.Title} was not applied: ESLint no longer checks {document.Name}");
            return;
        }
        if (fix.OpensDocumentation)
        {
            if (lint.Diagnostics.FirstOrDefault(d => d.Code is { ValueKind: JsonValueKind.String } code && code.GetString() == fix.Rule)?.CodePage is { } page)
            {
                Browse?.Invoke(this, page);
            }
            return;
        }
        BackgroundWork.Begin();
        try
        {
            await lint.RunAsync(fix);
        }
        catch (LanguageServerFailed failed)
        {
            Failed(fix.Title, failed);
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    private async Task FixEslintProblems()
    {
        if (editors.Active is not { } document)
        {
            return;
        }
        if (LintOf(document) is not { } lint)
        {
            Message?.Invoke(this, $"ESLint does not check {document.Name}");
            return;
        }
        BackgroundWork.Begin();
        try
        {
            if (await FixAll(document, lint, "Fix ESLint Problems") is { } changed)
            {
                Message?.Invoke(this, changed ? "ESLint: fixed what it can fix" : "ESLint: nothing to fix");
            }
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    /// <summary>Every problem ESLint can fix in a file fixed, one undo step: whether the text changed, null when ESLint gave
    /// nothing or the edit no longer fits what was typed meanwhile (said in the status bar).</summary>
    private async Task<bool?> FixAll(OpenDocument document, EslintDocument lint, string what)
    {
        string before = document.Text.Snapshot.ToString();
        try
        {
            using Asked asked = WorkspaceEdits.Track(editors);
            if (await lint.FixAllAsync() is not { } edit)
            {
                Message?.Invoke(this, $"{what}: ESLint has nothing to give for {document.Name} yet");
                return null;
            }
            if (!ApplyWorkspace(edit, asked, what))
            {
                return null;
            }
            return document.Text.Snapshot.ToString() != before;
        }
        catch (LanguageServerFailed failed)
        {
            Failed(what, failed);
            return null;
        }
    }

    private async Task ReformatWithPrettier()
    {
        if (editors.Active is not { } document || Web?.Invoke() is not { } web)
        {
            return;
        }
        BackgroundWork.Begin();
        try
        {
            await Prettify(web, document, "Reformat with Prettier");
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    /// <summary>What a save does first to a changed file, as Settings ask, in Rider's order: <c>eslint --fix</c>, then
    /// Prettier. Each its own undoable edit.</summary>
    public async Task BeforeSave(OpenDocument document)
    {
        if (Web?.Invoke() is not { } web || document.IsReadOnly)
        {
            return;
        }
        if (LintOf(document) is { } lint && web.Eslint.FixesOnSave)
        {
            await FixAll(document, lint, "Run eslint --fix on save");
        }
        if (web.Prettier.OnSave(document.Path))
        {
            await Prettify(web, document, "Prettier on save");
        }
    }

    /// <summary>What came of handing a file to Prettier.</summary>
    private enum Prettified
    {
        Formatted,
        Unchanged,
        Ignored,
        Unsupported,
        Failed,
    }

    /// <summary>A file formatted by its project's Prettier as one undoable edit, only the lines that changed replaced, the
    /// caret where Prettier carried it, the file's own line breaks kept; what came of it said in the status bar as Rider's
    /// Prettier says it.</summary>
    private async Task<Prettified> Prettify(WebLanguages web, OpenDocument document, string what)
    {
        if (document.IsReadOnly)
        {
            Message?.Invoke(this, $"{what}: {document.Name} is read-only");
            return Prettified.Failed;
        }
        string text = document.Text.Snapshot.ToString();
        int version = document.Text.Version;
        int caret = editors.Active == document ? Editor.Selections.Primary.Caret : document.Selections.Primary.Caret;
        PrettierAnswer answer = await BackgroundWork.Run(() => web.Prettier.FormatAsync(document.Path, text, caret));
        if (answer.Ignored)
        {
            Message?.Invoke(this, $"Prettier: file {document.Name} was ignored with .prettierignore");
            return Prettified.Ignored;
        }
        if (answer.Unsupported)
        {
            Message?.Invoke(this, $"Prettier: file {document.Name} has unsupported type");
            return Prettified.Unsupported;
        }
        if (answer.Formatted is not { } formatted)
        {
            // A syntax error's first line says where it is ("Unexpected token (1:9)"); the code frame after it is for a console.
            Message?.Invoke(this, $"Prettier: {(answer.Error ?? "it gave nothing back").Split('\n')[0].Trim()}");
            return Prettified.Failed;
        }
        if (document.Text.Version != version)
        {
            Message?.Invoke(this, $"{what} was not applied: {document.Name} changed while Prettier formatted it");
            return Prettified.Failed;
        }
        (string after, int cursor) = InLineEndings(formatted, answer.Cursor, document.Text.LineEnding);
        IReadOnlyList<TextEdit> edits = LineDiff.Edits(text, after);
        if (edits.Count == 0)
        {
            Message?.Invoke(this, "Prettier: no lines changed. Content is already properly formatted");
            return Prettified.Unchanged;
        }
        int lines = LineDiff.Hunks(LineDiff.Lines(text), LineDiff.Lines(after)).Sum(h => Math.Max(h.OldCount, h.NewCount));
        SelectionSet carried = SelectionSet.Single(Math.Clamp(cursor, 0, after.Length));
        if (editors.Active == document)
        {
            Editor.ApplyEdits(edits, carried);
        }
        else
        {
            document.Text.BreakUndoGroup();
            document.Text.Edit(edits, document.Selections, carried);
            document.Text.BreakUndoGroup();
        }
        Message?.Invoke(this, $"Prettier: formatted {lines} line{(lines == 1 ? string.Empty : "s")}");
        return Prettified.Formatted;
    }

    /// <summary>Prettier's text in the file's own line breaks (its endOfLine is LF unless a configuration says otherwise,
    /// and the file's separator is the status bar's to change), the caret's offset carried with them.</summary>
    private static (string Text, int Cursor) InLineEndings(string formatted, int cursor, string eol)
    {
        StringBuilder built = new(formatted.Length);
        int at = -1;
        for (int i = 0; i < formatted.Length;)
        {
            if (i == cursor)
            {
                at = built.Length;
            }
            if (formatted[i] is '\r' or '\n')
            {
                int length = formatted[i] == '\r' && i + 1 < formatted.Length && formatted[i + 1] == '\n' ? 2 : 1;
                built.Append(eol);
                i += length;
            }
            else
            {
                built.Append(formatted[i]);
                i++;
            }
        }
        return (built.ToString(), at < 0 ? built.Length : at);
    }
}
