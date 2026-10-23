using Passenger.Lsp;

namespace Passenger.App.CSharp;

/// <summary>Going between a web route and the .http requests that call it, from the window's routes: Go to Declaration from
/// a request to its route, Find Usages from a route to its requests.</summary>
public sealed partial class CodeAssist
{
    /// <summary>The routes the request on a line (the editor's, from zero) of an .http file calls.</summary>
    public Func<string, int, IReadOnlyList<LspLocation>>? HttpRouteTargets { get; set; }

    /// <summary>The .http requests that call the route a file declares at an offset, with the route's words; null where it
    /// declares none.</summary>
    public Func<string, int, (string Title, IReadOnlyList<LspLocation> Requests)?>? RouteCallers { get; set; }

    /// <summary>Whether the file shown is an .http or .rest file, whose requests go to their routes.</summary>
    private bool IsHttpFile(Workspace.OpenDocument? file) => file is not null && HttpRouteTargets is not null && Endpoints.HttpFile.Is(file.Path);

    /// <summary>Go to Declaration in an .http file: the route the request at the caret calls.</summary>
    private bool HttpGoTo(int offset)
    {
        if (editors.Active is not { } shown || !IsHttpFile(shown))
        {
            return false;
        }
        int line = Editor.Document.Snapshot.GetPosition(Math.Min(offset, Editor.Document.Snapshot.Length)).Line;
        Go(HttpRouteTargets!(shown.Path, line), offset, "Routes", "No route of the folder's web projects takes this request");
        return true;
    }

    /// <summary>Find Usages on a route's declaration: the .http requests that call it.</summary>
    private bool RouteUsages(int offset)
    {
        if (editors.Active is not { } shown || RouteCallers?.Invoke(shown.Path, offset) is not { } callers)
        {
            return false;
        }
        if (callers.Requests.Count == 0)
        {
            Message?.Invoke(this, $"No .http request calls {callers.Title}");
        }
        else
        {
            UsagesFound?.Invoke(this, ($"Requests to {callers.Title}", callers.Requests));
        }
        return true;
    }
}
