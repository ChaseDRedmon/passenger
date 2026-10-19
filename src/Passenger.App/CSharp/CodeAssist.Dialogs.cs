using Avalonia.Controls;
using Passenger.App.Editors;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Lsp;
using Passenger.Roslyn.Extension;
using Passenger.Text;

namespace Passenger.App.CSharp;

/// <summary>Rider's refactorings that ask their options in a dialog of their own: Change Signature, Extract Interface,
/// Move to Another File and Move to Namespace, Pull Members Up and Push Members Down. Each asks Passenger's extension in
/// the language server what is at the caret, shows its dialog, and on Next has the extension compute the change, which is
/// applied as one command that Undo takes back whole, the files it makes included.</summary>
public sealed partial class CodeAssist
{
    private async Task ChangeSignature()
    {
        const string name = "Change Signature";
        if (await DescribeAsync<Signature>(name, Handlers.DescribeSignature) is not ({ } signature, { } document, { } caret))
        {
            return;
        }
        using (caret)
        {
            if (signature.Problem is { } problem)
            {
                Message?.Invoke(this, problem);
                return;
            }
            await ShowAsync(new ChangeSignatureDialog(signature, (method, returnType, parameters) => MakeAsync(name, document,
                lsp => lsp.ExtensionAsync<Refactored>(Handlers.ChangeSignature, new SignatureChange(lsp.PlaceOf(caret.Offset), method, returnType, parameters)),
                files => $"Changed the signature of {signature.Container}{(signature.Kind == "method" ? "." + method : string.Empty)} in {Count(files, "file")}")));
        }
    }

    private async Task ExtractInterface()
    {
        const string name = "Extract Interface";
        if (await DescribeAsync<InterfaceSource>(name, Handlers.DescribeInterface) is not ({ } source, { } document, { } caret))
        {
            return;
        }
        using (caret)
        {
            if (source.Problem is { } problem)
            {
                Message?.Invoke(this, problem);
                return;
            }
            await ShowAsync(new ExtractInterfaceDialog(source, (interfaceName, file, members) => MakeAsync(name, document,
                lsp => lsp.ExtensionAsync<Refactored>(Handlers.ExtractInterface, new InterfaceExtraction(lsp.PlaceOf(caret.Offset), interfaceName, file, members)),
                _ => $"Extracted the interface {interfaceName} from {source.Type} to {file}")));
        }
    }

    /// <summary>Rider's Move on a type: a list of where it can go, then the dialog of the one chosen.</summary>
    private async Task Move()
    {
        const string name = "Move";
        if (await DescribeAsync<TypeSource>(name, Handlers.DescribeType) is not ({ } type, { } document, { } caret))
        {
            return;
        }
        if (type.Problem is { } problem)
        {
            caret.Dispose();
            Message?.Invoke(this, problem);
            return;
        }
        ShowModal(new ChoicePopup($"Move {type.Kind} '{type.Name}'",
            [new Choice("Move to Another File...", string.Empty, true, Mnemonic: '1'), new Choice("Move to Namespace...", string.Empty, false, Mnemonic: '2')]),
            caret.Offset, name, chosen =>
            {
                if (chosen?.Value is not bool toFile)
                {
                    caret.Dispose();
                    return Task.CompletedTask;
                }
                // Not awaited: the list's choice is work a screenshot waits for, and the dialog waits for the person.
                _ = MoveAsync(type, document, caret, toFile);
                return Task.CompletedTask;
            });
    }

    private async Task MoveAsync(TypeSource type, OpenDocument document, FollowedCaret caret, bool toFile)
    {
        using (caret)
        {
            string what = toFile ? "Move to Another File" : "Move to Namespace";
            await ShowAsync(toFile
                ? MoveTypeDialog.ToFile(type, file => MakeAsync(what, document,
                    lsp => lsp.ExtensionAsync<Refactored>(Handlers.MoveTypeToFile, new TypeToFile(lsp.PlaceOf(caret.Offset), file)),
                    _ => $"Moved {type.Kind} {type.Name} to {file}"))
                : MoveTypeDialog.ToNamespace(type, space => MakeAsync(what, document,
                    lsp => lsp.ExtensionAsync<Refactored>(Handlers.MoveTypeToNamespace, new TypeToNamespace(lsp.PlaceOf(caret.Offset), space)),
                    files => $"Moved {type.Kind} {type.Name} to namespace {space} in {Count(files, "file")}")));
        }
    }

