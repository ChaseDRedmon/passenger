using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Passenger.App.Aspire;
using Passenger.App.Run;
using Passenger.App.Telemetry;

namespace Passenger.App.Mcp;

public sealed partial class PassengerTools
{
    [McpServerTool(Name = "telemetry", Title = "Logs and traces", ReadOnly = true), Description(
        "A run's structured logs or traces (kind) from its dashboard: an Aspire AppHost's, or the container run's when its "
        + "configuration collects them; a resource's or Compose service's (resource) or one trace's (traceId) alone.")]
    public Task<TelemetryInfo> GetTelemetry(string kind = "logs", string? appHost = null,
        string? resource = null, string? traceId = null, int limit = 200,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        if (kind is not ("logs" or "traces"))
        {
            throw new ArgumentException("kind must be logs or traces");
        }
        ProjectSession current = Session;
        (ServiceRun run, TelemetrySource? source, string? filter) = await TelemetryOfAsync(current, appHost, resource, cancel);
        if (source is null)
        {
            throw new InvalidOperationException($"{run.Name}'s dashboard is unavailable");
        }
        IReadOnlyList<AspireStructuredLog> logs = kind == "logs"
            ? await AspireTelemetry.ReadLogsAsync(source.Endpoint, source.ApiKey, filter, traceId, limit, cancel) : [];
        IReadOnlyList<AspireTrace> traces = kind == "traces"
            ? await AspireTelemetry.ReadTracesAsync(source.Endpoint, source.ApiKey, filter, traceId, limit, cancel) : [];
        if (Session != current || current.IsEnded || run.IsStopped)
        {
            throw new InvalidOperationException("The workspace or its run changed");
        }
        return new TelemetryInfo([.. logs.Select(log => log.Data)],
            [.. traces.Select(trace => new TraceInfo(trace.TraceId, trace.Data, [.. trace.Spans.Select(span => span.Data)]))]);
    });

    /// <summary>The run an agent's <c>telemetry</c> reads, with the resource it reads alone: a Compose service of the container
    /// run that collects telemetry, else an owned AppHost's resource or the AppHost <paramref name="appHost"/> names; with
    /// neither named, the one run there is that has telemetry.</summary>
    private async Task<(ServiceRun Run, TelemetrySource? Source, string? Filter)> TelemetryOfAsync(ProjectSession current, string? appHost, string? resource,
        CancellationToken cancel)
    {
        Containers.ContainerRun? containers = current.ContainerRun is { IsStopped: false, Plan.Telemetry: not null } collecting ? collecting : null;
        if (appHost is null && containers is not null
            && (resource is null && !current.Aspire.Apps.Any(app => !app.IsTest && !app.Stopping)
                || resource is not null && !resource.Contains("::", StringComparison.Ordinal)
                    && (containers.Plan.Dotnet.Any(d => d.Service == resource) || containers.Plan.Backing.Contains(resource))))
        {
            return (containers, containers.Telemetry, resource);
        }
        string? qualified = resource is null ? null : appHost is not null && !resource.Contains("::", StringComparison.Ordinal)
            ? $"{Resolve(appHost)}::{resource}" : resource;
        AspireSelection? selected = qualified is null ? null : await window.ResolveOwnedAspireResourceAsync(qualified, null, cancel);
        string? path = appHost is null ? selected?.AppHostPath : Resolve(appHost);
        if (selected is not null && path is not null && !Passenger.Core.PathNames.Same(path, selected.AppHostPath))
        {
            throw new ArgumentException("appHost and resource name different AppHosts");
        }
        AspireAppRun[] apps = current.Aspire.Apps.Where(app => !app.IsTest && !app.Stopping
            && (path is null || Passenger.Core.PathNames.Same(path, app.Configuration.Project))).ToArray();
        AspireAppRun app = apps.Length == 1 ? apps[0]
            : throw new InvalidOperationException(containers is not null && path is null
                ? $"Name the run: appHost, or resource (a service of {containers.Name} or an AppHost's); workspace lists them"
                : "Name one owned AppHost by appHost or a unique resource; workspace lists targets");
        if (Session != current || current.IsEnded || app.Telemetry is null)
        {
            throw new InvalidOperationException("The owned AppHost's Dashboard is unavailable");
        }
        return (app, app.Telemetry, selected?.Resource.Name);
    }
}

public sealed record TraceInfo(string TraceId, JsonElement Summary, IReadOnlyList<JsonElement> Spans);
public sealed record TelemetryInfo(IReadOnlyList<JsonElement> Logs, IReadOnlyList<TraceInfo> Traces);
