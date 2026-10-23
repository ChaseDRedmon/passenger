namespace Passenger.App.Telemetry;

/// <summary>Where a run's structured logs and traces are read: an Aspire dashboard's telemetry API on this machine (an
/// AppHost's own, or the one a container run that collects them runs beside its services) and its API key, which
/// <see cref="Aspire.AspireTelemetry"/> reads for the Telemetry view and an agent's <c>telemetry</c> alike.</summary>
public sealed record TelemetrySource(Uri Endpoint, string ApiKey)
{
    /// <summary>Whether a record's resource (the dashboard's name for it: a service's name, with its instance after a dash
    /// when it has one) is the resource or Compose service named.</summary>
    public static bool Of(string resource, string named) =>
        resource == named || resource.StartsWith(named + "-", StringComparison.Ordinal);
}
