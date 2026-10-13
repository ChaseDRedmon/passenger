using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Passenger.App.Editors;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Languages.CSharp;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App.CSharp;

/// <summary>What the language server adds to the editor as you work: completion as you type, a card
/// when the pointer rests (the diagnostics there and the symbol's description), signature help after
/// an opening bracket or a comma, and the commands Rider binds (quick fixes, rename, go to
/// definition and implementation, find usages, reformat, next error). Its popups sit on a layer over
/// the editor, placed at the caret.</summary>
public sealed class CodeAssist
{
    private readonly EditorArea editors;
    private readonly Canvas layer;
    private readonly Func<CSharpService?> service;
    private readonly InfoCard hoverCard = new();
    private readonly InfoCard signatureCard = new();
    private readonly CompletionPopup completion = new();
    private readonly InfoCard itemCard = new();
    private readonly Dictionary<CompletionItem, LiveTemplate> templatesOffered = [];
    private readonly Dictionary<CompletionItem, (PostfixTemplate Template, PostfixTarget Target)> postfixOffered = [];
    private CancellationTokenSource? hovering;
    private CancellationTokenSource? completing;
    private CancellationTokenSource? describing;
    private CancellationTokenSource? signing;
    private Completion? open;
    private Control? modal;

    public CodeAssist(EditorArea editors, Canvas layer, Func<CSharpService?> service)
    {
        this.editors = editors;
        this.layer = layer;
        this.service = service;
        foreach (Control popup in new Control[] { hoverCard, signatureCard, completion, itemCard })
        {
            popup.IsVisible = false;
            layer.Children.Add(popup);
        }
        // Every editor a split makes is wired the same; what it asks is answered for the active one, which it is.
        editors.ForEachEditor(editor =>
        {
            editor.Hovered += (_, e) =>
            {
                if (editor == Editor)
                {
                    _ = ShowHover(e.Offset, e.Point);
                }
            };
            editor.HoverEnded += (_, _) => HideHover();
            editor.Typed += (_, text) =>
            {
                if (editor == Editor)
                {
                    OnTyped(text);
                }
            };
            editor.SelectionChanged += (_, _) =>
            {
                if (editor == Editor)
                {
                    OnCaretMoved();
                }
            };
            editor.NavigateRequested += (_, offset) =>
            {
                if (editor == Editor)
                {
                    _ = GoToDefinition(offset);
                }
            };
        });
        editors.ActiveChanged += (_, _) => CloseAll();
        completion.List.DoubleTapped += (_, _) => _ = Commit();
        completion.List.SelectionChanged += (_, _) => _ = DescribeSelected();
    }

    /// <summary>A place to go: a file and a line and column (from zero).</summary>
    public event EventHandler<(string Path, TextPosition Position)>? Navigate;

    /// <summary>Find Usages' answer, for the Find window: what was searched for and where it is used.</summary>
    public event EventHandler<(string Title, IReadOnlyList<LspLocation> Usages)>? UsagesFound;

    /// <summary>Something to say in the status bar ("No usages found").</summary>
    public event EventHandler<string>? Message;

    public bool IsCompletionOpen => completion.IsVisible;

    /// <summary>What the card beside the completion list says about the chosen item while it shows, null otherwise.</summary>
    public string? ItemDocumentation => itemCard.IsVisible ? itemCard.Text : null;

    /// <summary>The completion list's labels, in order, while it shows.</summary>
    public IReadOnlyList<string> CompletionLabels => completion.IsVisible ? [.. completion.Rows.Select(r => r.Item.Label)] : [];

    /// <summary>What the hover card says while it shows, null otherwise.</summary>
    public string? HoverText => hoverCard.IsVisible ? hoverCard.Text : null;

    /// <summary>The name in the rename box while it is open, null otherwise.</summary>
    public string? Renaming => modal is RenameBox box ? box.Text : null;

    /// <summary>While the debugger is paused, the value of what the pointer rests on, which the hover
    /// card shows instead of the symbol's description, as Rider does.</summary>
    public Func<int, CancellationToken, Task<string?>>? DebuggerValue { get; set; }

    private CodeEditor Editor => editors.CodeEditor;

    private CSharpDocument? Current => editors.Active is { } active ? service()?.For(active) : null;

    /// <summary>The shown file's syntax as it stands, the one parse of it everything shares; null for a file not C#.</summary>
    private ParsedText? Syntax => editors.Active?.Syntax?.Current;

    /// <summary>The server's document for the file shown, asked for each time it is used, since a server started again
    /// has a new one: null for a file that is not C#, and, said in the status bar for a command
    /// (<paramref name="what"/>), while the server cannot take it.</summary>
    private LspDocument? Ask(string? what = null)
    {
        if (Current is not { } current)
        {
            return null;
        }
        if (current.Lsp is null && what is not null && service() is { } csharp)
        {
            Message?.Invoke(this, csharp.Unready(what));
        }
        return current.Lsp;
    }

    /// <summary>An open file's server document as it is now, for what the person chose from an answer (a fix, a new
    /// name): the server that answered may have been started again while they chose.</summary>
    private LspDocument Now(OpenDocument document) => service()?.For(document)?.Lsp ?? throw new LanguageServerFailed(
        service() is { } csharp ? $"the C# language server is not ready: {csharp.State}" : "no C# language server runs for this folder");

    /// <summary>Says in the status bar why a command got nothing from the server.</summary>
    private void Failed(string what, LanguageServerFailed failed) => Message?.Invoke(this, $"{what} failed: {failed.Reason}");

    /// <summary>Keys the open popups take before the editor does: the completion list's Up, Down, Enter,
    /// Tab and Escape, and Escape for a card.</summary>
    public bool TakeKey(string action)
    {
        if (completion.IsVisible)
        {
            switch (action)
            {
                case EditorActions.Up:
                    completion.Move(-1);
                    return true;
                case EditorActions.Down:
                    completion.Move(1);
                    return true;
                case EditorActions.PageUp:
                    completion.Move(-9);
                    return true;
                case EditorActions.PageDown:
                    completion.Move(9);
                    return true;
                case EditorActions.NewLine or EditorActions.Indent:
                    _ = Commit();
                    return true;
                case EditorActions.Escape:
                    CloseCompletion();
                    return true;
            }
        }
        if (action == EditorActions.Indent && !Editor.IsInSnippet && TryExpandTemplate())
        {
            return true;
        }
        if (action == EditorActions.Escape && (signatureCard.IsVisible || hoverCard.IsVisible))
        {
            signatureCard.IsVisible = false;
            HideHover();
            return true;
        }
        return false;
    }

