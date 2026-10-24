using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Threading.Channels;
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
/// query and key kept, and its answer the test's (<see cref="Answer"/>), a 200's JSON wrapped as the dashboard wraps it; a
/// followed stream, when it <see cref="Streams"/>, the envelopes the test sends, a line each.</summary>
internal sealed class AspireDashboardHost : IAsyncDisposable
{
    private readonly WebApplication server;
    private readonly ConcurrentQueue<Asked> asked = new();
    private readonly ConcurrentDictionary<string, Channel<string>> sent = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> following = new(StringComparer.Ordinal);

    private AspireDashboardHost(WebApplication server)
    {
        this.server = server;
        server.MapGet("/api/telemetry/{**path}", async context =>
        {
            asked.Enqueue(new Asked(context.Request.Path + context.Request.QueryString, context.Request.Headers["x-api-key"].ToString()));
            string kind = (string)context.Request.RouteValues["path"]!;
            if (Streams && context.Request.Query["follow"] == "true")
            {
                await FollowAsync(context, kind);
                return;
            }
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

    /// <summary>Whether a followed request (follow=true) streams what the test sends (<see cref="Send"/>) as NDJSON, open until
    /// its reader goes or the stand-in stops, rather than answered as any other.</summary>
    public bool Streams { get; set; }

    /// <summary>How many readers follow a kind (logs, spans) now.</summary>
    public int Following(string kind) => following.GetValueOrDefault(kind);

    /// <summary>An envelope sent to the followed stream of a kind (logs, spans) as a line, to its reader or the next.</summary>
    public void Send(string kind, string envelope) => Lines(kind).Writer.TryWrite(envelope);

    private Channel<string> Lines(string kind) => sent.GetOrAdd(kind, _ => Channel.CreateUnbounded<string>());

    private async Task FollowAsync(HttpContext context, string kind)
    {
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/x-ndjson";
        await context.Response.StartAsync(context.RequestAborted);
        following.AddOrUpdate(kind, 1, (_, count) => count + 1);
        try
        {
            await foreach (string line in Lines(kind).Reader.ReadAllAsync(context.RequestAborted))
            {
                await context.Response.WriteAsync(line.ReplaceLineEndings(" ") + "\n", Encoding.UTF8, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        finally
        {
            following.AddOrUpdate(kind, 0, (_, count) => count - 1);
        }
    }

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
        foreach (Channel<string> lines in sent.Values)
        {
            lines.Writer.TryComplete();
        }
        await server.StopAsync();
        await server.DisposeAsync();
    }

    internal sealed record Asked(string PathAndQuery, string Key);
}
