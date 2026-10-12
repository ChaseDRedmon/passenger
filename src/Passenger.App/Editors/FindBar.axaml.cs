using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Editors;

/// <summary>Find and replace in the open file. Matches are found as the query is typed and drawn in
/// the editor; the one nearest after the caret is selected, and the count says which of how many.</summary>
public partial class FindBar : UserControl
{
    private CodeEditor? editor;
    private TextDocument? document;
    private SearchResult result = SearchResult.None;
    private int current = -1;

    public FindBar()
    {
        InitializeComponent();
        IsVisible = false;
        Query.TextChanged += (_, _) => Search(jump: true);
        MatchCase.IsCheckedChanged += (_, _) => Search(jump: true);
        WholeWords.IsCheckedChanged += (_, _) => Search(jump: true);
        UseRegex.IsCheckedChanged += (_, _) => Search(jump: true);
        NextButton.Click += (_, _) => Next();
        PreviousButton.Click += (_, _) => Previous();
        CloseButton.Click += (_, _) => Close();
        ReplaceButton.Click += (_, _) => ReplaceCurrent();
        ReplaceAllButton.Click += (_, _) => ReplaceAll();
        // On the way down, before the text box takes Enter and Escape for itself.
        Query.AddHandler(KeyDownEvent, OnFieldKeyDown, RoutingStrategies.Tunnel);
        ReplaceWith.AddHandler(KeyDownEvent, OnFieldKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>What the count beside the arrows says: "2/5", "0 results", a bad regex's error.</summary>
    public string CountText => Count.Text ?? string.Empty;

    public SearchQuery CurrentQuery => new(Query.Text ?? string.Empty, MatchCase.IsChecked == true, WholeWords.IsChecked == true, UseRegex.IsChecked == true);

    /// <summary>Follows whichever document the editor shows; called when a tab is activated.</summary>
    public void Attach(CodeEditor shown)
    {
        editor = shown;
        if (document is not null)
        {
            document.Changed -= OnDocumentChanged;
        }
        document = shown.Document;
        document.Changed += OnDocumentChanged;
        if (IsVisible)
        {
            Search(jump: false);
        }
    }

    /// <summary>Opens the bar (with the replace row if <paramref name="replace"/>), its query the
    /// selection when that is on one line, or the word under the caret.</summary>
    public void Open(bool replace)
    {
        if (editor is null)
        {
            return;
        }
        IsVisible = true;
        ReplaceRow.IsVisible = replace;
        TextSnapshot text = editor.Document.Snapshot;
        Selection primary = editor.Selections.Primary;
        string? seed = null;
        if (!primary.IsEmpty && text.GetLineFromOffset(primary.Start) == text.GetLineFromOffset(primary.End))
        {
            seed = text.GetText(primary.Range);
        }
        else if (primary.IsEmpty)
        {
            TextRange word = TextNavigation.WordAt(text, primary.Caret);
            if (word.Length > 0 && TextNavigation.IsWordChar(text[word.Start]))
            {
                seed = text.GetText(word);
            }
        }
        if (seed is not null && seed != Query.Text)
        {
            Query.Text = seed;
        }
        else
        {
            Search(jump: false);
        }
        // A control shown this moment is not laid out yet and refuses the focus; it takes it once it is.
        Dispatcher.UIThread.Post(() =>
        {
            Query.Focus();
            Query.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    public void Close()
    {
        IsVisible = false;
        editor?.SetHighlights([], -1);
        editor?.Focus();
    }

    public void Next() => Step(forward: true);

    public void Previous() => Step(forward: false);

    private void Search(bool jump)
    {
        if (editor is null || !IsVisible)
        {
            return;
        }
        result = TextSearch.FindAll(editor.Document.Snapshot, CurrentQuery);
        current = -1;
        if (result.Matches.Count > 0)
        {
            Selection primary = editor.Selections.Primary;
            current = result.NextFrom(primary.Start);
            if (jump)
            {
                editor.Select(result.Matches[current]);
            }
        }
        Show();
    }

    private void Step(bool forward)
    {
        if (editor is null || result.Matches.Count == 0)
        {
            return;
        }
        Selection primary = editor.Selections.Primary;
        current = forward ? result.NextFrom(primary.End) : result.PreviousFrom(primary.Start);
        editor.Select(result.Matches[current]);
        Show();
    }

    private void ReplaceCurrent()
    {
        if (editor is null || current < 0 || current >= result.Matches.Count)
        {
            return;
        }
        TextRange match = result.Matches[current];
        Selection primary = editor.Selections.Primary;
        if (primary.Start != match.Start || primary.End != match.End)
        {
            editor.Select(match);
            return;
        }
        string replacement = TextSearch.Replacement(editor.Document.Snapshot, CurrentQuery, match, ReplaceWith.Text ?? string.Empty);
        int after = match.Start + replacement.Length;
        editor.ApplyEdits([new TextEdit(match.Start, match.Length, replacement)], SelectionSet.Single(after));
        Step(forward: true);
    }

    private void ReplaceAll()
    {
        if (editor is null)
        {
            return;
        }
        TextSnapshot text = editor.Document.Snapshot;
        SearchQuery query = CurrentQuery;
        SearchResult all = TextSearch.FindAll(text, query, int.MaxValue);
        string with = ReplaceWith.Text ?? string.Empty;
        List<TextEdit> edits = all.Matches.Select(m => new TextEdit(m.Start, m.Length, TextSearch.Replacement(text, query, m, with))).ToList();
        editor.ApplyEdits(edits);
        Count.Text = $"Replaced {edits.Count}";
    }

    private void Show()
    {
        editor?.SetHighlights(result.Matches, current);
        Count.Text = result.Problem is { } problem ? problem
            : string.IsNullOrEmpty(Query.Text) ? string.Empty
            : result.Matches.Count == 0 ? "0 results"
            : $"{current + 1}/{result.Matches.Count}{(result.Truncated ? "+" : string.Empty)}";
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (IsVisible)
        {
            Search(jump: false);
        }
    }

    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when sender == ReplaceWith:
                ReplaceCurrent();
                e.Handled = true;
                break;
            case Key.Enter:
                Step(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }
}
