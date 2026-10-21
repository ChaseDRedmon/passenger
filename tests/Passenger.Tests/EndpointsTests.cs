using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Endpoints;

namespace Passenger.Tests;

/// <summary>The Endpoints window, as Rider's HTTP Client: the requests of a folder's .http files listed with their
/// methods, Test sending one as written and holding its answer to the file's criteria, Interactive Test sending it as
/// changed in its dialog, and every test on a client of its own that is gone once the test is done.</summary>
public sealed class EndpointsTests : IDisposable
{
    private readonly StandInApi api = new();

    public void Dispose() => api.Dispose();

    [AvaloniaFact]
    public void The_window_lists_each_request_of_the_folders_http_files_with_its_method_and_name()
    {
        MainWindow window = Open(Folder());
        window.ShowToolWindow("endpoints");
        Ui.WaitFor(() => window.Endpoints.Requests.Count == 3, "the .http file's three requests");
        Assert.Equal(["GET", "POST", "GET"], window.Endpoints.Requests.Select(r => r.Method));
        Assert.Equal(["All items", "Add an item", "{{base}}/missing"], window.Endpoints.Requests.Select(r => r.Title));
        Assert.Equal("dev", window.Endpoints.Environment);
        window.Close();
    }

    [AvaloniaFact]
    public void Test_sends_the_request_as_written_and_holds_its_answer_to_the_files_criteria()
    {
        MainWindow window = Open(Folder());
        window.ShowToolWindow("endpoints");
        Ui.WaitFor(() => window.Endpoints.Requests.Count == 3, "the requests");
        EndpointRequest all = window.Endpoints.Requests[0];

        Click(window, RowButton(window, all, "endpointTest"));
        Ui.WaitFor(() => window.Endpoints.Response.Response is not null, "the answer");

        Assert.True(window.Endpoints.Response.Passed, $"The answer met none of its criteria: {window.Endpoints.Response.Summary}");
        Assert.StartsWith("200 OK", window.Endpoints.Response.Summary);
        Assert.Equal(3, window.Endpoints.Response.Results.Count);
        Assert.Contains("\"name\": \"Ada\"", window.Endpoints.Response.Shown);
        Assert.True(window.Endpoints.Outcome(all));
        // The environment's variable and the file's header both reached the server.
        StandInApi.Request received = Assert.Single(api.Received);
        Assert.Equal("GET /items", received.Line);
        Assert.Equal("application/json", received.Header("Accept"));

        // A request answered with an error fails, with no criteria of its own.
        Click(window, RowButton(window, window.Endpoints.Requests[2], "endpointTest"));
        Ui.WaitFor(() => window.Endpoints.Response.Response is not null, "the second answer");
        Assert.False(window.Endpoints.Response.Passed, "A 404 passed a test that expects an answer below 400");
        Assert.False(window.Endpoints.Outcome(window.Endpoints.Requests[2]));
        window.Close();
    }

    [AvaloniaFact]
    public void Every_test_is_sent_on_a_client_of_its_own_that_is_gone_when_the_test_is_done()
    {
        MainWindow window = Open(Folder());
        window.ShowToolWindow("endpoints");
        Ui.WaitFor(() => window.Endpoints.Requests.Count == 3, "the requests");
        for (int i = 0; i < 3; i++)
        {
            Click(window, RowButton(window, window.Endpoints.Requests[0], "endpointTest"));
            Ui.WaitFor(() => window.Endpoints.Response.Response is not null, "an answer");
            int test = i + 1;
            // Each client closes its connection as it is disposed, before the next test starts.
            Ui.WaitFor(() => api.Connections == test && api.Open == 0, $"test {test}'s connection, closed once it was done (connections {api.Connections}, open {api.Open})");
        }
        Assert.Equal(3, api.Received.Select(r => r.Connection).Distinct().Count());
        window.Close();
    }

