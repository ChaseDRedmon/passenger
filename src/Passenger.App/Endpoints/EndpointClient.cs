using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Passenger.App.Endpoints;

/// <summary>A request ready to send: every variable resolved, the body's <c>&lt; file</c> read.</summary>
public sealed record EndpointCall(string Method, string Url, IReadOnlyList<KeyValuePair<string, string>> Headers, string Body);

/// <summary>How a test sends: how long it waits, whether it follows a redirect, and whether it takes a certificate the
/// system does not trust (a development server's own).</summary>
public sealed record SendOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool FollowRedirects { get; init; } = true;
    public bool AcceptAnyCertificate { get; init; }
}

/// <summary>What came back, or why nothing did (<see cref="Error"/>); headers are the response's and its content's.</summary>
public sealed record EndpointResponse
{
    public int Status { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];
    public string Body { get; init; } = string.Empty;
    public long Size { get; init; }

    /// <summary>Whether the body is only the first <see cref="EndpointClient.MostRead"/> bytes of a longer answer.</summary>
    public bool Cut { get; init; }

    public TimeSpan Elapsed { get; init; }
    public string? Error { get; init; }

    public string? ContentType => Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>The most of a body shown, in characters: a text box laid out over more holds the UI thread up.</summary>
    public const int MostShown = 256 * 1024;

    /// <summary>The body as it reads best: JSON indented, anything else as it came; a longer one's first
    /// <see cref="MostShown"/> characters as they came, saying how much more there is.</summary>
    public string Pretty
    {
        get
        {
            if (Body.Length > MostShown)
            {
                return $"{Body[..MostShown]}\n… {Body.Length - MostShown:N0} more characters not shown";
            }
            string trimmed = Body.TrimStart();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(Body);
                    return JsonSerializer.Serialize(document.RootElement, Indented);
                }
                catch (JsonException)
                {
                }
            }
            return Body;
        }
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}

/// <summary>Sends one request on a client of its own: each test makes its handler and <see cref="HttpClient"/>, sends,
/// reads the whole response and disposes both before it answers, so no connection, cookie or pooled socket outlives
/// the test and one test never sees another's state.</summary>
public static class EndpointClient
{
    /// <summary>The most of an answer's body read, decompressed: the rest is left unread, so a huge, endless or
    /// highly compressed answer neither fills memory nor holds the test.</summary>
    public const int MostRead = 16 * 1024 * 1024;

