using Passenger.Lsp;

namespace Passenger.App.CSharp;

/// <summary>A language server a window leaves running as it closes (its projects loaded without a problem), for the next
/// window that opens the same solution or projects with the same options to take up, loaded and warm, rather than start
/// and load its own: what a test class gives its windows on one project (<see cref="MainWindow.WarmServer"/>). One no
/// window takes is ended by the next one kept, by <see cref="Drop"/> or with the holder. The IDE's windows have none.</summary>
public sealed class WarmServer : IAsyncDisposable
{
    private readonly Lock gate = new();
    private RoslynSession? kept;

    /// <summary>The server kept, if it loaded what a window opens with the options it would start one with, and is still
    /// running; it is the window's from then on. One that does not match stays until another is kept or it is dropped.</summary>
    internal RoslynSession? Take(string folder, string? solution, IReadOnlyList<string> projects, IReadOnlyDictionary<string, object?> options)
    {
        lock (gate)
        {
            if (kept is not { IsLoaded: true } session || session.Server.HasExited || session.Folder != folder || session.Solution != solution
                || (solution is null && !session.Projects.SequenceEqual(projects, StringComparer.Ordinal)) || !Same(session.Server.Options, options))
            {
                return null;
            }
            kept = null;
            return session;
        }
    }

    /// <summary>Keeps a window's server for the next window, ending the one kept before, which no window took.</summary>
    internal Task Keep(RoslynSession session)
    {
        RoslynSession? before;
        lock (gate)
        {
            before = kept;
            kept = session;
        }
        return before is null || before == session ? Task.CompletedTask : Task.Run(() => before.DisposeAsync().AsTask());
    }

    /// <summary>Ends the server kept, so the next window starts its own.</summary>
    public Task Drop()
    {
        RoslynSession? ending;
        lock (gate)
        {
            ending = kept;
            kept = null;
        }
        return ending is null ? Task.CompletedTask : Task.Run(() => ending.DisposeAsync().AsTask());
    }

    public ValueTask DisposeAsync() => new(Drop());

    /// <summary>Whether two sets of options are the same: one set to its default (null) is not one never set, which the
    /// server takes from Passenger's own.</summary>
    private static bool Same(IReadOnlyDictionary<string, object?> set, IReadOnlyDictionary<string, object?> wanted) =>
        set.Count == wanted.Count && set.All(option => wanted.TryGetValue(option.Key, out object? value) && Equals(option.Value, value));
}
