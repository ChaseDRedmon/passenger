using System.Reflection;
using Avalonia;
using Avalonia.Threading;

namespace Passenger.Inspector;

/// <summary>The inspector inside the app, started by the startup hook in its own context: it connects to Passenger, waits
/// for the app's UI thread to exist (never touching the dispatcher before the app makes it, which would make the wrong
/// thread the UI's), says hello, and answers each request there. It ends when Passenger goes.</summary>
public static class Agent
{
    /// <summary>How often it looks for the app's UI before it is up.</summary>
    private static readonly TimeSpan Look = TimeSpan.FromMilliseconds(100);

    public static void Start(string socket, Thread main, string avalonia)
    {
        Thread thread = new(() => Serve(socket, main, avalonia)) { IsBackground = true, Name = "Passenger inspector" };
        thread.Start();
    }

    private static void Serve(string socket, Thread main, string avalonia)
    {
        using Connection? connection = Connection.Open(socket);
        if (connection is null)
        {
            return;
        }
        Dispatcher dispatcher = WaitForUi(main);
        Inspector inspector;
        try
        {
            inspector = dispatcher.Invoke(() => new Inspector(connection));
        }
        catch (Exception error)
        {
            connection.Send(new Reply { Event = Wire.Hello, Hello = new Hello(Wire.Version, Environment.ProcessId, avalonia, $"The inspector could not start: {error.Message}") });
            return;
        }
        connection.Send(new Reply { Event = Wire.Hello, Hello = new Hello(Wire.Version, Environment.ProcessId, avalonia, null) });
        while (connection.Read() is { } request)
        {
            _ = AnswerAsync(dispatcher, inspector, connection, request);
        }
        dispatcher.Post(inspector.Stop);
    }

    /// <summary>The app's UI dispatcher once the app has made it and its Application: the main thread's in a desktop app, or
    /// whichever thread made Avalonia's, read without making one.</summary>
    private static Dispatcher WaitForUi(Thread main)
    {
        FieldInfo? made = typeof(Dispatcher).GetField("s_uiThread", BindingFlags.NonPublic | BindingFlags.Static);
        while (true)
        {
            if ((Dispatcher.FromThread(main) ?? made?.GetValue(null) as Dispatcher) is { } dispatcher && HasApplication())
            {
                return dispatcher;
            }
            Thread.Sleep(Look);
        }
    }

    /// <summary>Whether the app's Application is up, after which its dispatcher has its platform's loop.</summary>
    private static bool HasApplication()
    {
        try
        {
            return Application.Current is not null;
        }
        catch (Exception)
        {
            // Read off the UI thread while the app is setting up: asked again in a moment.
            return false;
        }
    }

    private static async Task AnswerAsync(Dispatcher dispatcher, Inspector inspector, Connection connection, Request request)
    {
        Reply reply;
        try
        {
            reply = await dispatcher.InvokeAsync(() => inspector.Handle(request));
        }
        catch (Exception error)
        {
            reply = new Reply { Id = request.Id, Error = error.Message };
        }
        connection.Send(reply);
    }
}
