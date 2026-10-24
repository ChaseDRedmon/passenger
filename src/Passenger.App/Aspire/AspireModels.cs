using System.Text.Json;

namespace Passenger.App.Aspire;

public sealed record AspireAvailability(bool Available, string Message);

public sealed record AspireSelection(string AppHostPath, AspireResource Resource);

public sealed record AspireApp(string AppHostPath, int AppHostPid, string Status)
{
    public override string ToString() => $"{Path.GetFileNameWithoutExtension(AppHostPath)} · {AppHostPid} · {Status}";
}

public sealed class AspireResource(JsonElement data)
{
    public JsonElement Data { get; } = data.Clone();
    public string Name => AspireJson.RequiredString(Data, "name");
    public string DisplayName => AspireJson.String(Data, "displayName") ?? Name;
    public string? Uid => AspireJson.String(Data, "uid") ?? AspireJson.Properties(Data, "properties")
        .Where(property => property.Name == "resource.uid" && property.Value.ValueKind == JsonValueKind.String)
        .Select(property => property.Value.GetString()).FirstOrDefault();
    public string Generation => $"{Uid}\n{AspireJson.String(Data, "creationTimestamp")}\n{AspireJson.String(Data, "startTimestamp")}";
    public string? State => AspireJson.String(Data, "state");
    public string? HealthStatus => AspireJson.String(Data, "healthStatus");
    public string? Source => AspireJson.String(Data, "source");

    /// <summary>Whether it is the resource a name means: its own name, its display name, or the name with DCP's suffix after
    /// a dash, never a sibling whose name begins with the name and a dash (api and api-worker).</summary>
    public bool IsNamed(string? name) => name is not null && (Name == name || DisplayName == name || IsNameWithSuffix(Name, name));

    /// <summary>Whether a resource's name is a name with DCP's suffix, one token with no dash of its own, after a dash.</summary>
    public static bool IsNameWithSuffix(string resourceName, string name) =>
        resourceName.Length > name.Length + 1 && resourceName.StartsWith(name + "-", StringComparison.Ordinal)
        && resourceName.IndexOf('-', name.Length + 1) < 0;

    /// <summary>Whether it is a .NET project its AppHost runs (a service of its run, which the service actions reach), not a
    /// container, an executable, a parameter or an external resource.</summary>
    public bool IsProject => AspireJson.String(Data, "resourceType") == "Project" && Source is { } source && Path.IsPathFullyQualified(source);

    /// <summary>Whether the AppHost hides the resource: false when it says nothing of it, as the CLI's snapshot of one it
    /// shows need not.</summary>
    public bool IsHidden => Data.TryGetProperty("isHidden", out JsonElement hidden) && hidden.ValueKind == JsonValueKind.True;

    public IReadOnlyList<AspireResourceCommand> Commands => AspireJson.Properties(Data, "commands")
        .Select(p => new AspireResourceCommand(p.Name, p.Value)).ToArray();
    public override string ToString() => $"{DisplayName} · {State ?? "Unknown"} · {HealthStatus ?? "No health report"}";
}

public sealed class AspireResourceCommand(string name, JsonElement data)
{
    public string Name { get; } = name;
    public JsonElement Data { get; } = data;
    public string DisplayName => AspireJson.String(Data, "displayName") ?? Name;
    public string? Description => AspireJson.String(Data, "description");
    public bool Hidden => AspireJson.String(Data, "state") is "Hidden" || AspireJson.String(Data, "visibility") is "Hidden";
    public bool Enabled => AspireJson.String(Data, "state") is null or "Enabled"
        && AspireJson.String(Data, "visibility") is not "Hidden";
    public bool HasArguments => AspireJson.Array(Data, "argumentInputs").Count > 0;
    public override string ToString() => DisplayName;
}

public sealed record AspireConsoleLog(string ResourceName, string Content, bool IsError, string? Timestamp, int? LineNumber = null);

public sealed class AspireStructuredLog(JsonElement data)
{
    private string? resourceName;

    public JsonElement Data { get; } = data;

    /// <summary>Its place in the order its reader heard the logs (from 1), given once off the UI thread, which the Telemetry
    /// view keys its row by; 0 for a log read otherwise (the CLI's).</summary>
    public long Sequence { get; init; }

    /// <summary>The resource that logged it, read once.</summary>
    public string? ResourceName => resourceName ??= AspireJson.String(Data, "resourceName");

    /// <summary>The service that logged it, by its name alone (no instance), as <see cref="ResourceName"/> begins.</summary>
    public string? ServiceName => AspireJson.String(Data, "serviceName");
    public string Message => AspireJson.String(Data, "message") ?? string.Empty;
    public string? TraceId => AspireJson.String(Data, "traceId");
    public string? SpanId => AspireJson.String(Data, "spanId");
    public override string ToString() => $"{AspireJson.String(Data, "severity")} · {AspireJson.String(Data, "resourceName")} · {Message}";
}

