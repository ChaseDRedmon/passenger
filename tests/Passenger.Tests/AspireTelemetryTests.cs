using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Passenger.App.Aspire;

namespace Passenger.Tests;

public sealed class AspireTelemetryTests
{
    private const string ApiKey = "synthetic-dashboard-key";

    [Theory]
    [InlineData(null)]
    [InlineData("api-owned-instance")]
    public async Task Telemetry_snapshots_decode_scalars_and_hide_authorization_in_attributes_and_exceptions(string? resource)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20).Scaled());
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        object payload = new
        {
            resourceLogs = new[]
            {
                new
                {
                        resource = new { attributes = new[]
                        {
                            new { key = "service.name", value = new { stringValue = "api" } },
                            new { key = "service.instance.id", value = new { stringValue = "owned-instance" } },
                        } },
                    scopeLogs = new[]
                    {
                        new
                        {
                            logRecords = new[]
                            {
                                new
                                {
                                    timeUnixNano = "1760000000000000000",
                                    severityText = "Information",
                                    body = new { boolValue = true },
                                    attributes = (object[])
                                    [
                                        new { key = "code.line.number", value = new { intValue = "42" } },
                                        new { key = "http.request.header.authorization", value = new { stringValue = "Bearer private-value" } },
                                        new { key = "exception.message", value = new { stringValue = "failure: Basic cHJpdmF0ZQ==" } },
                                    ],
                                },
                            },
                        },
                    },
                },
            },
        };
        Task<string> serving = ServeJsonAsync(listener, payload, timeout.Token);
        Uri endpoint = new($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
        IReadOnlyList<AspireStructuredLog> logs = await AspireTelemetry.ReadLogsAsync(endpoint, ApiKey,
            resource: resource, limit: 1, cancel: timeout.Token);
        AspireStructuredLog log = Assert.Single(logs);
        Assert.Equal("true", log.Message);
        Assert.Equal("api-owned-instance", log.Data.GetProperty("resourceName").GetString());
        Assert.Equal("42", log.Data.GetProperty("attributes").GetProperty("code.line.number").GetString());
        Assert.DoesNotContain("private-value", log.Data.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("cHJpdmF0ZQ==", log.Data.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, log.Data.GetRawText(), StringComparison.Ordinal);
        string headers = await serving.WaitAsync(timeout.Token);
        Assert.Contains($"x-api-key: {ApiKey}", headers, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GET /api/telemetry/logs?limit=1"
            + (resource is null ? string.Empty : $"&resource={resource}") + " HTTP/1.1", headers, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("logs", "api-owned-instance", null, "Resource not found", "application/problem+json", true)]
    [InlineData("traces", "api-owned-instance", null, "Resource not found", "application/problem+json", true)]
    [InlineData("logs", "api-owned-instance", null, "Resource not found", "application/json", true)]
    [InlineData("logs", null, null, "Resource not found", "application/problem+json", false)]
    [InlineData("logs", "api-owned-instance", null, "Not Found", "application/problem+json", false)]
    [InlineData("traces", "api-owned-instance", "11111111111111111111111111111111", "Trace not found", "application/problem+json", false)]
    [InlineData("logs", "api-owned-instance", null, null, "application/json", true)]
    [InlineData("traces", "api-owned-instance", null, null, "application/json", true)]
    [InlineData("logs", null, null, null, "application/json", false)]
    [InlineData("traces", "api-owned-instance", "11111111111111111111111111111111", null, "application/json", false)]
    public async Task Only_native_unresolved_resource_snapshots_are_empty_without_unfiltered_fallback(
        string kind, string? resource, string? traceId, string? title, string contentType, bool empty)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20).Scaled());
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        Task<string> serving = ServeJsonAsync(listener, title is null ? new object() : new { title, status = 404 }, timeout.Token,
            status: 404, contentType: contentType, envelope: false);
        Uri endpoint = new($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
        async Task Read()
        {
            if (kind == "logs")
            {
                Assert.Empty(await AspireTelemetry.ReadLogsAsync(endpoint, ApiKey, resource, traceId, 1, timeout.Token));
            }
            else
            {
                Assert.Empty(await AspireTelemetry.ReadTracesAsync(endpoint, ApiKey, resource, traceId, 1, timeout.Token));
            }
        }
        if (empty)
        {
            await Read();
        }
        else
        {
            IOException error = await Assert.ThrowsAsync<IOException>(Read);
            Assert.Contains("HTTP 404", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(ApiKey, error.Message, StringComparison.Ordinal);
        }
        string headers = await serving.WaitAsync(timeout.Token);
        string path = traceId is null ? kind : $"traces/{traceId}";
        Assert.Contains($"GET /api/telemetry/{path}?limit=1"
            + (resource is null ? string.Empty : $"&resource={resource}") + " HTTP/1.1", headers, StringComparison.Ordinal);
        Assert.False(listener.Pending(), "A snapshot must not retry or fall back to another resource query.");
    }

    [Fact]
    public async Task Telemetry_never_sends_dashboard_credentials_to_a_non_loopback_address()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => AspireTelemetry.ReadLogsAsync(new Uri("https://remote.example/"), ApiKey,
            cancel: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => AspireTelemetry.ReadTracesAsync(new Uri("http://127.0.0.1@remote.example/"), ApiKey,
            cancel: TestContext.Current.CancellationToken));
    }

    private static async Task<string> ServeJsonAsync(TcpListener listener, object payload, CancellationToken cancel,
        int status = 200, string contentType = "application/json", bool envelope = true)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(cancel);
        await using NetworkStream stream = client.GetStream();
        List<byte> headers = [];
        byte[] one = new byte[1];
        while (headers.Count < 16 * 1024)
        {
            int read = await stream.ReadAsync(one, cancel);
            if (read == 0)
            {
                throw new IOException("The telemetry request ended before its headers");
            }
            headers.Add(one[0]);
            if (headers.Count >= 4 && headers[^4] == '\r' && headers[^3] == '\n' && headers[^2] == '\r' && headers[^1] == '\n')
            {
                byte[] body = JsonSerializer.SerializeToUtf8Bytes(envelope ? new { data = payload } : payload);
                byte[] response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response, cancel);
                await stream.WriteAsync(body, cancel);
                return Encoding.ASCII.GetString(headers.ToArray());
            }
        }
        throw new IOException("The telemetry request headers exceeded the test limit");
    }
}
