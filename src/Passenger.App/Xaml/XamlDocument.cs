using Passenger.App.Run;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Text;
using Passenger.Xaml;
using Passenger.Xaml.Features;
using Passenger.Xaml.Projects;
using Passenger.Xaml.Syntax;

namespace Passenger.App.Xaml;

/// <summary>One open XAML file's share of its project's XAML: its text read (off the UI thread, once per version) against
/// the project as it stands, for completion and the rest as it is typed, and its problems marked once typing pauses,
/// with the XAML compiler's from the last build on their lines until those lines change. The project's load may still be
/// under way as the file opens; what is asked meanwhile waits for it, and a file of a project whose XAML Passenger does
/// not read (neither Avalonia's nor WPF's) answers nothing.</summary>
public sealed class XamlDocument : IDisposable
{
    /// <summary>How long typing pauses before the file is checked again.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    private readonly XamlService service;
    private readonly Debounce analysing;
    private (int Version, XamlProject Project, XamlFile File)? last;
    private IReadOnlyList<Marker> live = [];
    private List<Marker> built = [];
    private bool disposed;

    internal XamlDocument(XamlService service, OpenDocument document, XamlLoading loading)
    {
        this.service = service;
        Document = document;
        Loading = loading;
        analysing = new Debounce(Settle, AnalyseAsync);
        document.Text.Changed += OnChanged;
        _ = AnalyseAsync();
    }

    public OpenDocument Document { get; }

    /// <summary>The session's XAML, which knows the other open files.</summary>
    internal XamlService Service => service;

    internal XamlLoading Loading { get; private set; }

    /// <summary>The project's first read, which a test or a screenshot waits for; its answer null for a project whose
    /// XAML Passenger does not read.</summary>
    public Task<XamlProject?> Loaded => Loading.Loaded;

    /// <summary>Whether the project's types have been read and are XAML Passenger reads.</summary>
    public bool IsLoaded => Loading.Loaded.IsCompletedSuccessfully && Loading.Loaded.Result is not null;

    /// <summary>The problems last found in the file, each with its fix when it has one.</summary>
    public IReadOnlyList<XamlDiagnostic> Diagnostics { get; private set; } = [];

    /// <summary>The file read as it stands, against the project as it stands: the last read when neither changed since;
    /// null for a project whose XAML Passenger does not read.</summary>
    public async Task<XamlFile?> FileAsync()
    {
        TextSnapshot text = Document.Text.Snapshot;
        int version = Document.Text.Version;
        string path = Document.Path;
        string? variant = Avalonia.Application.Current?.ActualThemeVariant.Key?.ToString();
        if (await Loading.Loaded is null || await Loading.Builder is not { } builder)
        {
            return null;
        }
        XamlProject project = builder.Current;
        if (last is { } kept && kept.Version == version && kept.Project == project && kept.File.Variant == variant)
        {
            return kept.File;
        }
        XamlFile file = await BackgroundWork.Run(() => Read(path, text.ToString(), project, variant));
        if (Document.Text.Version == version)
        {
            last = (version, project, file);
        }
        return file;
    }

    /// <summary>A parse made a file of the project, the project's summary of it taken from this text.</summary>
    private static XamlFile Read(string path, string text, XamlProject project, string? variant)
    {
        XmlTree tree = XmlTree.Parse(text);
        return new XamlFile(path, tree, project.With(XamlFileSummary.Of(path, tree, project.ResolveSource))) { Variant = variant };
    }

    /// <summary>What completion offers at a caret, for the text as it stands.</summary>
    public async Task<XamlCompletionList?> CompleteAsync(int caret, CancellationToken cancel)
    {
        if (!Loading.Loaded.IsCompleted)
        {
            service.Say($"Reading the types {Path.GetFileNameWithoutExtension(Loading.ProjectPath)}'s XAML can use…");
        }
        XamlFile? file = await FileAsync();
        cancel.ThrowIfCancellationRequested();
        return file is null ? null : await BackgroundWork.Run(() => XamlCompletion.At(file, caret), cancel);
    }

