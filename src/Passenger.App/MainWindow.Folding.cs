using Passenger.App.Workspace;

namespace Passenger.App;

/// <summary>Which regions a file opens folded, as Rider's: those it was folded with when it last closed (kept with the
/// folder, after a restart too), else those Settings' Code Folding folds by default. Code, Folding's actions are the
/// editor's own.</summary>
public partial class MainWindow
{
    private readonly FoldMemory folds = new();

    private void AddFolding()
    {
        Editors.Opened += (_, document) =>
            document.FoldsAtFirst = folds.Of(document.Path) ?? (region => settings.FoldsByDefault(region.Kind));
        Editors.Closed += (_, document) => folds.Remember(document);
    }
}
