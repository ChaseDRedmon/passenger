using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Passenger.App.Containers;

namespace Passenger.App.Aspire;

/// <summary>View-scoped Dashboard streams. Snapshots are bounded recent history, not lossless telemetry storage.</summary>
public static class AspireTelemetry
{
    /// <summary>Calls back on the reader thread with immutable recent-log snapshots until canceled. Does not retry.</summary>
    public static Task WatchLogsAsync(Uri baseUri, string dashboardApiKey,
        Action<IReadOnlyList<AspireStructuredLog>> changed, CancellationToken cancel)
        => LogsAsync(baseUri, dashboardApiKey, changed, cancel);

    /// <summary>Reads bounded recent logs without opening a live stream. An unresolved OTLP resource filter returns no records; call again explicitly to refresh.</summary>
    public static async Task<IReadOnlyList<AspireStructuredLog>> ReadLogsAsync(Uri baseUri, string dashboardApiKey,
        string? resource = null, string? traceId = null, int limit = 200, CancellationToken cancel = default)
    {
        IReadOnlyList<AspireStructuredLog> result = [];
        await LogsAsync(baseUri, dashboardApiKey, logs => result = logs, cancel, follow: false,
            SnapshotQuery(resource, traceId, Math.Clamp(limit, 1, AspireClient.LogLimit))).ConfigureAwait(false);
        return result;
    }

    private static Task LogsAsync(Uri baseUri, string dashboardApiKey,
        Action<IReadOnlyList<AspireStructuredLog>> changed, CancellationToken cancel, bool follow = true, string? query = null)
    {
        ArgumentNullException.ThrowIfNull(changed);
        Queue<(AspireStructuredLog Log, int Bytes)> logs = new();
        int bytes = 0;
        return ReadAsync(baseUri, dashboardApiKey, "logs", envelope =>
        {
            foreach ((JsonElement row, string resource) in Records(envelope, "resourceLogs", "scopeLogs", "logRecords"))
            {
                Dictionary<string, string?> attributes = Attributes(row);
                JsonElement data = JsonSerializer.SerializeToElement(new
                {
                    timestamp = Timestamp(row, "timeUnixNano"),
                    severity = AspireJson.String(row, "severityText") ?? "Unknown",
                    resourceName = resource,
                    message = row.TryGetProperty("body", out JsonElement body) ? AnyText(AnyValue(body)) : null,
                    traceId = Identity(row, "traceId", 32, required: false),
                    spanId = Identity(row, "spanId", 16, required: false),
                    exception = attributes.GetValueOrDefault("exception.stacktrace") ?? attributes.GetValueOrDefault("exception.message"),
                    attributes,
                });
                data = MaskCredentials(data, dashboardApiKey);
                int size = Encoding.UTF8.GetByteCount(data.GetRawText());
                logs.Enqueue((new AspireStructuredLog(data), size));
                bytes += size;
                while (logs.Count > AspireClient.LogLimit || bytes > AspireClient.ResponseByteLimit
                    || logs.Select(item => AspireJson.String(item.Log.Data, "resourceName")).Distinct(StringComparer.Ordinal).Count() > AspireClient.ResourceLimit)
                {
                    bytes -= logs.Dequeue().Bytes;
                }
            }
            changed(Array.AsReadOnly(logs.Select(item => item.Log).ToArray()));
        }, cancel, follow, query);
    }

    /// <summary>Assembles recent traces by trace/span identity. Late spans update retained traces; evicted spans are not recovered.</summary>
    public static Task WatchTracesAsync(Uri baseUri, string dashboardApiKey,
        Action<IReadOnlyList<AspireTrace>> changed, CancellationToken cancel)
        => TracesAsync(baseUri, dashboardApiKey, changed, cancel);

    /// <summary>Reads bounded recent traces, or one trace with all retained spans, without CLI subprocesses. An unresolved OTLP resource filter returns no recent traces; missing explicit trace IDs remain errors.</summary>
    public static async Task<IReadOnlyList<AspireTrace>> ReadTracesAsync(Uri baseUri, string dashboardApiKey,
        string? resource = null, string? traceId = null, int limit = 10, CancellationToken cancel = default)
    {
        ValidateTraceId(traceId);
        IReadOnlyList<AspireTrace> result = [];
        await TracesAsync(baseUri, dashboardApiKey, traces => result = traces, cancel, follow: false,
            SnapshotQuery(resource, null, Math.Clamp(limit, 1, AspireClient.TraceLimit)), traceId).ConfigureAwait(false);
        return resource is null ? result : Array.AsReadOnly(result.Where(trace => trace.Spans.Any(span => span.Source == resource)).ToArray());
    }