    [AvaloniaFact]
    public void Interactive_test_sends_the_request_as_changed_in_its_dialog_and_checks_the_criteria_given_there()
    {
        MainWindow window = Open(Folder());
        window.ShowToolWindow("endpoints");
        Ui.WaitFor(() => window.Endpoints.Requests.Count == 3, "the requests");
        EndpointRequest add = window.Endpoints.Requests[1];
        Click(window, RowButton(window, add, "endpointInteractive"));
        InteractiveTestDialog dialog = DialogButtonsTests.Check(window.InteractiveTest ?? throw new Xunit.Sdk.XunitException("Interactive Test opened no dialog"));
        Ui.Settle();
        Assert.Equal("POST", dialog.Method.SelectedItem);
        Assert.Equal("status 201", dialog.Criteria.Text);

        Assert.Equal([(true, "Content-Type", "application/json")], dialog.Headers.Pairs);

        dialog.Method.SelectedItem = "PUT";
        // A header typed into the table's empty last row becomes a row of its own, with another empty one after it.
        KeyValueGrid.Row trace = dialog.Headers.Rows[^1];
        trace.Key.Text = "X-Trace";
        Ui.Settle();
        trace.Value.Text = "{{trace}}";
        Ui.Settle();
        Assert.Equal(3, dialog.Headers.Rows.Count);
        Assert.Equal("Headers (2)", dialog.TabTitles[1]);
        dialog.Body.Text = """{ "name": "Grace" }""";
        dialog.Criteria.Text = "status 2xx\nheader Content-Type contains json\njson $.name equals Grace\ntime < 5000";
        Click(dialog, dialog.Send);
        Ui.WaitFor(() => dialog.Response.Response is not null, "the dialog's answer");

        Assert.True(dialog.Response.Passed, $"The changed request's answer failed: {string.Join("; ", dialog.Response.Results.Where(r => !r.Passed).Select(r => $"{r.Expectation} got {r.Actual}"))}");
        StandInApi.Request received = api.Received[^1];
        Assert.Equal("PUT /items", received.Line);
        Assert.Equal("t-42", received.Header("X-Trace"));
        Assert.Equal("""{ "name": "Grace" }""", received.Body);

        // A criterion the answer misses fails and says what the answer had; the file is never changed.
        dialog.Criteria.Text = "status 200";
        Click(dialog, dialog.Send);
        Ui.WaitFor(() => dialog.Response.Response is not null, "the second answer");
        ExpectationResult failed = Assert.Single(dialog.Response.Results);
        Assert.False(failed.Passed);
        Assert.Equal("201", failed.Actual);
        Assert.Contains("POST {{base}}/items", File.ReadAllText(add.File));

        // Text that is no criterion is refused before anything is sent.
        int sent = api.Received.Count;
        dialog.Criteria.Text = "colour blue";
        Click(dialog, dialog.Send);
        Assert.Contains("no criterion", dialog.Problem);
        Assert.Equal(sent, api.Received.Count);
        dialog.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void The_urls_query_and_the_params_table_follow_each_other_and_a_param_turned_off_is_not_sent()
    {
        MainWindow window = Open(Folder());
        window.ShowToolWindow("endpoints");
        Ui.WaitFor(() => window.Endpoints.Requests.Count == 3, "the requests");
        Click(window, RowButton(window, window.Endpoints.Requests[0], "endpointInteractive"));
        InteractiveTestDialog dialog = window.InteractiveTest!;
        Ui.Settle();
        Assert.Equal(0, dialog.PageShown);
        Assert.Empty(dialog.Params.Pairs);

        // Rows typed into the table write the URL's query, {{variables}} as written.
        KeyValueGrid.Row page = dialog.Params.Rows[^1];
        page.Key.Text = "page";
        Ui.Settle();
        page.Value.Text = "2";
        Ui.Settle();
        KeyValueGrid.Row query = dialog.Params.Rows[^1];
        query.Key.Text = "q";
        Ui.Settle();
        query.Value.Text = "{{trace}}";
        Ui.Settle();
        Assert.Equal("{{base}}/items?page=2&q={{trace}}", dialog.Url.Text);
        Assert.Equal("Params (2)", dialog.TabTitles[0]);

        // A row turned off leaves the URL and stays in the table.
        dialog.Params.Rows[0].On.IsChecked = false;
        Ui.Settle();
        Assert.Equal("{{base}}/items?q={{trace}}", dialog.Url.Text);
        Assert.Equal([(false, "page", "2"), (true, "q", "{{trace}}")], dialog.Params.Pairs);

        // The URL typed: the table takes its query, the row turned off kept after it.
        dialog.Url.Text = "{{base}}/items?limit=5&q=x";
        Ui.Settle();
        Assert.Equal([(true, "limit", "5"), (true, "q", "x"), (false, "page", "2")], dialog.Params.Pairs);

        Click(dialog, dialog.Send);
        Ui.WaitFor(() => dialog.Response.Response is not null, "the answer");
        Assert.Equal("GET /items?limit=5&q=x", api.Received[^1].Line);
        dialog.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void A_request_whose_variable_has_no_value_says_which_and_sends_nothing()
    {
        string folder = Folder();
        File.WriteAllText(Path.Combine(folder, "http-client.env.json"), """{ "prod": { "base": "https://example.invalid" } }""");
        MainWindow window = Open(folder);
        window.ShowToolWindow("endpoints");
        Ui.WaitFor(() => window.Endpoints.Requests.Count == 3 && window.Endpoints.Environment == "prod", "the requests in prod");
        window.Endpoints.ChooseEnvironment("No environment");
        Ui.Settle();
        Click(window, RowButton(window, window.Endpoints.Requests[0], "endpointTest"));
        Assert.Contains("{{base}}", window.Endpoints.Response.Summary);
        Assert.Empty(api.Received);
        window.Close();
    }

    [AvaloniaFact]
    public void An_http_file_written_on_disk_is_listed_again()
    {
        string folder = Folder();
        MainWindow window = Open(folder);
        window.ShowToolWindow("endpoints");
        Ui.WaitFor(() => window.Endpoints.Requests.Count == 3, "the requests");
        File.WriteAllText(Path.Combine(folder, "more.rest"), "DELETE {{base}}/items/1\n");
        Ui.WaitFor(() => window.Endpoints.Requests.Count == 4, "the new file's request");
        Assert.Contains(window.Endpoints.Requests, r => r.Method == "DELETE");

        // A file written while the window is closed is there as it is shown again.
        window.DockHost.Hide("endpoints");
        Ui.Settle();
        File.WriteAllText(Path.Combine(folder, "later.http"), "PATCH {{base}}/items/1\n");
        window.ShowToolWindow("endpoints");
        Ui.WaitFor(() => window.Endpoints.Requests.Count == 5, "the request of the file written while the window was closed");
        window.Close();
    }

    [AvaloniaFact]
    public void An_answer_longer_than_passenger_reads_is_cut_there_and_says_so()
    {
        string folder = Folder();
        File.WriteAllText(Path.Combine(folder, "huge.http"), "GET {{base}}/huge\n");
        MainWindow window = Open(folder);
        window.ShowToolWindow("endpoints");
        Ui.WaitFor(() => window.Endpoints.Requests.Count == 4, "the requests");
        Click(window, RowButton(window, window.Endpoints.Requests.Single(r => r.Url.EndsWith("/huge", StringComparison.Ordinal)), "endpointTest"));
        Ui.WaitFor(() => window.Endpoints.Response.Response is not null, "the huge answer");

        EndpointResponse answer = window.Endpoints.Response.Response!;
        Assert.True(answer.Error is null, $"The huge answer failed: {answer.Error}");
        Assert.True(answer.Cut, "An answer longer than Passenger reads was read whole");
        Assert.Equal(EndpointClient.MostRead, answer.Size);
        Assert.Contains("the rest unread", window.Endpoints.Response.Summary);
        Assert.EndsWith("more characters not shown", window.Endpoints.Response.Shown);
        window.Close();
    }

    [AvaloniaFact]
    public void A_request_whose_variables_name_each_other_over_and_over_sends_nothing_and_says_why()
    {
        string folder = Folder();
        // Each variable names the next ten times, ten deep: ten billion references filled in, were nothing to stop it.
        StringBuilder file = new();
        for (int i = 0; i < 10; i++)
        {
            string next = "{{v" + (i + 1).ToString(CultureInfo.InvariantCulture) + "}}";
            file.Append(CultureInfo.InvariantCulture, $"@v{i} = {string.Concat(Enumerable.Repeat(next, 10))}\n");
        }
        file.Append("@v10 = x\n\nGET {{base}}/items?q={{v0}}\n");
        File.WriteAllText(Path.Combine(folder, "laughs.http"), file.ToString());
        MainWindow window = Open(folder);
        window.ShowToolWindow("endpoints");
        Ui.WaitFor(() => window.Endpoints.Requests.Count == 4, "the requests");

        Click(window, RowButton(window, window.Endpoints.Requests.Single(r => r.File.EndsWith("laughs.http", StringComparison.Ordinal)), "endpointTest"));
        Assert.Contains("over and over", window.Endpoints.Response.Summary);
        Assert.Empty(api.Received);
        window.Close();
    }

    private string Folder()
    {
        string folder = TestFolders.Create("endpoints");
        File.WriteAllText(Path.Combine(folder, "api.http"), """
            @trace = t-42

            ### All items
            # @expect status 200
            # @expect header Content-Type contains json
            # @expect json $.items[0].name equals Ada
            GET {{base}}/items
            Accept: application/json

            ###
            # @name Add an item
            # @expect status 201
            POST {{base}}/items HTTP/1.1
            Content-Type: application/json

            { "name": "Ada" }

            > {%
                client.test("made", () => {});
            %}

            ###
            {{base}}/missing
            """);
        File.WriteAllText(Path.Combine(folder, "http-client.env.json"), $$"""{ "dev": { "base": "{{api.Root}}" } }""");
        return folder;
    }

    private static MainWindow Open(string folder)
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }

    private static Button RowButton(MainWindow window, EndpointRequest request, string name)
    {
        Button? found = null;
        Ui.WaitFor(() => (found = window.Endpoints.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Name == name && Ancestors(b).Any(a => ReferenceEquals(a.DataContext, request)))) is not null, $"{request.Title}'s {name} button");
        return found!;

        static IEnumerable<Visual> Ancestors(Visual visual) => visual.GetVisualAncestors();
    }