/// <summary>What one envelope of a followed log stream changed of the logs its reader keeps: those it added and those the
/// bounds evicted, each oldest first (an evicted log may be one it added).</summary>
public sealed record AspireLogBatch(IReadOnlyList<AspireStructuredLog> Added, IReadOnlyList<AspireStructuredLog> Evicted);

public sealed class AspireSpan(JsonElement data)
{
    /// <summary>What a span with no parent keeps as its parent's id once read: a string of its own, never a span's id.</summary>
    private static readonly string NoParent = new('-', 1);
    private string? spanId;
    private string? parentSpanId;

    public JsonElement Data { get; } = data;

    /// <summary>Its id and its parent's, read once: a trace's tree reads them for every span at every look.</summary>
    public string SpanId => spanId ??= AspireJson.RequiredString(Data, "spanId");
    public string? ParentSpanId
    {
        get
        {
            string parent = parentSpanId ??= AspireJson.String(Data, "parentSpanId") ?? NoParent;
            return ReferenceEquals(parent, NoParent) ? null : parent;
        }
    }
    public string? Source => AspireJson.String(Data, "source");

    /// <summary>The service whose span it is, by its name alone (no instance), as <see cref="Source"/> begins.</summary>
    public string? Service => AspireJson.String(Data, "service");
    public override string ToString() => $"{Source} · {AspireJson.String(Data, "name")} · {AspireJson.Duration(Data)} · {AspireJson.String(Data, "status")}";
}

public sealed class AspireTrace(JsonElement data, IReadOnlyList<AspireSpan> spans)
{
    private string? traceId;

    public JsonElement Data { get; } = data;
    public IReadOnlyList<AspireSpan> Spans { get; } = spans;
    public string TraceId => traceId ??= AspireJson.RequiredString(Data, "traceId");
    public override string ToString() => $"{AspireJson.String(Data, "title") ?? TraceId} · {AspireJson.Duration(Data)}{(AspireJson.Boolean(Data, "hasError") ? " · Error" : string.Empty)}";
}

internal static class AspireJson
{
    public static JsonElement Object(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Aspire returned an invalid JSON object.");
        }
        return value;
    }

    public static string RequiredString(JsonElement value, string name) => String(value, name) is { Length: > 0 } text
        ? text : throw new InvalidDataException($"Aspire JSON is missing '{name}'.");

    public static string? String(JsonElement value, string name)
    {
        if (!Object(value).TryGetProperty(name, out JsonElement field) || field.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return field.ValueKind == JsonValueKind.String ? field.GetString()
            : throw new InvalidDataException($"Aspire JSON field '{name}' must be a string.");
    }

    public static bool Boolean(JsonElement value, string name)
    {
        if (!Object(value).TryGetProperty(name, out JsonElement field))
        {
            throw new InvalidDataException($"Aspire JSON is missing '{name}'.");
        }
        return field.ValueKind is JsonValueKind.True or JsonValueKind.False ? field.GetBoolean()
            : throw new InvalidDataException($"Aspire JSON field '{name}' must be a boolean.");
    }

    public static double Number(JsonElement value, string name) => Object(value).TryGetProperty(name, out JsonElement field)
        && field.ValueKind == JsonValueKind.Number && field.TryGetDouble(out double number) && double.IsFinite(number)
        ? number : throw new InvalidDataException($"Aspire JSON field '{name}' must be a finite number.");

    public static string Duration(JsonElement value) => !Object(value).TryGetProperty("durationMs", out JsonElement duration)
        || duration.ValueKind == JsonValueKind.Null ? "Duration unavailable" : $"{Number(value, "durationMs"):0.###} ms";

    public static IReadOnlyList<JsonElement> Array(JsonElement value, string? name = null, bool required = false)
    {
        if (name is not null)
        {
            if (!Object(value).TryGetProperty(name, out value) || value.ValueKind == JsonValueKind.Null)
            {
                return required ? throw new InvalidDataException($"Aspire JSON is missing '{name}'.") : [];
            }
        }
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Aspire JSON field '{name ?? "root"}' must be an array.");
        }
        return value.EnumerateArray().ToArray();
    }

    public static IEnumerable<JsonProperty> Properties(JsonElement value, string name)
    {
        if (!Object(value).TryGetProperty(name, out JsonElement field) || field.ValueKind == JsonValueKind.Null)
        {
            return [];
        }
        return Object(field).EnumerateObject();
    }
}
