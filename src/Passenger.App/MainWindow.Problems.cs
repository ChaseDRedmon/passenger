using Avalonia.Controls;
using Avalonia.Interactivity;
using Passenger.App.CSharp;
using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App;

/// <summary>Problems as Rider keeps them: the current file's as they are typed, the solution's with solution-wide
/// analysis on (the status bar's icon turns it on and off and counts its errors), the projects' load problems under
/// Toolset, Environment, and the last build's apart. A link in a problem (an advisory) opens in the browser.</summary>
public partial class MainWindow
{
    private OpenDocument? problemsFile;

    private void AddProblems()
    {
        Editors.ActiveChanged += (_, _) => FollowActiveFile();
        Bottom.Problems.LinkClicked += (_, link) => Browse(link);
        ShowAnalysis();
    }

    /// <summary>Whether the status bar says an error, a warning or neither, as its icon shows it.</summary>
    public ProblemSeverity? StatusSeverity => status.Severity;

    /// <summary>Problems' Toolset, Environment: each project's load warnings and errors, under the project, its file the
    /// place to go to and the advisory or page its message names the link.</summary>
    private void ShowProjectProblems()
    {
        Bottom.Problems.ShowToolset([.. (session.CSharp?.ProjectProblems ?? []).Select(p => new Problem(p.Severity, p.Message, null, p.Project, new TextPosition(0, 0),
            Path.GetFileNameWithoutExtension(p.Project)) { Link = Problem.LinkIn(p.Message) })]);
    }

    /// <summary>Turns solution-wide analysis on or off, as the status bar's icon does, and keeps the choice.</summary>
    public void ToggleSolutionAnalysis() => ApplySettings(settings with { SolutionWideAnalysis = !settings.SolutionWideAnalysis });

    /// <summary>Puts the analysis setting in force on the C# service there is (or the next one).</summary>
    private void ApplyAnalysis()
    {
        if (session.CSharp is not null)
        {
            session.CSharp.SolutionWide = settings.SolutionWideAnalysis;
        }
        ShowAnalysis();
    }

    /// <summary>A new C# service follows the window's analysis setting and reports to Problems.</summary>
    private void WatchProblems(CSharpService service)
    {
        service.SolutionWide = settings.SolutionWideAnalysis;
        service.SolutionProblemsChanged += (_, _) =>
        {
            if (session.CSharp == service)
            {
                ShowSolutionProblems();
            }
        };
        service.ProjectProblemsChanged += (_, _) =>
        {
            if (session.CSharp == service)
            {
                ShowProjectProblems();
            }
        };
    }

    private void FollowActiveFile()
    {
        if (problemsFile is not null)
        {
            problemsFile.PropertyChanged -= OnProblemsFileChanged;
        }
        problemsFile = Editors.Active;
        if (problemsFile is not null)
        {
            problemsFile.PropertyChanged += OnProblemsFileChanged;
        }
        ShowFileProblems();
    }

    private void OnProblemsFileChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OpenDocument.Markers))
        {
            ShowFileProblems();
        }
    }

    private void ShowFileProblems()
    {
        if (problemsFile is not { } file)
        {
            Bottom.Problems.ShowFile(null, []);
            return;
        }
        TextSnapshot text = file.Text.Snapshot;
        List<Problem> problems = [];
        string? project = null;
        foreach (Marker marker in file.Markers)
        {
            if (marker.Kind is not (MarkerKind.Error or MarkerKind.Warning))
            {
                continue;
            }
            int start = Math.Min(marker.Range.Start, text.Length);
            problems.Add(new Problem(marker.Kind == MarkerKind.Error ? ProblemSeverity.Error : ProblemSeverity.Warning, marker.Message, null,
                file.Path, text.GetPosition(start), project ??= ProjectNameOf(file.Path)));
        }
        Bottom.Problems.ShowFile(file.Name, problems);
    }

    private void ShowSolutionProblems()
    {
        IReadOnlyList<(string Path, Diagnostic Diagnostic)>? found = session.CSharp?.SolutionProblems;
        Bottom.Problems.ShowSolution(found?.Select(p => new Problem(
            p.Diagnostic.Severity == 1 ? ProblemSeverity.Error : ProblemSeverity.Warning,
            p.Diagnostic.Message,
            p.Diagnostic.Code?.ValueKind == System.Text.Json.JsonValueKind.String ? p.Diagnostic.Code.Value.GetString() : p.Diagnostic.Code?.ToString(),
            p.Path,
            new TextPosition(p.Diagnostic.Range.Start.Line, p.Diagnostic.Range.Start.Character),
            ProjectNameOf(p.Path))).ToList());
        ShowAnalysis();
    }

    /// <summary>The status bar's analysis icon: grey and off, a green tick, or the solution's error count in red.</summary>
    private void ShowAnalysis()
    {
        IReadOnlyList<Problem>? found = Bottom.Problems.SolutionProblems;
        int errors = found?.Count(p => p.Severity == ProblemSeverity.Error) ?? 0;
        (string icon, string brush, string text, string tip) = !settings.SolutionWideAnalysis
            ? ("Icon.Problems", "Passenger.TextMuted", string.Empty, "Solution-wide analysis is off: click to analyse every file of the solution")
            : found is null || errors == 0
                ? ("Icon.Check", "Passenger.Test.Passed", string.Empty, "Solution-wide analysis: no errors. Click to turn it off")
                : ("Icon.Error", "Passenger.Editor.Error", errors.ToString(), $"Solution-wide analysis: {errors} error{(errors == 1 ? "" : "s")}; Problems lists them. Click to turn it off");
        AnalysisIcon.Bind(PathIcon.DataProperty, AnalysisIcon.GetResourceObservable(icon));
        AnalysisIcon.Bind(PathIcon.ForegroundProperty, AnalysisIcon.GetResourceObservable(brush));
        AnalysisText.Text = text;
        AnalysisText.IsVisible = text.Length > 0;
        ToolTip.SetTip(AnalysisButton, tip);
        AnalysisButton.IsVisible = session.CSharp is not null;
    }

    private void OnAnalysisClicked(object? sender, RoutedEventArgs e) => ToggleSolutionAnalysis();
}
