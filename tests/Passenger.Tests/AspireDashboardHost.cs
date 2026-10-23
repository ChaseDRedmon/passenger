using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Passenger.Tests;

/// <summary>A stand-in for an AppHost's dashboard telemetry on loopback (HTTP/1.1), which a test answers: each request's path,
/// query and key kept, and its answer the test's (<see cref="Answer"/>), a 200's JSON wrapped as the dashboard wraps it.</summary>
internal sealed class AspireDashboardHost : IAsyncDisposable
{
    private readonly WebApplication server;
    private readonly ConcurrentQueue<Asked> asked = new();

    private AspireDashboardHost(WebApplication server)
    {
        this.server = server;
        server.MapGet("/api/telemetry/{**path}", async context =>
        {
            asked.Enqueue(new Asked(context.Request.Path + context.Request.QueryString, context.Request.Headers["x-api-key"].ToString()));
            (int status, string contentType, string body) = Answer(context.Request.Path.Value ?? string.Empty);
            context.Response.StatusCode = status;
            context.Response.ContentType = contentType;
            await context.Response.WriteAsync(body, Encoding.UTF8, context.RequestAborted);
        });
    }

    /// <summary>The address the stand-in AppHost's launch profile names as its own (applicationUrl), where the IDE finds its
    /// dashboard.</summary>
    public Uri Endpoint { get; private set; } = null!;

    /// <summary>Each request so far: its path with its query, and the dashboard key it carried.</summary>
    public Asked[] Requests => asked.ToArray();

    /// <summary>The answer to a request by its path: an empty snapshot unless the test says otherwise.</summary>
    public Func<string, (int Status, string ContentType, string Body)> Answer { get; set; } =
        _ => (200, "application/json", Wrapped("{\"resourceLogs\":[]}"));

    /// <summary>A snapshot as the dashboard sends one: its payload under data.</summary>
    public static string Wrapped(string payload) => "{\"data\":" + payload + "}";

    public static async Task<AspireDashboardHost> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listener => listener.Protocols = HttpProtocols.Http1));
        AspireDashboardHost host = new(builder.Build());
        await host.server.StartAsync();
        host.Endpoint = new Uri(host.server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        return host;
    }

    public async ValueTask DisposeAsync()
    {
        await server.StopAsync();
        await server.DisposeAsync();
    }

    internal sealed record Asked(string PathAndQuery, string Key);
}
