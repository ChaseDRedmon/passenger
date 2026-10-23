using Passenger.App.Aspire;
using A = Passenger.App.AppActions;

namespace Passenger.App;

public partial class MainWindow
{
    private void AddAspireActions()
    {
        Add(A.AspireDiscover, "Discover Aspire AppHosts", "Aspire", context =>
        {
            ShowToolWindow(ToolWindowIds.Aspire);
            _ = WorkingAsync(A.AspireDiscover, "Discovering AppHosts", () => Aspire.RefreshAppsAsync());
        });
        Add(A.AspireRefresh, "Refresh Aspire Resources", "Aspire", context =>
        {
            ShowToolWindow(ToolWindowIds.Aspire);
            _ = WorkingAsync(A.AspireRefresh, "Refreshing resources", () => Aspire.RequestRefreshAsync());
        },
            _ => Aspire.SelectedApp is null ? "No Aspire AppHost is selected" : null);
        AddService(A.AspireDebug, "Debug Aspire Resource", selected => DebugAspireResourceAsync(selected.AppHostPath, selected.Resource.Name));
        AddService(A.AspireAttach, "Attach to Aspire Resource", selected => DebugAspireResourceAsync(selected.AppHostPath, selected.Resource.Name, attach: true));
        AddService(A.AspireRun, "Run Aspire Resource Without Debugger", selected => RestartAspireServiceAsync(session, selected, debugging: false));
        AddService(A.AspireProfile, "Profile Aspire Resource", selected => ProfileAspireResourceAsync(selected.AppHostPath, selected.Resource.Name));
        Add(A.AspireCommand, "Run Aspire Resource Command…", "Aspire", context =>
        {
            if (Aspire.TakePendingCommand() is { } command)
            {
                _ = WorkingAsync(A.AspireCommand, "Running resource command", () => Aspire.ExecuteSelectedCommandAsync(command));
            }
            else
            {
                Aspire.ShowCommandChoices();
            }
        }, NeedsAspireResource);
        Add(A.AspireLogFind, "Find in Aspire Logs", "Aspire", _ => { ShowToolWindow(ToolWindowIds.Aspire); Aspire.ShowPage("Console"); Aspire.OpenFind(); });
        Add(A.AspireShowTrace, "Show Aspire Log Trace", "Aspire", _ => { ShowToolWindow(ToolWindowIds.Aspire); Aspire.ShowSelectedTrace(); },
            _ => Aspire.StructuredLogs.SelectedItem is AspireStructuredLog { TraceId.Length: > 0 } ? null : "No log with a trace is selected");
        Add(A.AspireShowRelatedLogs, "Show Related Aspire Logs", "Aspire", _ => { ShowToolWindow(ToolWindowIds.Aspire); Aspire.ShowRelatedLogs(); },
            _ => Aspire.TraceList.SelectedItem is AspireTrace && Aspire.SpanList.SelectedItem is Telemetry.TelemetryView.SpanRow ? null : "No trace span is selected");
        foreach (string page in AspireView.Pages)
        {
            // Details is the chosen resource's, beside every resource listed: shown as the window's resources are asked for.
            string id = AspireView.PageAction(page);
            Add(id, id == A.AspireResources ? "Show Aspire Resources" : $"Show Aspire {page}", "Aspire", _ =>
            {
                ShowToolWindow(ToolWindowIds.Aspire);
                Aspire.ShowPage(page);
            }, words: id == A.AspireResources ? ["Show Aspire Details"] : null);
        }
        Add(A.AspireOpenLink, "Open Aspire Resource Endpoint", "Aspire", _ =>
        {
            if (Aspire.TakePendingLink() is { Scheme: "http" or "https" } link)
            {
                Browse(link);
            }
            else
            {
                Aspire.ShowEndpointChoices();
            }
        });
        Add(A.AspireSource, "Open Aspire Resource Source", "Aspire", _ => Aspire.OpenSource(),
            _ => Aspire.SelectedResource?.Source is { } source && Path.IsPathFullyQualified(source) ? null : "No resource source is selected");
        Add(A.AspireContainer, "Show Aspire Resource Container", "Aspire", _ =>
        {
            if (Aspire.SelectedContainerId is { } id)
            {
                Aspire.SelectContainer?.Invoke(id);
            }
        }, _ => Aspire.SelectedContainerId is null ? "No container resource is selected"
            : Aspire.SelectContainer is null ? "Container selection is unavailable" : null);

        void AddService(string id, string title, Func<AspireSelection, Task<string?>> run) =>
            Add(id, title, "Aspire", context =>
            {
                if (Aspire.Selection is { } selected)
                {
                    _ = WorkingAsync(id, title, async () =>
                    {
                        if (await run(selected) is { } why)
                        {
                            status.Text = why;
                        }
                    });
                }
            }, NeedsAspireResource);
    }

    private string? NeedsAspireResource(ActionContext _) => Aspire.Selection is null ? "No Aspire resource is selected" : null;
}
