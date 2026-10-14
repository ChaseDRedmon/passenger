using Passenger.App.ToolWindows;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Languages.CSharp;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App;

/// <summary>Find Usages in Rider's Find window: the language server's usages told apart by what each does there (read
/// from the file's syntax), put under their projects and files, each search kept as a tab.</summary>
public partial class MainWindow
{
    private FindView? find;

    /// <summary>The Find tool window.</summary>
    public FindView Find => find!;

    private void AddFind()
    {
        find = new FindView(path => TextOf(path)?.ToString());
        Dock.Add(new Docking.ToolWindow("find", "Find", "Icon.Search", find, Docking.ToolAnchor.BottomLeft) { Action = AppActions.ShowFind, ActionTitle = "Find Tool Window" });
        find.GoToRequested += (_, usage) => NavigateTo(usage.Path, new TextPosition(usage.Line, usage.Column));
        assist.UsagesFound += (_, found) => ShowUsages(found.Title, found.Usages);
    }

    /// <summary>Bumped by each search shown, so one that lands after a later one is dropped.</summary>
    private int usageSearches;

    private void ShowUsages(string title, IReadOnlyList<LspLocation> locations) => _ = ShowUsagesAsync(title, locations);

    /// <summary>The usages under what each does there, read from each file's syntax off the UI thread: an open file's
    /// shared parse as it stands, a file not open read from disk and parsed there.</summary>
    private async Task ShowUsagesAsync(string title, IReadOnlyList<LspLocation> locations)
    {
        int search = ++usageSearches;
        List<(string Path, TextSnapshot? Open, ParsedText? Syntax, string Project, List<LspLocation> Here)> files =
            [.. locations.GroupBy(l => DocumentUris.ToPath(l.Uri)).Select(file =>
            {
                OpenDocument? open = Editors.Documents.FirstOrDefault(d => d.Path == file.Key);
                return (file.Key, open?.Text.Snapshot, open?.Syntax?.Current, ProjectNameOf(file.Key), file.ToList());
            })];
        List<Usage> usages = await BackgroundWork.Run(() =>
        {
            List<Usage> found = [];
            foreach ((string path, TextSnapshot? open, ParsedText? syntax, string project, List<LspLocation> here) in files)
            {
                TextSnapshot snapshot = syntax?.Text ?? open ?? (FileSearch.Read(path) is { } text ? TextSnapshot.From(text) : TextSnapshot.Empty);
                ServerLines lines = ServerLines.Of(snapshot);
                List<int> offsets = [.. here.Select(l => lines.OffsetOf(l.Range.Start))];
                IReadOnlyList<UsageKind> kinds = path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    ? UsageKinds.Classify(syntax?.Root ?? Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(snapshot.ToString()).GetRoot(), offsets)
                    : [.. here.Select(_ => UsageKind.Other)];
                for (int i = 0; i < here.Count; i++)
                {
                    LspRange range = here[i].Range;
                    TextPosition start = snapshot.GetPosition(offsets[i]);
                    int length = range.End.Line == range.Start.Line ? range.End.Character - range.Start.Character : 0;
                    found.Add(new Usage(path, start.Line, start.Column, Math.Max(0, length), snapshot.GetLineText(start.Line), kinds[i], project));
                }
            }
            return found;
        });
        if (search != usageSearches)
        {
            return;
        }
        Find.Show(title, usages);
        ShowToolWindow("find");
        status.Text = $"{title}: {usages.Count} found";
    }
}
