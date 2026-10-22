using Avalonia.Controls;
using Passenger.App.Aspire;

namespace Passenger.App.ToolWindows;

/// <summary>The views of the tool windows that start at the bottom (Build, Run, Debug, Unit Tests, Problems). Each is
/// a tool window of its own, which the person can move anywhere the dock has a place.</summary>
public sealed class ToolViews
{
    public ToolViews()
    {
        Tests = new TestsView(TestTree);
        AspireHost.Content = AspirePane;
    }

    public OutputView Build { get; } = new();
    public OutputView Run { get; } = new() { RerunsWhileRunning = true };
    public ProblemsView Problems { get; } = new();
    public DebugView Debug { get; } = new();
    public AspirePane AspirePane { get; private set; } = new();
    public AspireView Aspire => AspirePane.View;
    public ContentControl AspireHost { get; } = new();

    internal void ResetAspire(Passenger.App.Aspire.AspireClient client)
    {
        AspirePane.Dispose();
        AspirePane = new AspirePane(client);
        AspireHost.Content = AspirePane;
    }

    /// <summary>The Unit Tests window, over the tree the test runner fills.</summary>
    public TestsView Tests { get; }

    public Testing.TestTree TestTree { get; } = new();
}