    private static Task TracesAsync(Uri baseUri, string dashboardApiKey,
        Action<IReadOnlyList<AspireTrace>> changed, CancellationToken cancel, bool follow = true, string? query = null, string? requestedTraceId = null)
    {
        ArgumentNullException.ThrowIfNull(changed);
        Dictionary<string, Dictionary<string, AspireSpan>> traces = new(StringComparer.Ordinal);
        LinkedList<string> recent = new();
        int bytes = 0;
        int count = 0;
        return ReadAsync(baseUri, dashboardApiKey, follow ? "spans" : requestedTraceId is null ? "traces" : $"traces/{requestedTraceId.ToLowerInvariant()}", envelope =>
        {
            foreach ((JsonElement row, string resource) in Records(envelope, "resourceSpans", "scopeSpans", "spans"))
            {
                string traceId = Identity(row, "traceId", 32, required: true)!;
                string spanId = Identity(row, "spanId", 16, required: true)!;
                ulong start = Nanoseconds(row, "startTimeUnixNano");
                ulong end = Nanoseconds(row, "endTimeUnixNano");
                if (end < start)
                {
                    throw new InvalidDataException("Aspire telemetry returned a span ending before it starts.");
                }
                int status = row.TryGetProperty("status", out JsonElement statusData)
                    && statusData.TryGetProperty("code", out JsonElement code) && code.TryGetInt32(out int number) ? number : 0;
                JsonElement data = JsonSerializer.SerializeToElement(new
                {
                    traceId,
                    spanId,
                    parentSpanId = Identity(row, "parentSpanId", 16, required: false),
                    name = AspireJson.String(row, "name"),
                    source = resource,
                    startTime = Timestamp(row, "startTimeUnixNano"),
                    endTime = Timestamp(row, "endTimeUnixNano"),
                    startTimeUnixNano = start.ToString(CultureInfo.InvariantCulture),
                    endTimeUnixNano = end.ToString(CultureInfo.InvariantCulture),
                    durationMs = (end - start) / 1_000_000d,
                    status = status switch { 1 => "Ok", 2 => "Error", _ => "Unset" },
                    attributes = Attributes(row),
                    events = Optional(row, "events"),
                    links = Optional(row, "links"),
                });
                if (!traces.TryGetValue(traceId, out Dictionary<string, AspireSpan>? spans))
                {
                    traces.Add(traceId, spans = new(StringComparer.Ordinal));
                }
                if (spans.TryGetValue(spanId, out AspireSpan? previous))
                {
                    bytes -= SpanBytes(previous);
                    count--;
                }
                AspireSpan span = new(MaskCredentials(data, dashboardApiKey));
                spans[spanId] = span;
                bytes += SpanBytes(span);
                count++;
                recent.Remove(traceId);
                recent.AddFirst(traceId);
                while (traces.Count > AspireClient.TraceLimit || count > AspireClient.SpanLimit || bytes > AspireClient.ResponseByteLimit
                    || traces.Values.SelectMany(items => items.Values).Select(item => item.Source).Distinct(StringComparer.Ordinal).Count() > AspireClient.ResourceLimit)
                {
                    string oldest = recent.Last!.Value;
                    foreach (AspireSpan removed in traces[oldest].Values)
                    {
                        bytes -= SpanBytes(removed);
                        count--;
                    }
                    traces.Remove(oldest);
                    recent.RemoveLast();
                }
            }
            changed(Array.AsReadOnly(recent.Select(id => Trace(id, traces[id].Values)).ToArray()));
        }, cancel, follow, query);
    }

    private static AspireTrace Trace(string id, IEnumerable<AspireSpan> values)
    {
        AspireSpan[] spans = values.OrderBy(span => Nanoseconds(span.Data, "startTimeUnixNano")).ThenBy(span => span.SpanId, StringComparer.Ordinal).ToArray();
        ulong start = spans.Min(span => Nanoseconds(span.Data, "startTimeUnixNano"));
        ulong end = spans.Max(span => Nanoseconds(span.Data, "endTimeUnixNano"));
        return new AspireTrace(JsonSerializer.SerializeToElement(new
        {
            traceId = id,
            title = AspireJson.String((spans.FirstOrDefault(span => span.ParentSpanId is null) ?? spans[0]).Data, "name"),
            durationMs = (end - start) / 1_000_000d,
            hasError = spans.Any(span => AspireJson.String(span.Data, "status") == "Error"),
        }), Array.AsReadOnly(spans));
    }