    /// <summary>Rider's Pull Members Up (to a base type in the solution) or Push Members Down (to the classes derived from
    /// the one at the caret).</summary>
    private async Task MoveMembers(bool up)
    {
        string name = up ? "Pull Members Up" : "Push Members Down";
        if (await DescribeAsync<Hierarchy>(name, Handlers.DescribeHierarchy) is not ({ } hierarchy, { } document, { } caret))
        {
            return;
        }
        using (caret)
        {
            string? problem = hierarchy.Problem
                ?? (up && hierarchy.Bases.Count == 0 ? $"'{hierarchy.Type}' has no base type in this solution to pull members up to"
                : !up && hierarchy.Derived.Count == 0 ? $"No class in this solution derives from '{hierarchy.Type}' to push members down to"
                : hierarchy.Members.Count == 0 ? $"'{hierarchy.Type}' has no members to move"
                : null);
            if (problem is not null)
            {
                Message?.Invoke(this, problem);
                return;
            }
            Func<string, IReadOnlyList<string>, IReadOnlyList<string>, Task<string?>> make = (target, members, made) => MakeAsync(name, document,
                lsp => lsp.ExtensionAsync<Refactored>(up ? Handlers.PullMembersUp : Handlers.PushMembersDown, new MembersMove(lsp.PlaceOf(caret.Offset), target, members, made)),
                files => up
                    ? $"Pulled {Count(members.Count, "member")} of {hierarchy.Type} up to {hierarchy.Bases.First(b => b.Id == target).Text}"
                    : $"Pushed {Count(members.Count, "member")} of {hierarchy.Type} down to {Count(target.Split('\n').Length, "class", "classes")}");
            await ShowAsync(up ? MembersMoveDialog.PullUp(hierarchy, make) : MembersMoveDialog.PushDown(hierarchy, make));
        }
    }

    private static string Count(int count, string one, string? many = null) => count == 1 ? $"1 {one}" : $"{count} {many ?? one + "s"}";

    /// <summary>What Passenger's extension says is at the caret for a refactoring, with the file and the caret followed from
    /// now; null, said in the status bar, when the server cannot be asked.</summary>
    private async Task<(T? Found, OpenDocument? Document, FollowedCaret? Caret)> DescribeAsync<T>(string name, string handler) where T : class
    {
        if (Ask(name) is not { } lsp || editors.Active is not { } document)
        {
            return (null, null, null);
        }
        FollowedCaret caret = new(new ChangeTracker(document.Text), Caret);
        BackgroundWork.Begin();
        try
        {
            return (await lsp.ExtensionAsync<T>(handler, new Roslyn.Extension.Caret(lsp.PlaceOf(Caret))), document, caret);
        }
        catch (LanguageServerFailed failed)
        {
            caret.Dispose();
            Failed(name, failed);
            return (null, null, null);
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    private async Task ShowAsync(RefactoringDialog dialog)
    {
        if (TopLevel.GetTopLevel(Editor) is Window owner)
        {
            await dialog.ShowDialog<bool>(owner);
        }
    }

    /// <summary>A refactoring computed by Passenger's extension for a file and applied as one command, which Undo takes back
    /// whole: every file it changes, open or not, and every file it makes. Null once made, said in the status bar; else
    /// why not, for its dialog to say.</summary>
    private async Task<string?> MakeAsync(string name, OpenDocument document, Func<LspDocument, Task<Refactored>> ask, Func<int, string> done)
    {
        try
        {
            LspDocument lsp = Now(document);
            using Asked asked = WorkspaceEdits.Track(editors);
            Refactored made = await ask(lsp);
            if (made.Problem is { } problem)
            {
                return problem;
            }
            if (made.Changes.Count == 0)
            {
                Message?.Invoke(this, $"{name} changed nothing");
                return null;
            }
            System.Text.Json.JsonElement edit = RoslynExtension.WorkspaceEdit(made);
            if (await WorkspaceEdits.StaleAsync(editors, lsp, edit, asked.Since) is { } stale)
            {
                return $"{name} was not applied: {stale}";
            }
            List<string> dropped = [];
            using (CommandHistory.Recording recording = editors.Commands.Record(name))
            {
                foreach (FileChange change in made.Changes)
                {
                    recording.Watch(PathNames.Canonical(change.Path));
                }
                _ = WorkspaceEdits.Apply(editors, edit, asked, dropped);
            }
            FilesChanged?.Invoke(this, EventArgs.Empty);
            // Counted by file: a file made is its creation and its text, which the edit counts apart.
            Message?.Invoke(this, done(made.Changes.Count - dropped.Count) + (dropped.Count > 0 ? $", but not in {string.Join(", ", dropped)}: it was edited meanwhile" : string.Empty));
            return null;
        }
        catch (LanguageServerFailed failed)
        {
            return $"{name} failed: {failed.Reason}";
        }
        catch (WorkspaceEditFailed failed)
        {
            return failed.Message;
        }
    }

    /// <summary>Where the caret was as a refactoring was asked for, followed through what changes the file until its
    /// dialog is done.</summary>
    private sealed class FollowedCaret(ChangeTracker tracker, int offset) : IDisposable
    {
        public int Offset => tracker.MapOffset(offset);

        public void Dispose() => tracker.Dispose();
    }
}
