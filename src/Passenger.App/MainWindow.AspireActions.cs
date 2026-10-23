using Passenger.App.Aspire;
using Passenger.App.ToolWindows;
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
        // A resource's Debug, Attach, Run without Debugger and Profile are the service actions every run of services has
        // (AddServiceActions), which act on the Aspire window's resource chosen too.
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
        Add(A.AspireLogFind, "Find in Aspire Logs", "Aspire", _ => { ShowToolWindow(ToolWindowIds.Aspire); Aspire.ShowPage(RunWindows.LogTab); Aspire.OpenFind(); });
        Add(A.AspireShowTrace, "Show Aspire Log Trace", "Aspire", _ => { ShowToolWindow(ToolWindowIds.Aspire); Aspire.ShowSelectedTrace(); },
            _ => Aspire.Telemetry.SelectedLog is { TraceId.Length: > 0 } ? null : "No log with a trace is selected");
        Add(A.AspireShowRelatedLogs, "Show Related Aspire Logs", "Aspire", _ => { ShowToolWindow(ToolWindowIds.Aspire); Aspire.ShowRelatedLogs(); },
            _ => Aspire.Telemetry.SelectedTrace is not null && Aspire.Telemetry.SelectedSpan is not null ? null : "No trace span is selected");
        foreach (string page in AspireView.Pages)
        {
            // Details is the chosen resource's, beside every resource listed: shown as the window's resources are asked for.
            // Each is found by the name its tab had before the windows shared one vocabulary too.
            string id = AspireView.PageAction(page);
            Add(id, id == A.AspireResources ? "Show Aspire Resources" : $"Show Aspire {page}", "Aspire", _ =>
            {
                ShowToolWindow(ToolWindowIds.Aspire);
                Aspire.ShowPage(page);
            }, words: id switch
            {
                A.AspireResources => ["Show Aspire Details"],
                A.AspireConsole => ["Show Aspire Console"],
                A.AspireAppHostConsole => ["Show Aspire AppHost console"],
                _ => null,
            });
        }
        // The Telemetry tab's two pages, as a log's trace and a span's logs choose them.
        foreach ((string id, string title, Passenger.App.Telemetry.TelemetryPage page) in (ReadOnlySpan<(string, string, Passenger.App.Telemetry.TelemetryPage)>)[
            (A.AspireLogs, "Show Aspire Structured logs", Passenger.App.Telemetry.TelemetryPage.Logs), (A.AspireTraces, "Show Aspire Traces", Passenger.App.Telemetry.TelemetryPage.Traces)])
        {
            Add(id, title, "Aspire", _ =>
            {
                ShowToolWindow(ToolWindowIds.Aspire);
                Aspire.ShowTelemetry(page);
            });
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
    }

    private string? NeedsAspireResource(ActionContext _) => Aspire.Selection is null ? "No Aspire resource is selected" : null;
}