    /// <summary>Runs one of the code actions (<see cref="AppActions"/>' code.* names), or returns false
    /// for one that is not this.</summary>
    public bool Run(string action)
    {
        int caret = Editor.Selections.Primary.Caret;
        switch (action)
        {
            case AppActions.Complete:
                _ = StartCompletion(null);
                return true;
            case AppActions.QuickFix:
                _ = ShowFixes();
                return true;
            case AppActions.Generate:
                // Rider's Generate: the server's actions that write code for the type (constructors, equality,
                // interface members, overrides).
                _ = ShowFixes(a => GenerateWords.Any(w => a.Title.StartsWith(w, StringComparison.Ordinal)), "Generate");
                return true;
            case AppActions.RefactorThis:
                _ = ShowFixes(a => a.Kind?.StartsWith("refactor", StringComparison.Ordinal) == true, "Refactor This");
                return true;
            case AppActions.ExtractMethod:
                _ = Refactor(a => a.Title == "Extract method", "Extract Method", renameMethod: true);
                return true;
            case AppActions.IntroduceVariable:
                _ = Refactor(a => a.Title.StartsWith("Introduce local", StringComparison.Ordinal), "Introduce Variable", renameMethod: false);
                return true;
            case AppActions.Inline:
                _ = Refactor(a => a.Kind == "refactor.inline" || a.Title.StartsWith("Inline", StringComparison.Ordinal), "Inline", renameMethod: false);
                return true;
            case AppActions.Rename:
                ShowRename();
                return true;
            case AppActions.GoToDeclaration:
                _ = GoToDefinition(caret);
                return true;
            case AppActions.GoToImplementation:
                _ = GoToImplementation(caret);
                return true;
            case AppActions.FindUsages:
                _ = FindUsages(caret);
                return true;
            case AppActions.Reformat:
                _ = Reformat();
                return true;
            case AppActions.CodeCleanup:
                _ = Cleanup();
                return true;
            case AppActions.ParameterInfo:
                _ = ShowSignature();
                return true;
            case AppActions.QuickDocumentation:
                _ = ShowHover(caret, Editor.PointOf(caret));
                return true;
            case AppActions.NextError:
                StepToMarker(forward: true);
                return true;
            case AppActions.PreviousError:
                StepToMarker(forward: false);
                return true;
        }
        return false;
    }

    private static readonly string[] GenerateWords = ["Generate", "Implement", "Override", "Add parameter", "Add null check", "Create and assign"];

    /// <summary>What the open list of choices (fixes, refactorings, definitions) offers, by its words; empty with none open.</summary>
    public IReadOnlyList<string> Offered => layer.Children.OfType<ChoicePopup>().LastOrDefault()?.Titles ?? [];

    private void OnTyped(string text)
    {
        HideHover();
        if (completion.IsVisible)
        {
            if (text.All(IsIdentifierChar))
            {
                Filter();
                if (open is { IsIncomplete: true })
                {
                    // The server listed only some of what fits: what is typed now narrows it to the rest.
                    _ = StartCompletion(null, incomplete: true);
                }
                return;
            }
            if (CommitsOn(text) is { } end)
            {
                _ = CommitOn(text, end);
                return;
            }
            CloseCompletion();
        }
        AfterTyped(text);
    }

    /// <summary>Where the name typed so far ends, when what was just typed is one of the chosen item's commit characters
    /// (the server's: a dot, a bracket, a semicolon), so the item goes in before it; null otherwise. Space is not one, as
    /// in Rider, and nothing commits before a letter of the name is typed.</summary>
    private int? CommitsOn(string text)
    {
        if (open is null || text.Length != 1 || text == " " || completion.Selected is not { } row
            || templatesOffered.ContainsKey(row.Item) || postfixOffered.ContainsKey(row.Item))
        {
            return null;
        }
        int end = Editor.Selections.Primary.Caret - 1;
        if (end <= open.Start || end > Editor.Document.Snapshot.Length || Editor.Document.Snapshot[end] != text[0])
        {
            return null;
        }
        return (row.Item.CommitCharacters ?? open.CommitCharacters).Contains(text) ? end : null;
    }