    private static int SpanBytes(AspireSpan span) => Encoding.UTF8.GetByteCount(span.Data.GetRawText());

    private static async Task ReadAsync(Uri baseUri, string apiKey, string kind, Action<JsonElement> receive, CancellationToken cancel,
        bool follow = true, string? query = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        if (!baseUri.IsAbsoluteUri || !baseUri.IsLoopback || baseUri.Scheme is not ("http" or "https")
            || baseUri.UserInfo.Length > 0 || baseUri.Query.Length > 0 || baseUri.Fragment.Length > 0)
        {
            throw new ArgumentException("Telemetry requires a known loopback HTTP or HTTPS address without credentials, query, or fragment.", nameof(baseUri));
        }
        using SocketsHttpHandler handler = new() { AllowAutoRedirect = false, UseProxy = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(30) };
        using HttpClient client = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
        CancellationToken requested = cancel;
        using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        if (!follow)
        {
            bounded.CancelAfter(TimeSpan.FromSeconds(30));
        }
        cancel = bounded.Token;
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(baseUri, $"/api/telemetry/{kind}?{(follow ? "follow=true" : query)}"));
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Accept.ParseAdd(follow ? "application/x-ndjson" : "application/json");
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            bool missingResource = !follow && (kind is "logs" or "traces")
                && query?.Contains("&resource=", StringComparison.Ordinal) == true
                && response.StatusCode == System.Net.HttpStatusCode.NotFound
                && (string.Equals(response.Content.Headers.ContentType?.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase));
            if (!response.IsSuccessStatusCode && !missingResource)
            {
                throw new IOException($"Aspire telemetry {kind} request failed (HTTP {(int)response.StatusCode}; media type {response.Content.Headers.ContentType?.MediaType ?? "none"}; resource filter {query?.Contains("&resource=", StringComparison.Ordinal) == true}).");
            }
            if (!follow)
            {
                if (!missingResource && !string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Aspire telemetry did not return application/json.");
                }
                await using Stream snapshot = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
                using MemoryStream body = new();
                byte[] chunk = new byte[8192];
                int length;
                while ((length = await snapshot.ReadAsync(chunk, cancel).ConfigureAwait(false)) > 0)
                {
                    if (body.Length + length > AspireClient.ResponseByteLimit)
                    {
                        throw new InvalidDataException("Aspire telemetry snapshot exceeded 8 MiB.");
                    }
                    body.Write(chunk, 0, length);
                }
                using JsonDocument document = JsonDocument.Parse(body.GetBuffer().AsMemory(0, (int)body.Length), new JsonDocumentOptions { MaxDepth = 64 });
                if (missingResource)
                {
                    if (document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.EnumerateObject().Any()
                        || AspireJson.String(document.RootElement, "title") == "Resource not found"
                        && document.RootElement.TryGetProperty("status", out JsonElement status) && status.TryGetInt32(out int number) && number == 404)
                    {
                        return;
                    }
                    throw new IOException($"Aspire telemetry {kind} request failed (HTTP 404; fields {string.Join(",", document.RootElement.EnumerateObject().Take(8).Select(property => property.Name.Length <= 32 && property.Name.All(char.IsAsciiLetter) ? property.Name : "other"))}).");
                }
                if (!document.RootElement.TryGetProperty("data", out JsonElement data))
                {
                    throw new InvalidDataException("Aspire telemetry omitted its snapshot data.");
                }
                receive(AspireJson.Object(data));
                return;
            }
            if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/x-ndjson", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Aspire telemetry did not return application/x-ndjson.");
            }
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            using MemoryStream line = new();
            byte[] buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] == '\n')
                    {
                        if (line.Length > 0)
                        {
                            using JsonDocument document = JsonDocument.Parse(line.GetBuffer().AsMemory(0, (int)line.Length), new JsonDocumentOptions { MaxDepth = 64 });
                            cancel.ThrowIfCancellationRequested();
                            receive(AspireJson.Object(document.RootElement));
                            line.SetLength(0);
                        }
                    }
                    else
                    {
                        if (line.Length >= AspireClient.LineByteLimit)
                        {
                            throw new InvalidDataException("Aspire telemetry line exceeded 1 MiB.");
                        }
                        line.WriteByte(buffer[i]);
                    }
                }
            }
            cancel.ThrowIfCancellationRequested();
            throw new IOException("The Aspire telemetry stream ended. Reopen the view to reconnect.");
        }
        catch (OperationCanceledException) when (!follow && !requested.IsCancellationRequested)
        {
            throw new TimeoutException("The Aspire telemetry snapshot did not complete within 30 seconds.");
        }
        catch (HttpRequestException error)
        {
            string reason = error.StatusCode is { } status ? $"HTTP {(int)status}"
                : error.InnerException is System.Net.Sockets.SocketException socket ? socket.SocketErrorCode.ToString()
                : error.HttpRequestError.ToString();
            throw new IOException($"Aspire telemetry {kind} connection to {baseUri.GetLeftPart(UriPartial.Authority)} failed ({reason}).");
        }
        catch (JsonException)
        {
            throw new InvalidDataException("Aspire telemetry returned malformed OTLP JSON.");
        }
    }

    private static string SnapshotQuery(string? resource, string? traceId, int limit)
    {
        ValidateTraceId(traceId);
        if (resource is not null && (resource.Length is 0 or > 1024 || resource.Any(char.IsControl)))
        {
            throw new ArgumentException("Invalid Aspire telemetry resource identity", nameof(resource));
        }
        return $"limit={limit.ToString(CultureInfo.InvariantCulture)}"
            + (resource is null ? string.Empty : $"&resource={Uri.EscapeDataString(resource)}")
            + (traceId is null ? string.Empty : $"&traceId={traceId.ToLowerInvariant()}");
    }

    private static void ValidateTraceId(string? traceId)
    {
        if (traceId is not null && (traceId.Length != 32 || !traceId.All(Uri.IsHexDigit)))
        {
            throw new ArgumentException("traceId must contain 32 hexadecimal characters", nameof(traceId));
        }
    }

    private static JsonElement MaskCredentials(JsonElement data, string apiKey)
    {
        JsonNode root = JsonNode.Parse(data.GetRawText())!;
        Mask(root);
        return JsonSerializer.SerializeToElement(root);

        void Mask(JsonNode? node)
        {
            if (node is JsonObject properties)
            {
                bool secretAttribute = properties["key"] is JsonValue key && key.TryGetValue(out string? name)
                    && name is not null && ContainerSecrets.Secret(name);
                foreach ((string field, JsonNode? value) in properties.ToArray())
                {
                    if (field != "key" && ContainerSecrets.Secret(field) || secretAttribute && field == "value")
                    {
                        properties[field] = ContainerSecrets.Masked;
                    }
                    else if (value is JsonValue text && text.TryGetValue(out string? content) && content is not null)
                    {
                        properties[field] = ContainerSecrets.InValue(content.Replace(apiKey, ContainerSecrets.Masked, StringComparison.Ordinal));
                    }
                    else
                    {
                        Mask(value);
                    }
                }
            }
            else if (node is JsonArray items)
            {
                foreach (JsonNode? item in items)
                {
                    Mask(item);
                }
            }
        }
    }

    private static IEnumerable<(JsonElement Row, string Resource)> Records(JsonElement envelope, string resources, string scopes, string records)
    {
        foreach (JsonElement group in AspireJson.Array(envelope, resources, required: true))
        {
            if (!group.TryGetProperty("resource", out JsonElement resource))
            {
                throw new InvalidDataException("Aspire telemetry omitted its OTLP resource.");
            }
            Dictionary<string, string?> attributes = Attributes(resource);
            string name = attributes.GetValueOrDefault("service.name") ?? "unknown";
            string? instance = attributes.GetValueOrDefault("service.instance.id");
            string identity = string.IsNullOrEmpty(instance) ? name : $"{name}-{instance}";
            if (identity.Length > 1024 || identity.Any(char.IsControl))
            {
                throw new InvalidDataException("Aspire telemetry returned an invalid resource identity.");
            }
            foreach (JsonElement scope in AspireJson.Array(group, scopes, required: true))
            {
                foreach (JsonElement row in AspireJson.Array(scope, records, required: true))
                {
                    if (Encoding.UTF8.GetByteCount(row.GetRawText()) > AspireClient.LineByteLimit)
                    {
                        throw new InvalidDataException("Aspire telemetry record exceeded 1 MiB.");
                    }
                    yield return (AspireJson.Object(row), identity);
                }
            }
        }
    }

    private static Dictionary<string, string?> Attributes(JsonElement row)
    {
        Dictionary<string, string?> result = new(StringComparer.Ordinal);
        foreach (JsonElement item in AspireJson.Array(row, "attributes"))
        {
            string key = AspireJson.RequiredString(item, "key");
            if (!item.TryGetProperty("value", out JsonElement value))
            {
                throw new InvalidDataException("Aspire telemetry omitted an attribute value.");
            }
            result[key] = ContainerSecrets.Secret(key) ? ContainerSecrets.Masked : AnyText(AnyValue(value));
        }
        return result;
    }

    private static string? AnyText(JsonNode? value) => value is JsonValue scalar && scalar.TryGetValue(out string? text)
        ? text : value?.ToJsonString();

    private static JsonNode? AnyValue(JsonElement value, int depth = 0)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (depth >= 16)
        {
            throw new InvalidDataException("Aspire telemetry AnyValue nesting exceeded 16 levels.");
        }
        JsonProperty[] fields = AspireJson.Object(value).EnumerateObject().Where(field => field.Value.ValueKind != JsonValueKind.Null).ToArray();
        if (fields.Length == 0)
        {
            return null;
        }
        if (fields.Length != 1)
        {
            throw new InvalidDataException("Aspire telemetry returned multiple AnyValue variants.");
        }
        JsonProperty field = fields[0];
        switch (field.Name)
        {
            case "stringValue" or "bytesValue" when field.Value.ValueKind == JsonValueKind.String:
                return JsonValue.Create(ContainerSecrets.InValue(field.Value.GetString()!));
            case "boolValue" when field.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                return JsonValue.Create(field.Value.GetBoolean());
            case "intValue" when field.Value.ValueKind == JsonValueKind.String
                && long.TryParse(field.Value.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer):
                return JsonValue.Create(integer);
            case "intValue" when field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt64(out long numericInteger):
                return JsonValue.Create(numericInteger);
            case "doubleValue" when field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetDouble(out double number) && double.IsFinite(number):
                return JsonValue.Create(number);
            case "doubleValue" when field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() is "NaN" or "Infinity" or "-Infinity":
                return JsonValue.Create(field.Value.GetString());
            case "arrayValue":
            {
                IReadOnlyList<JsonElement> items = AnyItems(field.Value);
                JsonArray array = new();
                foreach (JsonElement item in items)
                {
                    array.Add(AnyValue(item, depth + 1));
                }
                return array;
            }
            case "kvlistValue":
            {
                IReadOnlyList<JsonElement> items = AnyItems(field.Value);
                JsonObject properties = new();
                foreach (JsonElement item in items)
                {
                    string key = AspireJson.RequiredString(item, "key");
                    if (!item.TryGetProperty("value", out JsonElement child))
                    {
                        throw new InvalidDataException("Aspire telemetry omitted an AnyValue attribute value.");
                    }
                    properties[key] = ContainerSecrets.Secret(key) ? JsonValue.Create(ContainerSecrets.Masked) : AnyValue(child, depth + 1);
                }
                return properties;
            }
            default:
                throw new InvalidDataException("Aspire telemetry returned an invalid AnyValue variant.");
        }
    }

    private static IReadOnlyList<JsonElement> AnyItems(JsonElement value)
    {
        IReadOnlyList<JsonElement> items = AspireJson.Array(value, "values");
        if (items.Count > 1024)
        {
            throw new InvalidDataException("Aspire telemetry AnyValue collection exceeded 1024 entries.");
        }
        return items;
    }

    private static string? Identity(JsonElement row, string field, int length, bool required)
    {
        string? value = AspireJson.String(row, field);
        if (!required && string.IsNullOrEmpty(value))
        {
            return null;
        }
        return value is not null && value.Length == length && value.All(Uri.IsHexDigit)
            ? value.ToLowerInvariant() : throw new InvalidDataException("Aspire telemetry returned an invalid trace or span identity.");
    }

    private static ulong Nanoseconds(JsonElement row, string field)
    {
        if (row.TryGetProperty(field, out JsonElement value)
            && (value.ValueKind == JsonValueKind.String && ulong.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)))
        {
            return parsed;
        }
        throw new InvalidDataException("Aspire telemetry returned an invalid nanosecond timestamp.");
    }

    private static string Timestamp(JsonElement row, string field)
    {
        ulong nanoseconds = Nanoseconds(row, field);
        ulong ticks = nanoseconds / 100;
        if (ticks > (ulong)(DateTimeOffset.MaxValue.Ticks - DateTimeOffset.UnixEpoch.Ticks))
        {
            throw new InvalidDataException("Aspire telemetry timestamp is outside the supported date range.");
        }
        return DateTimeOffset.UnixEpoch.AddTicks((long)ticks).ToString("O", CultureInfo.InvariantCulture);
    }

    private static JsonElement? Optional(JsonElement row, string field) => row.TryGetProperty(field, out JsonElement value) ? value : null;
}