    /// <summary>The headers .NET keeps on a request's content rather than on the request.</summary>
    private static readonly HashSet<string> ContentHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Allow", "Content-Disposition", "Content-Encoding", "Content-Language", "Content-Length", "Content-Location",
        "Content-MD5", "Content-Range", "Content-Type", "Expires", "Last-Modified",
    };

    /// <summary>A request of a file made ready in an environment's variables, or why it cannot be sent.</summary>
    public static (EndpointCall? Call, string? Problem) Prepare(EndpointRequest request, string method, string url,
        IEnumerable<KeyValuePair<string, string>> headers, string body, IReadOnlyDictionary<string, string> variables)
    {
        HttpVariables.Filling filling = new();
        string resolvedUrl = HttpVariables.Resolve(url, variables, filling).Trim();
        List<KeyValuePair<string, string>> resolvedHeaders = [.. headers.Select(h => new KeyValuePair<string, string>(
            HttpVariables.Resolve(h.Key, variables, filling), HttpVariables.Resolve(h.Value, variables, filling)))];
        string resolvedBody = HttpVariables.Resolve(body, variables, filling);
        if (filling.Overflowed)
        {
            return (null, $"The request's variables fill in more than {HttpVariables.MostFilledIn:N0} characters: does one name another over and over?");
        }
        HashSet<string> unresolved = filling.Unresolved;
        if (unresolved.Count > 0)
        {
            return (null, $"No value for {string.Join(", ", unresolved.Select(u => "{{" + u + "}}"))}: set it in the file (@{unresolved.First()} = …) or in {HttpVariables.PublicFile}");
        }
        // A body that is "< ./file.json" alone is that file's text, as Rider sends it.
        if (resolvedBody.Trim() is ['<', ' ', .. string included] && !included.Contains('\n'))
        {
            string file = Path.GetFullPath(included.Trim(), Path.GetDirectoryName(request.File)!);
            try
            {
                resolvedBody = File.ReadAllText(file);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return (null, $"The body's file {file} could not be read: {error.Message}");
            }
        }
        if (!resolvedUrl.Contains("://", StringComparison.Ordinal))
        {
            resolvedUrl = "http://" + resolvedUrl;
        }
        if (!Uri.TryCreate(resolvedUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
        {
            return (null, $"\"{resolvedUrl}\" is no http or https URL");
        }
        return (new EndpointCall(method.Trim().ToUpperInvariant(), uri.AbsoluteUri, resolvedHeaders, resolvedBody), null);
    }

    public static async Task<EndpointResponse> SendAsync(EndpointCall call, SendOptions options, CancellationToken cancel)
    {
        Stopwatch clock = Stopwatch.StartNew();
        // The timeout holds the whole exchange, the body's reading included, which HttpClient's own leaves out once the
        // headers are in.
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(options.Timeout);
        try
        {
            using SocketsHttpHandler handler = new()
            {
                AllowAutoRedirect = options.FollowRedirects,
                UseCookies = false,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                ConnectTimeout = options.Timeout,
            };
            if (options.AcceptAnyCertificate)
            {
                handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            }
            using HttpClient client = new(handler, disposeHandler: false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            using HttpRequestMessage message = new(new HttpMethod(call.Method), call.Url);
            if (call.Body.Length > 0 || call.Headers.Any(h => ContentHeaders.Contains(h.Key)))
            {
                message.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(call.Body));
            }
            foreach ((string name, string value) in call.Headers)
            {
                bool added = ContentHeaders.Contains(name) && message.Content is not null
                    ? message.Content.Headers.TryAddWithoutValidation(name, value)
                    : message.Headers.TryAddWithoutValidation(name, value);
                if (!added)
                {
                    return new EndpointResponse { Error = $"The header {name} could not be sent as \"{value}\"", Elapsed = clock.Elapsed };
                }
            }
            using HttpResponseMessage response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            (byte[] bytes, bool cut) = await ReadAtMostAsync(response.Content, limit.Token).ConfigureAwait(false);
            TimeSpan elapsed = clock.Elapsed;
            return new EndpointResponse
            {
                Status = (int)response.StatusCode,
                Reason = response.ReasonPhrase ?? string.Empty,
                Version = $"HTTP/{response.Version}",
                Headers = [.. Flatten(response.Headers), .. Flatten(response.Content.Headers)],
                Body = Decode(bytes, response.Content.Headers.ContentType),
                Size = bytes.LongLength,
                Cut = cut,
                Elapsed = elapsed,
            };
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new EndpointResponse { Error = $"No answer within {options.Timeout.TotalSeconds:0.#} s", Elapsed = clock.Elapsed };
        }
        catch (HttpRequestException error)
        {
            return new EndpointResponse { Error = error.InnerException?.Message ?? error.Message, Elapsed = clock.Elapsed };
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or FormatException or IOException)
        {
            return new EndpointResponse { Error = error.Message, Elapsed = clock.Elapsed };
        }
    }

    private static async Task<(byte[] Bytes, bool Cut)> ReadAtMostAsync(HttpContent content, CancellationToken cancel)
    {
        await using Stream stream = await content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using MemoryStream read = new();
        byte[] buffer = new byte[81920];
        while (read.Length <= MostRead)
        {
            int count = await stream.ReadAsync(buffer, cancel).ConfigureAwait(false);
            if (count == 0)
            {
                return (read.ToArray(), false);
            }
            read.Write(buffer, 0, count);
        }
        return (read.GetBuffer()[..MostRead], true);
    }

    private static IEnumerable<KeyValuePair<string, string>> Flatten(HttpHeaders headers) =>
        headers.Select(h => new KeyValuePair<string, string>(h.Key, string.Join(", ", h.Value)));

    private static string Decode(byte[] bytes, MediaTypeHeaderValue? type)
    {
        Encoding encoding = Encoding.UTF8;
        if (type?.CharSet is { Length: > 0 } charset)
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
            }
        }
        return encoding.GetString(bytes);
    }
}