    private static void Click(Window window, Control control)
    {
        Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
    }

    /// <summary>A small JSON API on a loopback port: GET /items lists one item, POST and PUT /items answer 201 with the
    /// body sent, anything else 404. It keeps every request with the connection it came on, and counts connections made
    /// and still open, so a test sees whether a client was kept.</summary>
    private sealed class StandInApi : IDisposable
    {
        public sealed record Request(int Connection, string Line, IReadOnlyDictionary<string, string> Headers, string Body)
        {
            public string? Header(string name) => Headers.TryGetValue(name, out string? value) ? value : null;
        }

        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly List<Request> received = [];
        private int connections;
        private int open;

        public StandInApi()
        {
            listener.Start();
            Root = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            _ = Task.Run(AcceptAsync);
        }

        public string Root { get; }

        public int Connections => Volatile.Read(ref connections);

        public int Open => Volatile.Read(ref open);

        public IReadOnlyList<Request> Received
        {
            get
            {
                lock (received)
                {
                    return [.. received];
                }
            }
        }

        public void Dispose() => listener.Stop();

        private async Task AcceptAsync()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                }
                catch (Exception error) when (error is SocketException or ObjectDisposedException)
                {
                    return;
                }
                int id = Interlocked.Increment(ref connections);
                Interlocked.Increment(ref open);
                _ = Task.Run(() => ServeAsync(client, id));
            }
        }

        private async Task ServeAsync(TcpClient client, int id)
        {
            try
            {
                using (client)
                {
                    NetworkStream stream = client.GetStream();
                    // Keep-alive: requests are read until the client closes, so a client used twice would be seen.
                    while (await ReadAsync(stream, id) is { } request)
                    {
                        lock (received)
                        {
                            received.Add(request);
                        }
                        (int status, string reason, string body) = Answer(request);
                        byte[] content = Encoding.UTF8.GetBytes(body);
                        string head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {content.Length}\r\n\r\n";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                        await stream.WriteAsync(content);
                    }
                }
            }
            catch (IOException)
            {
            }
            finally
            {
                Interlocked.Decrement(ref open);
            }
        }

        private static (int, string, string) Answer(Request request) => request.Line switch
        {
            "GET /items" => (200, "OK", """{"items":[{"id":1,"name":"Ada"}]}"""),
            "GET /huge" => (200, "OK", string.Concat(Enumerable.Repeat(new string('a', 99) + "\n", (EndpointClient.MostRead / 100) + 10_000))),
            "POST /items" or "PUT /items" => (201, "Created", request.Body),
            _ => (404, "Not Found", """{"error":"not found"}"""),
        };

        private static async Task<Request?> ReadAsync(NetworkStream stream, int id)
        {
            List<byte> head = [];
            byte[] one = new byte[1];
            while (!(head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n'))
            {
                if (await stream.ReadAsync(one) == 0)
                {
                    return null;
                }
                head.Add(one[0]);
            }
            string[] lines = Encoding.ASCII.GetString([.. head]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines.Skip(1))
            {
                int colon = line.IndexOf(':');
                headers[line[..colon]] = line[(colon + 1)..].Trim();
            }
            byte[] body = new byte[headers.TryGetValue("Content-Length", out string? length) ? int.Parse(length) : 0];
            await stream.ReadExactlyAsync(body);
            string[] request = lines[0].Split(' ');
            return new Request(id, $"{request[0]} {request[1]}", headers, Encoding.UTF8.GetString(body));
        }
    }
}
