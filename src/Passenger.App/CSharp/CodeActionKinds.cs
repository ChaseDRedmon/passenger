namespace Passenger.App.CSharp;

/// <summary>What Show Context Actions says beside a language server's action: Rider's name for what it is, a quick-fix or a
/// context action, which Rider's list tells by the action's icon. Never the protocol's own kind ("quickfix",
/// "refactor.inline"), which no Rider list shows; a kind Rider has no name for says nothing.</summary>
internal static class CodeActionKinds
{
    // Every refactoring offered at the caret is one of Rider's context actions there; Roslyn marks only some of its Extract
    // and Inline ones refactor.extract and refactor.inline (Inline Method is a plain refactor), so sub-kinds tell nothing.
    public static string Shown(string? kind) => kind switch
    {
        null => string.Empty,
        _ when Is(kind, "quickfix") => "Quick-fix",
        _ when Is(kind, "refactor") => "Context action",
        _ => string.Empty,
    };

    /// <summary>Whether a kind is the one named or under it, as the protocol's kinds nest by their dots.</summary>
    private static bool Is(string kind, string name) =>
        kind == name || kind.StartsWith(name + ".", StringComparison.Ordinal);
}