    /// <summary>The chosen item goes in before the commit character typed, and the character then does what it does
    /// after any name: a dot lists members, a bracket shows the signature.</summary>
    private async Task CommitOn(string text, int end)
    {
        BackgroundWork.Begin();
        try
        {
            await CommitCore(end);
            AfterTyped(text);
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    private void AfterTyped(string text)
    {
        char last = text[^1];
        if (last == '.')
        {
            _ = StartCompletion(".");
        }
        else if (IsIdentifierChar(last) && !char.IsDigit(last) && WordStart(Editor.Selections.Primary.Caret) == Editor.Selections.Primary.Caret - 1)
        {
            _ = StartCompletion(null);
        }
        if (last is '(' or ',')
        {
            _ = ShowSignature();
        }
        else if (last == ')' || last == ';')
        {
            signing?.Cancel();
            signatureCard.IsVisible = false;
        }
    }

    /// <summary>Filters once the move's input is done: a typed character moves the caret before the editor says what was
    /// typed, and a commit character must find the list still open.</summary>
    private void OnCaretMoved()
    {
        if (completion.IsVisible)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(Filter);
        }
    }

    private async Task StartCompletion(string? trigger, bool incomplete = false)
    {
        BackgroundWork.Begin();
        try
        {
            await StartCompletionCore(trigger, incomplete);
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    private async Task StartCompletionCore(string? trigger, bool incomplete)
    {
        if (Current?.Lsp is not { } lsp)
        {
            return;
        }
        completing?.Cancel();
        CancellationTokenSource cancel = new();
        completing = cancel;
        int caret = Editor.Selections.Primary.Caret;
        try
        {
            (CompletionList? list, _) = await lsp.CompletionAsync(caret, trigger, cancel.Token, incomplete);
            if (cancel.IsCancellationRequested || list is null || list.Items.Count == 0)
            {
                if (!cancel.IsCancellationRequested)
                {
                    CloseCompletion();
                }
                return;
            }
            int start = list.Items.FirstOrDefault(i => i.TextEdit is not null)?.TextEdit is { } edit
                ? lsp.OffsetOf(edit.Range.Start)
                : WordStart(caret);
            // Rider lists the live templates that may go here with the server's items, ahead of a keyword they share.
            TemplateScope scope = Syntax is { } syntax ? TemplateScopes.At(syntax, caret).Scope : TemplateScope.None;
            templatesOffered.Clear();
            foreach (LiveTemplate template in LiveTemplates.In(scope))
            {
                templatesOffered[new CompletionItem { Label = template.Abbreviation, Kind = 15, Detail = template.Description, SortText = "!" + template.Abbreviation }] = template;
            }
            // After an expression and a dot, the postfix templates that may wrap it, after its members.
            postfixOffered.Clear();
            foreach ((PostfixTemplate postfix, PostfixTarget target) in Postfixes(Math.Min(start, caret), caret))
            {
                postfixOffered[new CompletionItem { Label = postfix.Name, Kind = 15, Detail = postfix.Example, SortText = "~" + postfix.Name }] = (postfix, target);
            }
            open = new Completion([.. list.Items, .. templatesOffered.Keys, .. postfixOffered.Keys], Math.Min(start, Editor.Selections.Primary.Caret),
                list.ItemDefaults?.CommitCharacters ?? [], list.IsIncomplete);
            Filter();
        }
        catch (OperationCanceledException)
        {
            // Typed on: the list is asked for again.
        }
        catch (LanguageServerFailed)
        {
            // No list, as with nothing to offer; typing on asks again.
        }
    }

    /// <summary>The list narrowed to what has been typed since it opened: prefix matches first (an
    /// exact-case one first of those), then CamelHumps, then anywhere in the name.</summary>
    private void Filter()
    {
        if (open is null)
        {
            return;
        }
        TextSnapshot text = Editor.Document.Snapshot;
        int caret = Editor.Selections.Primary.Caret;
        if (caret < open.Start || Editor.Selections.HasMultiple)
        {
            CloseCompletion();
            return;
        }
        string typed = text.GetText(open.Start, caret - open.Start);
        if (!typed.All(IsIdentifierChar))
        {
            CloseCompletion();
            return;
        }
        List<CompletionRow> rows = open.Items
            .Select(item => (item, score: Score(typed, item.FilterText ?? item.Label)))
            .Where(x => x.score is not null)
            .OrderByDescending(x => x.score)
            .ThenByDescending(x => x.item.Preselect == true)
            .ThenBy(x => x.item.SortText ?? x.item.Label, StringComparer.Ordinal)
            .Take(200)
            .Select(x => Row(x.item))
            .ToList();
        if (rows.Count == 0)
        {
            CloseCompletion();
            return;
        }
        completion.Show(rows);
        completion.IsVisible = true;
        Place(completion, open.Start, above: false);
    }

    private async Task Commit()
    {
        BackgroundWork.Begin();
        try
        {
            await CommitCore();
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    /// <summary>The chosen item in place of the name typed, up to <paramref name="end"/> (the caret when not given).</summary>
    private async Task CommitCore(int? end = null)
    {
        if (open is null || completion.Selected is not { } row || Current?.Lsp is not { } lsp)
        {
            return;
        }
        int start = open.Start;
        CloseCompletion();
        CompletionItem item = row.Item;
        if (templatesOffered.TryGetValue(item, out LiveTemplate? chosen))
        {
            Expand(chosen, start, Editor.Selections.Primary.Caret);
            return;
        }
        if (postfixOffered.TryGetValue(item, out (PostfixTemplate Template, PostfixTarget Target) postfix))
        {
            ExpandPostfix(postfix.Template, postfix.Target, Editor.Selections.Primary.Caret);
            return;
        }
        TextDocument document = Editor.Document;
        using ChangeTracker asked = new(document);
        int typed = end ?? Editor.Selections.Primary.Caret;
        try
        {
            if (item.Data is not null && item.AdditionalTextEdits is null)
            {
                item = await lsp.ResolveAsync(item) ?? item;
            }
        }
        catch (LanguageServerFailed)
        {
            // The item goes in as listed, without what resolving would have added (a using it needs).
        }
        // The editor shows another file now: the item was for the one it showed.
        if (Editor.Document != document)
        {
            return;
        }
        if (ClientCommands.CompletionEdit(item) is { } complex)
        {
            CommitWhole(asked, complex, start, typed);
            return;
        }
        start = asked.MapOffset(start);
        int caret = Math.Min(asked.MapOffset(typed), Editor.Document.Snapshot.Length);
        string insert = item.TextEdit?.NewText ?? item.TextEditText ?? item.InsertText ?? item.Label;
        List<Passenger.Text.TextEdit> edits = [new Passenger.Text.TextEdit(start, Math.Max(0, caret - start), insert)];
        if (item.AdditionalTextEdits is { Count: > 0 } extra && WorkspaceEdits.Carried(asked, extra) is { } placed)
        {
            edits.AddRange(placed.Where(e => e.End <= start || e.Start >= caret));
        }
        Editor.ApplyEdits(LineEndings.Fit(document.Snapshot, edits, document.LineEnding));
        if (item.Command is { } command)
        {
            Message?.Invoke(this, NotRun(item.Label, command));
        }
    }

    /// <summary>A completion whose code the server writes whole (an override's body, the async an await needs in its
    /// method): its edit in place of the name typed from <paramref name="start"/> to <paramref name="typed"/>, answered for
    /// the text <paramref name="asked"/> began with, and the caret where the server puts it.</summary>
    private void CommitWhole(ChangeTracker asked, ComplexEdit complex, int start, int typed)
    {
        ServerLines lines = ServerLines.Of(asked.Snapshot);
        int from = lines.OffsetOf(complex.Edit.Range.Start);
        int to = lines.OffsetOf(complex.Edit.Range.End);
        string written = complex.Edit.NewText;
        string eol = asked.Document.LineEnding;
        // Roslyn's edit covers the name as it stood when resolved; one that meets the name takes all of what was typed.
        (int first, int last) = from <= typed && to >= start ? (Math.Min(from, start), Math.Max(to, typed)) : (from, to);
        if (LineEndings.Fit(asked.Snapshot, [new Passenger.Text.TextEdit(first, last - first, written)], eol) is not [Passenger.Text.TextEdit made])
        {
            return;
        }
        if (asked.Map([made]) is not [Passenger.Text.TextEdit placed])
        {
            Message?.Invoke(this, "The completion was not inserted: the code it changes was edited meanwhile");
            return;
        }
        // The server's caret is an offset in the text its edit leaves, which starts as this edit's does, or after a '\r' it took in.
        int into = complex.Caret is { } caret && caret - from >= 0 && caret - from <= written.Length
            ? Math.Min(LineEndings.In(asked.Snapshot.GetText(made.Start, first - made.Start) + written[..(caret - from)], eol).Length, made.Text.Length)
            : made.Text.Length;
        Editor.ApplyEdits([placed], SelectionSet.Single(placed.Start + into));
    }

    /// <summary>Rider's abbreviation and Tab: the word before the caret, when a live template has it as its
    /// abbreviation and may go here, becomes the template. Whether one did.</summary>
    private bool TryExpandTemplate()
    {
        if (Current is null || Editor.Selections.HasMultiple || !Editor.Selections.Primary.IsEmpty)
        {
            return false;
        }
        int caret = Editor.Selections.Primary.Caret;
        int start = WordStart(caret);
        if (start == caret)
        {
            return false;
        }
        string word = Editor.Document.Snapshot.GetText(start, caret - start);
        if (Postfixes(start, caret).FirstOrDefault(p => p.Template.Name == word) is { Template: { } postfix } found)
        {
            ExpandPostfix(postfix, found.Target, caret);
            return true;
        }
        TemplateScope scope = Syntax is { } syntax ? TemplateScopes.At(syntax, caret).Scope : TemplateScope.None;
        if (LiveTemplates.In(scope).FirstOrDefault(t => t.Abbreviation == word) is not { } template)
        {
            return false;
        }
        Expand(template, start, caret);
        return true;
    }

    /// <summary>The postfix templates that may wrap the expression before a dot ending just before a word, and what
    /// each would wrap; none when no dot or no expression is there.</summary>
    private IEnumerable<(PostfixTemplate Template, PostfixTarget Target)> Postfixes(int word, int caret)
    {
        if (word < 1 || Syntax is not { } syntax || word > syntax.Text.Length || syntax.Text[word - 1] != '.'
            || PostfixTemplates.Before(syntax, word - 1, caret) is not { } target)
        {
            return [];
        }
        return PostfixTemplates.All.Where(t => !t.Statement || target.WholeStatement).Select(t => (t, target));
    }

    /// <summary>A postfix template in place of its expression, the dot and the name typed after it.</summary>
    private void ExpandPostfix(PostfixTemplate template, PostfixTarget target, int caret)
    {
        int start = template.Statement ? target.Start : target.ChainStart;
        string expression = (template.Statement ? target.Text : target.ChainText).Replace("$", "$$", StringComparison.Ordinal);
        TextSnapshot text = Editor.Document.Snapshot;
        int lineStart = text.GetLineStart(text.GetLineFromOffset(start));
        string before = text.GetText(lineStart, start - lineStart);
        string indent = before[..(before.Length - before.TrimStart().Length)];
        string unit = Editor.Indent.UseTabs ? "\t" : new string(' ', Editor.Indent.Size);
        Dictionary<string, string> defaults = template.Defaults.ToDictionary(d => d.Key, d => d.Value == "$name" ? target.Name : d.Value);
        Editor.InsertSnippet(TextRange.FromBounds(start, caret), Snippet.Parse(template.Text.Replace("$EXPR$", expression, StringComparison.Ordinal), defaults, indent, unit));
    }

    /// <summary>Puts a live template over its abbreviation, indented as the line it is on, and starts filling it in.</summary>
    private void Expand(LiveTemplate template, int start, int end)
    {
        TextSnapshot text = Editor.Document.Snapshot;
        string line = text.GetText(text.GetLineStart(text.GetLineFromOffset(start)), start - text.GetLineStart(text.GetLineFromOffset(start)));
        string indent = line[..(line.Length - line.TrimStart().Length)];
        string unit = Editor.Indent.UseTabs ? "\t" : new string(' ', Editor.Indent.Size);
        string? typeName = Syntax is { } syntax ? TemplateScopes.At(syntax, end).TypeName : null;
        Dictionary<string, string> defaults = template.Defaults.ToDictionary(d => d.Key, d => d.Value == LiveTemplates.TypeName ? typeName ?? "Name" : d.Value);
        Editor.InsertSnippet(TextRange.FromBounds(start, end), Snippet.Parse(template.Text, defaults, indent, unit));
    }

    private void CloseCompletion()
    {
        completing?.Cancel();
        describing?.Cancel();
        open = null;
        completion.IsVisible = false;
        itemCard.IsVisible = false;
    }

    /// <summary>Rider's documentation beside the completion list: the chosen item's signature and summary, resolved
    /// once the choice rests a moment.</summary>
    private async Task DescribeSelected()
    {
        describing?.Cancel();
        itemCard.IsVisible = false;
        if (!completion.IsVisible || completion.Selected is not { } row || Current?.Lsp is not { } lsp)
        {
            return;
        }
        CancellationTokenSource cancel = new();
        describing = cancel;
        BackgroundWork.Begin();
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(120), cancel.Token);
            CompletionItem item = row.Item;
            if (item.Data is not null && item.Documentation is null)
            {
                item = await lsp.ResolveAsync(item, cancel.Token) ?? item;
            }
            if (cancel.IsCancellationRequested || !completion.IsVisible || completion.Selected != row)
            {
                return;
            }
            itemCard.Clear();
            if (item.Detail is { Length: > 0 } detail)
            {
                itemCard.AddCode(detail);
            }
            if (Shapes.Text(item.Documentation) is { Length: > 0 } documentation)
            {
                itemCard.AddMarkdown(documentation);
            }
            if (itemCard.IsEmpty)
            {
                return;
            }
            itemCard.IsVisible = true;
            PlaceBeside(itemCard, completion);
        }
        catch (OperationCanceledException)
        {
            // Another item chosen, or the list closed.
        }
        catch (LanguageServerFailed)
        {
            // No card beside the list.
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    /// <summary>A card to the right of a popup, at its top; to its left when the right has no room.</summary>
    private void PlaceBeside(Control card, Control beside)
    {
        card.Measure(Size.Infinity);
        double left = Canvas.GetLeft(beside);
        double width = card.DesiredSize.Width;
        double x = left + beside.Bounds.Width + 4;
        if (x + width > layer.Bounds.Width && left - width - 4 >= 0)
        {
            x = left - width - 4;
        }
        Canvas.SetLeft(card, x);
        Canvas.SetTop(card, Canvas.GetTop(beside));
    }

    private async Task ShowHover(int offset, Point point)
    {
        BackgroundWork.Begin();
        try
        {
            await ShowHoverCore(offset, point);
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    private async Task ShowHoverCore(int offset, Point point)
    {
        hovering?.Cancel();
        CancellationTokenSource cancel = new();
        hovering = cancel;
        hoverCard.Clear();
        foreach (Marker marker in Editor.MarkersAt(offset))
        {
            hoverCard.AddText(marker.Message, marker.Kind switch
            {
                MarkerKind.Error => InfoCard.Brush("Passenger.Editor.Error"),
                MarkerKind.Warning => InfoCard.Brush("Passenger.Editor.Warning"),
                _ => null,
            });
        }
        string? debuggerValue = DebuggerValue is { } value ? await value(offset, cancel.Token) : null;
        if (cancel.IsCancellationRequested)
        {
            return;
        }
        if (debuggerValue is not null)
        {
            hoverCard.AddCode(debuggerValue);
        }
        else if (Current?.Lsp is { } lsp)
        {
            try
            {
                Hover? hover = await lsp.HoverAsync(offset, cancel.Token);
                if (cancel.IsCancellationRequested)
                {
                    return;
                }
                if (hover is not null && Shapes.Text(hover.Contents) is { Length: > 0 } markdown)
                {
                    hoverCard.AddMarkdown(markdown);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (LanguageServerFailed)
            {
                // The card says what the markers there say, without the symbol's description.
            }
        }
        if (hoverCard.IsEmpty || completion.IsVisible)
        {
            hoverCard.IsVisible = false;
            return;
        }
        hoverCard.IsVisible = true;
        TextSnapshot text = Editor.Document.Snapshot;
        int lineStart = text.GetLineStart(text.GetLineFromOffset(Math.Min(offset, text.Length)));
        Point anchor = Editor.PointOf(lineStart);
        Place(hoverCard, new Point(point.X, anchor.Y), above: false);
    }

    private void HideHover()
    {
        hovering?.Cancel();
        hoverCard.IsVisible = false;
    }

    private async Task ShowSignature()
    {
        BackgroundWork.Begin();
        try
        {
            await ShowSignatureCore();
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    private async Task ShowSignatureCore()
    {
        if (Current?.Lsp is not { } lsp)
        {
            return;
        }
        int caret = Editor.Selections.Primary.Caret;
        TextDocument document = Editor.Document;
        signing?.Cancel();
        CancellationTokenSource cancel = new();
        signing = cancel;
        try
        {
            SignatureHelp? help = await lsp.SignatureHelpAsync(caret, cancel.Token);
            // A late answer is for a call the caret has left, or a file no longer shown.
            if (cancel.IsCancellationRequested || Editor.Document != document)
            {
                return;
            }
            if (help is null || help.Signatures.Count == 0)
            {
                signatureCard.IsVisible = false;
                return;
            }
            SignatureInformation signature = help.Signatures[Math.Clamp(help.ActiveSignature ?? 0, 0, help.Signatures.Count - 1)];
            signatureCard.Clear();
            signatureCard.AddCode(signature.Label);
            int active = signature.ActiveParameter ?? help.ActiveParameter ?? -1;
            if (signature.Parameters is { } parameters && active >= 0 && active < parameters.Count
                && Shapes.Text(parameters[active].Documentation) is { Length: > 0 } about)
            {
                signatureCard.AddText(about);
            }
            else if (Shapes.Text(signature.Documentation) is { Length: > 0 } documentation)
            {
                signatureCard.AddText(documentation);
            }
            signatureCard.IsVisible = true;
            Place(signatureCard, caret, above: true);
        }
        catch (OperationCanceledException)
        {
            // The caret left the call, or asked again.
        }
        catch (LanguageServerFailed)
        {
            // No signature shown; the next bracket or comma asks again.
        }
    }

    private async Task ShowFixes(Func<CodeAction, bool>? only = null, string title = "Fixes and refactorings")
    {
        BackgroundWork.Begin();
        try
        {
            await ShowFixesCore(only, title);
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    private async Task ShowFixesCore(Func<CodeAction, bool>? only, string title)
    {
        string what = only is null ? "Show Context Actions" : title;
        if (Ask(what) is not { } lsp || Current is not { } current || editors.Active is not { } document)
        {
            return;
        }
        Selection primary = Editor.Selections.Primary;
        int line = lsp.PositionOf(primary.Caret).Line;
        List<Diagnostic> here = current.Diagnostics
            .Where(d => d.Range.Start.Line <= line && line <= d.Range.End.Line)
            .ToList();
        try
        {
            IReadOnlyList<CodeAction> actions = [.. (await lsp.CodeActionsAsync(primary.Range, here)).Where(a => a.Disabled is null && (only?.Invoke(a) ?? true))];
            if (actions.Count == 0)
            {
                Message?.Invoke(this, only is null ? "No fixes or refactorings here" : "No refactorings here");
                return;
            }
            List<Choice> choices = [.. actions.Select(a => new Choice(a.Title, a.Kind ?? string.Empty, a))];
            ShowModal(new ChoicePopup(title, choices), primary.Caret, what, choice =>
                choice?.Value is CodeAction chosen ? Carry(document, chosen, primary.Caret, chosen.Title, null) : Task.CompletedTask);
        }
        catch (LanguageServerFailed failed)
        {
            Failed(what, failed);
        }
    }

    /// <summary>A code action chosen, carried out: one that offers choices (a fix-all's scopes, the actions nested under
    /// it) offers them as a list at <paramref name="at"/> and applies the one chosen there; any other is applied at once.
    /// <paramref name="applied"/> runs once its change is made.</summary>
    private async Task Carry(OpenDocument document, CodeAction action, int at, string what, Asked? listed, Action? applied = null)
    {
        IReadOnlyList<ActionChoice> choices = ClientCommands.Choices(action);
        if (choices.Count == 0)
        {
            if (await Apply(document, action, null, what, listed))
            {
                applied?.Invoke();
            }
            return;
        }
        ShowModal(new ChoicePopup(action.Title, [.. choices.Select(c => new Choice(c.Text, string.Empty, c))]), at, what, async choice =>
        {
            if (choice?.Value is ActionChoice chosen && await Apply(document, chosen.Action, chosen.Scope, what, null))
            {
                applied?.Invoke();
            }
        });
    }

    /// <summary>A code action's change made: resolved first when the server left it for later (a fix-all's over the scope
    /// chosen), by the server running now, its edit applied, and a command it still carries, which Passenger cannot run,
    /// said in the status bar, as is a server that cannot resolve it. <paramref name="listed"/> follows the files from
    /// when the actions were asked for. Whether every file took it.</summary>
    private async Task<bool> Apply(OpenDocument document, CodeAction action, string? scope, string what, Asked? listed)
    {
        bool resolve = (action.Edit is null && action.Data is not null) || scope is not null;
        // An answer resolved now is for the text as it is now, and carried through what is typed while it comes.
        using Asked? resolving = resolve ? WorkspaceEdits.Track(editors) : null;
        CodeAction chosen;
        try
        {
            chosen = resolve ? await Now(document).ResolveChosenAsync(action, scope) : action;
        }
        catch (LanguageServerFailed failed)
        {
            Message?.Invoke(this, $"{what} was not applied: {failed.Reason}");
            return false;
        }
        bool applied = chosen.Edit is { } edit && ApplyWorkspace(edit, resolving ?? listed, what);
        if (chosen.Command is { } command)
        {
            Message?.Invoke(this, NotRun(what, command));
        }
        else if (chosen.Edit is null)
        {
            Message?.Invoke(this, $"{what} changed nothing");
        }
        return applied;
    }

    /// <summary>What the status bar says of a command left unrun: Roslyn's own are run, and it registers no other.</summary>
    private static string NotRun(string what, LspCommand command) => $"{what} needs {command.Command}, which Passenger cannot run";

    /// <summary>One of Rider's refactorings by its own key, over the selection: the language server's action of that
    /// kind applied at once, and for Extract Method its new method's name then offered to rename, as Rider asks for it.</summary>
    private async Task Refactor(Func<CodeAction, bool> pick, string name, bool renameMethod)
    {
        if (Ask(name) is not { } lsp || editors.Active is not { } document)
        {
            return;
        }
        BackgroundWork.Begin();
        try
        {
            Selection primary = Editor.Selections.Primary;
            using Asked asked = WorkspaceEdits.Track(editors);
            using ExtractedMethod? made = renameMethod && document.Syntax is { } syntax ? new ExtractedMethod(Editor.Document, syntax, primary) : null;
            CodeAction? action = (await lsp.CodeActionsAsync(primary.Range, [])).Where(a => a.Disabled is null).FirstOrDefault(pick);
            if (action is null)
            {
                Message?.Invoke(this, $"{name} is not available here{(primary.IsEmpty && renameMethod ? ": select the code first" : string.Empty)}");
                return;
            }
            // Choices the server nests under the action (Introduce Variable's occurrences) are offered as a list first.
            await Carry(document, action, primary.Caret, name, asked, () =>
            {
                Message?.Invoke(this, $"{name} done");
                // The call that replaced the selection is where the new name is typed, in the file still shown.
                if (made is not null && made.Document == Editor.Document && made.Call() is { } call)
                {
                    Editor.Selections = SelectionSet.Single(call);
                    ShowRename();
                }
            });
        }
        catch (LanguageServerFailed failed)
        {
            Failed(name, failed);
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    private void ShowRename()
    {
        if (Ask("Rename") is null || editors.Active is not { } document)
        {
            return;
        }
        TextSnapshot text = Editor.Document.Snapshot;
        int caret = Editor.Selections.Primary.Caret;
        TextRange word = TextNavigation.WordAt(text, caret);
        if (word.Length == 0 || !TextNavigation.IsWordChar(text[word.Start]))
        {
            return;
        }
        RenameBox box = new(text.GetText(word));
        ChangeTracker opened = new(Editor.Document);
        ShowModal(box, word.Start, async name =>
        {
            int at = opened.MapOffset(word.Start);
            opened.Dispose();
            if (name is null)
            {
                return;
            }
            try
            {
                using Asked asked = WorkspaceEdits.Track(editors);
                // Asked of the server running now: the one there as the box opened may have been started again since.
                if (await Now(document).RenameAsync(at, name) is { } edit)
                {
                    List<string> dropped = [];
                    int files = WorkspaceEdits.Apply(editors, edit, asked, dropped);
                    Message?.Invoke(this, dropped.Count > 0 ? $"Renamed, but not in {string.Join(", ", dropped)}: it was edited meanwhile"
                        : files > 1 ? $"Renamed in {files} files" : "Renamed");
                }
                else
                {
                    Message?.Invoke(this, "This can't be renamed");
                }
            }
            catch (LanguageServerFailed failed)
            {
                Failed("Rename", failed);
            }
            catch (WorkspaceEditFailed failed)
            {
                Message?.Invoke(this, $"Rename failed: {failed.Message}");
            }
        });
    }

    private async Task GoToDefinition(int offset)
    {
        BackgroundWork.Begin();
        try
        {
            await GoToDefinitionCore(offset);
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    private async Task GoToDefinitionCore(int offset)
    {
        if (Ask("Go to Declaration") is not { } lsp)
        {
            return;
        }
        try
        {
            Go(await lsp.DefinitionAsync(offset), offset, "Declarations", "Cannot find a declaration to go to");
        }
        catch (LanguageServerFailed failed)
        {
            Failed("Go to Declaration", failed);
        }
    }

    private async Task GoToImplementation(int offset)
    {
        BackgroundWork.Begin();
        try
        {
            await GoToImplementationCore(offset);
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    private async Task GoToImplementationCore(int offset)
    {
        if (Ask("Go to Implementation") is not { } lsp)
        {
            return;
        }
        try
        {
            Go(await lsp.ImplementationAsync(offset), offset, "Implementations", "No implementations found");
        }
        catch (LanguageServerFailed failed)
        {
            Failed("Go to Implementation", failed);
        }
    }

    private async Task FindUsages(int offset)
    {
        BackgroundWork.Begin();
        try
        {
            await FindUsagesCore(offset);
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    private async Task FindUsagesCore(int offset)
    {
        if (Ask("Find Usages") is not { } lsp)
        {
            return;
        }
        try
        {
            IReadOnlyList<LspLocation> usages = await lsp.ReferencesAsync(offset);
            if (usages.Count == 0)
            {
                Message?.Invoke(this, "No usages found");
                return;
            }
            int start = WordStart(offset);
            int end = offset;
            while (end < Editor.Document.Snapshot.Length && IsIdentifierChar(Editor.Document.Snapshot[end]))
            {
                end++;
            }
            string name = Editor.Document.Snapshot.GetText(start, end - start);
            UsagesFound?.Invoke(this, ($"Usages of {(name.Length > 0 ? name : "symbol")}", usages));
        }
        catch (LanguageServerFailed failed)
        {
            Failed("Find Usages", failed);
        }
    }

    private void Go(IReadOnlyList<LspLocation> locations, int offset, string title, string none)
    {
        if (locations.Count == 0)
        {
            Message?.Invoke(this, none);
        }
        else if (locations.Count == 1)
        {
            Arrive(locations[0]);
        }
        else
        {
            Pick(title, locations, offset);
        }
    }

    private void Pick(string title, IReadOnlyList<LspLocation> locations, int offset)
    {
        string root = service()?.Session?.Folder ?? string.Empty;
        List<Choice> choices = locations.Select(l =>
        {
            string path = DocumentUris.ToPath(l.Uri);
            string shown = root.Length > 0 && PathNames.IsUnder(path, root) ? Path.GetRelativePath(root, path) : Path.GetFileName(path);
            return new Choice($"{shown}:{l.Range.Start.Line + 1}", LinePreview(path, l.Range.Start.Line), l, DetailIsCode: true);
        }).ToList();
        ShowModal(new ChoicePopup(title, choices), offset, title, choice =>
        {
            if (choice?.Value is LspLocation location)
            {
                Arrive(location);
            }
            return Task.CompletedTask;
        });
    }

    private void Arrive(LspLocation location) =>
        Navigate?.Invoke(this, (DocumentUris.ToPath(location.Uri), new TextPosition(location.Range.Start.Line, location.Range.Start.Character)));

    private async Task Reformat()
    {
        BackgroundWork.Begin();
        try
        {
            await ReformatCore();
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    private async Task ReformatCore()
    {
        if (Ask("Reformat Code") is not { } lsp || editors.Active is not { } document)
        {
            return;
        }
        try
        {
            using ChangeTracker asked = new(document.Text);
            IReadOnlyList<LspTextEdit> edits = await lsp.FormatAsync(Editor.Indent);
            if (ApplyAnswer(document, asked, edits, "Reformat Code"))
            {
                Message?.Invoke(this, edits.Count == 0 ? "Already formatted" : "Reformatted");
            }
        }
        catch (LanguageServerFailed failed)
        {
            Failed("Reformat Code", failed);
        }
    }

    private async Task Cleanup()
    {
        BackgroundWork.Begin();
        try
        {
            await CleanupCore();
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    /// <summary>Rider's Code Cleanup on the file, through the language server's own fixes: the unnecessary usings
    /// removed (IDE0005's fix), the usings sorted, then the file reformatted.</summary>
    private async Task CleanupCore()
    {
        if (Ask("Code Cleanup") is not { } lsp || Current is not { } current || editors.Active is not { } document)
        {
            return;
        }
        List<string> done = [];
        try
        {
            List<Diagnostic> unused = [.. current.Diagnostics.Where(d => d.Code is { ValueKind: System.Text.Json.JsonValueKind.String } code && code.GetString() == "IDE0005")];
            if (unused.Count > 0 && await ApplyNamed(lsp, document, LineRange(Editor.Document.Snapshot.GetLineFromOffset(lsp.OffsetOf(unused[0].Range.Start))), unused, "Remove unnecessary usings"))
            {
                done.Add("removed unused usings");
            }
            TextSnapshot text = Editor.Document.Snapshot;
            int usingLine = Enumerable.Range(0, text.LineCount).FirstOrDefault(l => text.GetLineText(l).TrimStart().StartsWith("using ", StringComparison.Ordinal) && text.GetLineText(l).TrimEnd().EndsWith(';'), -1);
            if (usingLine >= 0)
            {
                string before = text.ToString();
                if (await ApplyNamed(lsp, document, LineRange(usingLine), [], "Sort Usings") && Editor.Document.Snapshot.ToString() != before)
                {
                    done.Add("sorted the usings");
                }
            }
            string unformatted = document.Text.Snapshot.ToString();
            using ChangeTracker asked = new(document.Text);
            IReadOnlyList<LspTextEdit> edits = await lsp.FormatAsync(Editor.Indent);
            if (ApplyAnswer(document, asked, edits, "Code Cleanup") && document.Text.Snapshot.ToString() != unformatted)
            {
                done.Add("reformatted");
            }
            Message?.Invoke(this, done.Count == 0 ? "Already clean" : $"Code cleanup {string.Join(", ", done)}");
        }
        catch (LanguageServerFailed failed)
        {
            Failed("Code Cleanup", failed);
        }

        TextRange LineRange(int line)
        {
            LineSpan span = Editor.Document.Snapshot.GetLine(line);
            return new TextRange(span.Start, span.Length);
        }
    }

    /// <summary>Runs the server's action of that title at a range, if it offers one; whether it did.</summary>
    private async Task<bool> ApplyNamed(LspDocument lsp, OpenDocument document, TextRange range, IReadOnlyList<Diagnostic> diagnostics, string title)
    {
        using Asked asked = WorkspaceEdits.Track(editors);
        IReadOnlyList<CodeAction> actions = await lsp.CodeActionsAsync(range, diagnostics);
        if (actions.FirstOrDefault(a => a.Title == title && a.Disabled is null) is not { } action)
        {
            return false;
        }
        return await Apply(document, action, null, title, asked);
    }

    /// <summary>A workspace edit applied, its open files' edits carried through what was typed since the request; one
    /// whose edited text changed meanwhile is left alone and named in the status bar. Whether every file took it.</summary>
    private bool ApplyWorkspace(System.Text.Json.JsonElement edit, Asked? asked, string what)
    {
        List<string> dropped = [];
        try
        {
            WorkspaceEdits.Apply(editors, edit, asked, dropped);
        }
        catch (WorkspaceEditFailed failed)
        {
            Message?.Invoke(this, $"{what} was not applied: {failed.Message}");
            return false;
        }
        if (dropped.Count > 0)
        {
            Message?.Invoke(this, $"{what} was not applied to {string.Join(", ", dropped)}: it was edited meanwhile");
        }
        return dropped.Count == 0;
    }

    /// <summary>Edits answered for a document's text as it was asked about, applied to that document as it is now (through
    /// the editor when it shows it), never to whatever file the editor shows by then; refused, said in the status bar,
    /// when the code they change was edited meanwhile.</summary>
    private bool ApplyAnswer(OpenDocument document, ChangeTracker asked, IReadOnlyList<LspTextEdit> answer, string what)
    {
        if (!editors.Documents.Contains(document))
        {
            return false;
        }
        if (WorkspaceEdits.Carried(asked, answer) is not { } edits)
        {
            Message?.Invoke(this, $"{what} was not applied: the code it changes was edited meanwhile");
            return false;
        }
        if (edits.Count > 0)
        {
            WorkspaceEdits.ApplyTo(editors, document, edits);
        }
        return true;
    }

    /// <summary>The next (or previous) error or warning after the caret, wrapping.</summary>
    private void StepToMarker(bool forward)
    {
        List<Marker> problems = Editor.Markers.Where(m => m.Kind is MarkerKind.Error or MarkerKind.Warning).ToList();
        if (problems.Count == 0)
        {
            Message?.Invoke(this, "No errors or warnings in this file");
            return;
        }
        int caret = Editor.Selections.Primary.Caret;
        Marker target = forward
            ? problems.FirstOrDefault(m => m.Range.Start > caret, problems[0])
            : problems.LastOrDefault(m => m.Range.Start < caret, problems[^1]);
        Editor.Selections = SelectionSet.Single(target.Range.Start);
        _ = ShowHover(target.Range.Start, Editor.PointOf(target.Range.Start));
    }

    private void ShowModal(ChoicePopup popup, int offset, string what, Func<Choice?, Task> done)
    {
        CloseModal();
        modal = popup;
        layer.Children.Add(popup);
        Place(popup, offset, above: false);
        popup.Done += (_, choice) => _ = Carried(what, () => done(choice));
    }

    private void ShowModal(RenameBox box, int offset, Func<string?, Task> done)
    {
        CloseModal();
        modal = box;
        layer.Children.Add(box);
        Place(box, offset, above: true);
        box.Done += (_, name) => _ = Carried("Rename", () => done(name));
    }

    /// <summary>What a modal's answer does (a fix chosen, a new name), carried out as background work. A failure of the
    /// server's, or an edit the disk refused, is said in the status bar, should the work not say it itself; anything else
    /// is the IDE's own bug, reported as an IDE error rather than lost in the event's handler.</summary>
    private async Task Carried(string what, Func<Task> done)
    {
        CloseModal();
        BackgroundWork.Begin();
        try
        {
            await done();
        }
        catch (LanguageServerFailed failed)
        {
            Failed(what, failed);
        }
        catch (WorkspaceEditFailed failed)
        {
            Message?.Invoke(this, $"{what} failed: {failed.Message}");
        }
        catch (Exception error)
        {
            IdeErrors.Report(error);
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    private void CloseModal()
    {
        if (modal is not null)
        {
            layer.Children.Remove(modal);
            modal = null;
            Editor.Focus();
        }
    }

    private void CloseAll()
    {
        CloseCompletion();
        HideHover();
        signing?.Cancel();
        signatureCard.IsVisible = false;
        CloseModal();
    }

    private void Place(Control popup, int offset, bool above) => Place(popup, Editor.PointOf(offset), above);

    /// <summary>Puts a popup under the line at <paramref name="at"/> (its top), or over it when asked
    /// or when there is no room below, kept inside the editor.</summary>
    private void Place(Control popup, Point at, bool above)
    {
        // The point is the editor's; the layer spans every split, so it is translated into the layer's.
        at = Editor.TranslatePoint(at, layer) ?? at;
        popup.Measure(Size.Infinity);
        Size size = popup.DesiredSize;
        Size room = layer.Bounds.Size;
        double lineHeight = Editor.LineHeight;
        double x = Math.Clamp(at.X - 4, 0, Math.Max(0, room.Width - size.Width - 8));
        double below = at.Y + lineHeight + 2;
        double over = at.Y - size.Height - 2;
        double y = above ? (over >= 0 ? over : below) : (below + size.Height <= room.Height || over < 0 ? below : over);
        Canvas.SetLeft(popup, x);
        Canvas.SetTop(popup, y);
    }

    private int WordStart(int caret)
    {
        TextSnapshot text = Editor.Document.Snapshot;
        int start = caret;
        while (start > 0 && IsIdentifierChar(text[start - 1]))
        {
            start--;
        }
        return start;
    }

    private string LinePreview(string path, int line)
    {
        if (editors.Documents.FirstOrDefault(d => d.Path == path) is { } open && line < open.Text.Snapshot.LineCount)
        {
            return open.Text.Snapshot.GetLineText(line).Trim();
        }
        try
        {
            return File.ReadLines(path).Skip(line).FirstOrDefault()?.Trim() ?? string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static int? Score(string typed, string text)
    {
        if (typed.Length == 0)
        {
            return 0;
        }
        if (text.StartsWith(typed, StringComparison.Ordinal))
        {
            return 4000 - text.Length;
        }
        if (text.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
        {
            return 3000 - text.Length;
        }
        if (FuzzyMatch.Score(typed, text) is { } fuzzy && fuzzy >= 850)
        {
            return 2000 - text.Length;
        }
        if (typed.Length >= 2 && text.Contains(typed, StringComparison.OrdinalIgnoreCase))
        {
            return 1000 - text.Length;
        }
        return null;
    }

    /// <summary>A row's badge: the letter and colour Rider gives each kind of completion.</summary>
    private static CompletionRow Row(CompletionItem item)
    {
        (string letter, string brush) = item.Kind switch
        {
            2 or 3 => ("m", "Passenger.Syntax.Method"),
            4 => ("c", "Passenger.Syntax.Class"),
            5 => ("f", "Passenger.Syntax.Field"),
            6 => ("v", "Passenger.TextMuted"),
            7 => ("C", "Passenger.Syntax.Class"),
            8 => ("I", "Passenger.Syntax.Interface"),
            9 => ("N", "Passenger.Syntax.Keyword"),
            10 => ("p", "Passenger.Syntax.Property"),
            13 => ("E", "Passenger.Syntax.Enum"),
            14 => ("k", "Passenger.Syntax.Keyword"),
            15 => ("s", "Passenger.TextMuted"),
            20 => ("e", "Passenger.Syntax.EnumMember"),
            21 => ("c", "Passenger.Syntax.Constant"),
            22 => ("S", "Passenger.Syntax.Struct"),
            23 => ("e", "Passenger.Syntax.Event"),
            24 => ("o", "Passenger.TextMuted"),
            25 => ("T", "Passenger.Syntax.TypeParameter"),
            _ => ("·", "Passenger.TextMuted"),
        };
        string detail = item.LabelDetails?.Description ?? item.Detail ?? string.Empty;
        return new CompletionRow(item, letter, InfoCard.Brush(brush), detail);
    }

    /// <summary>An open completion list: every item the server gave, and where the word it completes
    /// began.</summary>
    /// <summary>An open completion list: its items, where the name it completes starts, the characters that commit an
    /// item that names none of its own, and whether the server left items out until more is typed.</summary>
    private sealed record Completion(IReadOnlyList<CompletionItem> Items, int Start, IReadOnlyList<string> CommitCharacters, bool IsIncomplete);
}
