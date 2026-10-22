using Passenger.App.Aspire;

namespace Passenger.App;

public partial class MainWindow
{
    private AspireContainerOwnership? aspireContainerOwnership;
    private bool aspireContainerCleanupScheduled;
    private bool aspireContainerCleanupStarted;

    private void PrepareAspireContainerCleanup(ProjectSession current)
    {
        if (current.Folder is not null)
        {
            Start();
        }
        else if (!aspireContainerCleanupScheduled)
        {
            aspireContainerCleanupScheduled = true;
            Opened += (_, _) => Start();
        }

        void Start()
        {
            if (!aspireContainerCleanupStarted)
            {
                aspireContainerCleanupStarted = true;
                _ = ReportAspireContainerWorkAsync(AspireContainers.CleanupAsync());
            }
        }
    }

    private AspireContainerOwnership AspireContainers
    {
        get
        {
            if (aspireContainerOwnership is null)
            {
                CancellationTokenSource lifetime = new();
                Closing += (_, _) => lifetime.Cancel();
                Closed += (_, _) => lifetime.Dispose();
                aspireContainerOwnership = new(StateFolder, Engine, lifetime.Token);
            }
            return aspireContainerOwnership;
        }
    }

    private static async Task ReportAspireContainerWorkAsync(Task work)
    {
        try
        {
            await work.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            IdeErrors.Write($"Aspire container ownership: {error}");
        }
    }

    private async Task ShowAspireContainerAsync(ProjectSession current, string id)
    {
        ShowToolWindow(ToolWindowIds.Containers);
        await ContainersWindow.RefreshAsync();
        if (session != current || current.IsEnded)
        {
            return;
        }
        if (ContainersWindow.Choose(row => row.Container?.Id == id))
        {
            ContainersWindow.ShowTab("Log");
        }
        else
        {
            Say("The Aspire container is no longer available in the current engine");
        }
    }
}
