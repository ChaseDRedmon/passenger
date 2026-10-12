using Passenger.App.ToolWindows;
using Passenger.App.Workspace;
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
        Dock.Add(new Docking.ToolWindow("find", "Find", "Icon.Search", find, Docking.ToolAnchor.BottomLeft) { Action = AppActions.ShowFind });
        find.GoToRequested += (_, usage) => NavigateTo(usage.Path, new TextPosition(usage.Line, usage.Column));
        assist.UsagesFound += (_, found) => ShowUsages(found.Title, found.Usages);
    }

    private void ShowUsages(string title, IReadOnlyList<LspLocation> locations)
    {
        List<Usage> usages = [];
        foreach (IGrouping<string, LspLocation> file in locations.GroupBy(l => PathNames.Canonical(new Uri(l.Uri).LocalPath)))
        {
            TextSnapshot snapshot = TextOf(file.Key) ?? TextSnapshot.Empty;
            ServerLines lines = ServerLines.Of(snapshot);
            string text = snapshot.ToString();
            List<LspLocation> here = [.. file];
            List<int> offsets = [.. here.Select(l => lines.OffsetOf(l.Range.Start))];
            IReadOnlyList<UsageKind> kinds = file.Key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? UsageKinds.Classify(text, offsets)
                : [.. here.Select(_ => UsageKind.Other)];
            string project = ProjectOf(file.Key);
            for (int i = 0; i < here.Count; i++)
            {
                LspRange range = here[i].Range;
                TextPosition start = snapshot.GetPosition(offsets[i]);
                int length = range.End.Line == range.Start.Line ? range.End.Character - range.Start.Character : 0;
                usages.Add(new Usage(file.Key, start.Line, start.Column, Math.Max(0, length), snapshot.GetLineText(start.Line), kinds[i], project));
            }
        }
        Find.Show(title, usages);
        ShowToolWindow("find");
        StatusText.Text = $"{title}: {usages.Count} found";
    }

    /// <summary>The project a file belongs to, by its folder; outside them all, the folder's name, as Rider's
    /// Miscellaneous Files.</summary>
    private string ProjectOf(string path)
    {
        Workspace.ProjectModel? owner = Projects.Where(p => path.StartsWith(p.Folder + Path.DirectorySeparatorChar, StringComparison.Ordinal)).MaxBy(p => p.Folder.Length);
        return owner is not null ? Path.GetFileNameWithoutExtension(owner.Path) : "Miscellaneous Files";
    }
}
