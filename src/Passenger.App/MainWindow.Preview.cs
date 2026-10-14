using Passenger.App.Editors;
using Passenger.App.Run;
using Passenger.App.Workspace;
using Passenger.App.Xaml;
using Passenger.Editor;
using Passenger.Text;
using A = Passenger.App.AppActions;

namespace Passenger.App;

/// <summary>The XAML previewer beside the editor, as Rider's Avalonia previewer splits it: View, XAML Preview shows each editor group's XAML file of an Avalonia project with its design view to its right, Avalonia's own
/// previewer drawing it from the project's last build and following the text as typing pauses. A file's preview runs
/// while the file is open and the preview on; it ends as either goes.</summary>
public partial class MainWindow
{
    /// <summary>How long typing pauses before the previewer is sent the text.</summary>
    private static readonly TimeSpan PreviewSettle = TimeSpan.FromMilliseconds(400);

    private readonly Dictionary<OpenDocument, (XamlPreview Preview, XamlPreviewPane Pane, Debounce Typing, EventHandler<DocumentChangedEventArgs> Changed)> previews = [];
    private bool previewing;

    /// <summary>Whether XAML files are shown with their previews.</summary>
    public bool Previewing => previewing;

    /// <summary>The preview of a file while it is shown, for tests.</summary>
    public XamlPreview? PreviewOf(OpenDocument document) => previews.TryGetValue(document, out var shown) ? shown.Preview : null;

    /// <summary>The preview pane of a file while it is shown, for tests to find its frame on the window.</summary>
    public XamlPreviewPane? PreviewPaneOf(OpenDocument document) => previews.TryGetValue(document, out var shown) ? shown.Pane : null;

    private void AddPreview()
    {
        actions!.Add(new IdeAction(A.XamlPreview, "XAML Preview", "Editor", _ => TogglePreview())
        {
            WhyNot = context => context.File is null ? "No file is open"
                : session.Xaml.For(context.File) is null ? "The previewer is for the XAML of an Avalonia project" : null,
            IsChecked = _ => previewing,
            Menus = [new MenuPlace("View", 4, 0)],
        });
        Editors.ActiveChanged += (_, _) => FollowPreviews();
        Editors.Closed += (_, document) => EndPreview(document);
        Closed += (_, _) =>
        {
            foreach (OpenDocument document in previews.Keys.ToList())
            {
                EndPreview(document);
            }
        };
    }

    /// <summary>The previews follow a session's XAML from now: the last session's end with it, and its own are made as its
    /// files are read, and again from each build it finishes.</summary>
    private void BindPreviews(ProjectSession next)
    {
        foreach (OpenDocument document in previews.Keys.ToList())
        {
            EndPreview(document);
        }
        next.Xaml.ProjectChanged += (_, _) =>
        {
            if (session == next)
            {
                FollowPreviews();
            }
        };
        buildRun.BuildFinished += (_, outcome) =>
        {
            if (session == next)
            {
                PreviewsBuilt(outcome);
            }
        };
        FollowPreviews();
    }

    private void TogglePreview()
    {
        previewing = !previewing;
        if (!previewing)
        {
            foreach (OpenDocument document in previews.Keys.ToList())
            {
                EndPreview(document);
            }
        }
        FollowPreviews();
    }

    /// <summary>Each editor group shows the preview of its XAML file while previews are on, and none otherwise.</summary>
    private void FollowPreviews()
    {
        foreach (EditorGroup group in Editors.Groups)
        {
            group.Side = previewing && group.Active is { } shown && session.Xaml.For(shown) is { } document ? PaneFor(shown, document) : null;
        }
    }

    private XamlPreviewPane PaneFor(OpenDocument shown, XamlDocument document)
    {
        if (previews.TryGetValue(shown, out var kept))
        {
            return kept.Pane;
        }
        XamlPreview preview = new(document);
        XamlPreviewPane pane = new(preview);
        Debounce typing = new(PreviewSettle, () =>
        {
            preview.Update();
            return Task.CompletedTask;
        });
        EventHandler<DocumentChangedEventArgs> changed = (_, _) => typing.Restart();
        shown.Text.Changed += changed;
        previews[shown] = (preview, pane, typing, changed);
        _ = preview.StartAsync();
        return pane;
    }

    /// <summary>A build that succeeded is what each preview draws from then on.</summary>
    private void PreviewsBuilt(BuildOutcome outcome)
    {
        if (outcome.Status != BuildStatus.Succeeded)
        {
            return;
        }
        foreach (XamlPreview preview in previews.Values.Select(p => p.Preview))
        {
            _ = preview.RestartAsync();
        }
    }

    private void EndPreview(OpenDocument document)
    {
        if (!previews.Remove(document, out var shown))
        {
            return;
        }
        document.Text.Changed -= shown.Changed;
        shown.Typing.Stop();
        shown.Preview.Dispose();
        foreach (EditorGroup group in Editors.Groups.Where(g => g.Side == shown.Pane))
        {
            group.Side = null;
        }
    }
}