    /// <summary>The file checked as it stands, its markers replaced by what is found, unless it changed meanwhile.</summary>
    private async Task AnalyseAsync()
    {
        using IDisposable held = BackgroundWork.Scope();
        int version = Document.Text.Version;
        XamlFile? file = await FileAsync();
        if (file is null || disposed)
        {
            return;
        }
        if (await Loading.Builder is { } builder && await BackgroundWork.Run(() => builder.XamlTyped(file.Path, file.Tree)) is not null && !disposed)
        {
            service.Summarised(this);
        }
        IReadOnlyList<XamlDiagnostic> found = await BackgroundWork.Run(() => XamlDiagnostics.Of(file));
        if (disposed || Document.Text.Version != version)
        {
            return;
        }
        Diagnostics = found;
        live = [.. found.Select(d => new Marker(d.Range, d.Severity == XamlSeverity.Error ? MarkerKind.Error : MarkerKind.Warning, d.Message))];
        Show();
        IReadOnlyList<ColourUse> colours = await BackgroundWork.Run(() => Colours.InXaml(file, file.Variant));
        if (!disposed && Document.Text.Version == version)
        {
            Document.ColourMarks = XamlService.Marks(Document.Text.Snapshot, colours);
        }
    }

    /// <summary>The XAML compiler's problems in this file from the build that just ended, placed on their lines; each
    /// goes once its line is edited, or with the next build.</summary>
    internal void Built(IEnumerable<Problem> problems)
    {
        TextSnapshot text = Document.Text.Snapshot;
        built = [];
        foreach (Problem problem in problems)
        {
            if (problem.Position.Line >= text.LineCount)
            {
                continue;
            }
            LineSpan line = text.GetLine(problem.Position.Line);
            int start = line.Start + Math.Min(problem.Position.Column, line.Length);
            TextRange range = WordAt(text, start, line);
            string code = problem.Code is { Length: > 0 } c ? $"{c}: " : string.Empty;
            built.Add(new Marker(range, problem.Severity == ProblemSeverity.Error ? MarkerKind.Error : MarkerKind.Warning, code + Plain(problem.Message)));
        }
        Show();
    }

    /// <summary>The name a compiler's place points at (its tag's or attribute's), or the rest of its line.</summary>
    private static TextRange WordAt(TextSnapshot text, int start, LineSpan line)
    {
        int at = start;
        if (at < line.End && text[at] == '<')
        {
            at++;
        }
        int end = at;
        while (end < line.End && XmlTree.IsNameChar(text[end]))
        {
            end++;
        }
        return end > at ? TextRange.FromBounds(at, end) : TextRange.FromBounds(start, Math.Max(start, line.End));
    }

    /// <summary>A compiler's message without the place it repeats at its end (Avalonia's "Line 8, position 6.", WPF's
    /// MarkupCompile's "Line 8 Position 6.").</summary>
    private static string Plain(string message)
    {
        int at = message.LastIndexOf(" Line ", StringComparison.Ordinal);
        return at > 0 && message.IndexOf(" position ", at, StringComparison.OrdinalIgnoreCase) > at ? message[..at].TrimEnd() : message;
    }

    private void OnChanged(object? sender, DocumentChangedEventArgs e)
    {
        live = [.. live.Select(m => m with { Range = TextEditing.MapRange(m.Range, e.Edits) })];
        if (built.Count > 0)
        {
            DropEditedLines(e);
        }
        Show();
        analysing.Restart();
    }

    /// <summary>A build's problem on a line edited since is the build's, no longer this text's.</summary>
    private void DropEditedLines(DocumentChangedEventArgs e)
    {
        TextSnapshot before = e.Before;
        HashSet<int> edited = [.. e.Edits.SelectMany(edit => Enumerable.Range(before.GetLineFromOffset(Math.Min(edit.Start, before.Length)),
            before.GetLineFromOffset(Math.Min(edit.End, before.Length)) - before.GetLineFromOffset(Math.Min(edit.Start, before.Length)) + 1))];
        built = [.. built.Where(m => !edited.Contains(before.GetLineFromOffset(Math.Min(m.Range.Start, before.Length))))
            .Select(m => m with { Range = TextEditing.MapRange(m.Range, e.Edits) })];
    }

    /// <summary>The markers shown: what the check found, and the build's where the check found nothing on that line.</summary>
    private void Show()
    {
        TextSnapshot text = Document.Text.Snapshot;
        HashSet<int> lines = [.. live.Where(m => m.Range.Start <= text.Length).Select(m => text.GetLineFromOffset(m.Range.Start))];
        Document.Markers = [.. live, .. built.Where(m => m.Range.Start > text.Length || !lines.Contains(text.GetLineFromOffset(m.Range.Start)))];
    }

    /// <summary>The project was read again (its C# changed, another XAML file did): the file is checked against it.</summary>
    internal void ProjectChanged()
    {
        last = null;
        analysing.Restart();
    }

    /// <summary>The project is being loaded again (its project file or restore changed).</summary>
    internal void Follow(XamlLoading reloading)
    {
        Loading = reloading;
        last = null;
        analysing.Restart();
    }

    public void Dispose()
    {
        disposed = true;
        analysing.Stop();
        Document.Text.Changed -= OnChanged;
        last = null;
    }
}
