using System.Text.Json;
using System.Text.Json.Serialization;

namespace Passenger.Lsp;

// The Language Server Protocol 3.17, as much of it as Passenger speaks, in its own field names
// (serialized camelCase). Where the protocol allows several shapes for one field, the field is a
// JsonElement and Shapes.cs reads it.

public sealed record LspPosition(int Line, int Character);

public sealed record LspRange(LspPosition Start, LspPosition End);

public sealed record LspLocation(string Uri, LspRange Range);

/// <summary>A call or type hierarchy's item: a symbol, where it is and its name's place there; Data is the server's,
/// sent back as it came when its callers, callees, supertypes or subtypes are asked for.</summary>
public sealed class HierarchyItem
{
    public string Name { get; init; } = string.Empty;
    public int Kind { get; init; }
    public string? Detail { get; init; }
    public string Uri { get; init; } = string.Empty;
    public LspRange Range { get; init; } = new(new LspPosition(0, 0), new LspPosition(0, 0));
    public LspRange SelectionRange { get; init; } = new(new LspPosition(0, 0), new LspPosition(0, 0));
    public System.Text.Json.JsonElement? Data { get; init; }
}

public sealed record HierarchyItemParams(HierarchyItem Item);

/// <summary>A caller and where in it the calls are.</summary>
public sealed record IncomingCall(HierarchyItem From, IReadOnlyList<LspRange> FromRanges);

/// <summary>A callee and where in the caller the calls to it are.</summary>
public sealed record OutgoingCall(HierarchyItem To, IReadOnlyList<LspRange> FromRanges);

public sealed record LspLocationLink(LspRange? OriginSelectionRange, string TargetUri, LspRange TargetRange, LspRange TargetSelectionRange);

public sealed record TextDocumentIdentifier(string Uri);

public sealed record VersionedTextDocumentIdentifier(string Uri, int Version);

public sealed record TextDocumentItem(string Uri, string LanguageId, int Version, string Text);

public sealed record TextDocumentContentChangeEvent(LspRange? Range, string Text);

public sealed record DidOpenTextDocumentParams(TextDocumentItem TextDocument);

public sealed record DidChangeTextDocumentParams(VersionedTextDocumentIdentifier TextDocument, IReadOnlyList<TextDocumentContentChangeEvent> ContentChanges);

public sealed record DidCloseTextDocumentParams(TextDocumentIdentifier TextDocument);

public sealed record DidSaveTextDocumentParams(TextDocumentIdentifier TextDocument);

public sealed record TextDocumentPositionParams(TextDocumentIdentifier TextDocument, LspPosition Position);

public sealed record LspTextEdit(LspRange Range, string NewText);

public sealed record MarkupContent(string Kind, string Value);

public sealed record WorkspaceFolder(string Uri, string Name);

public sealed record InitializeParams(int ProcessId, string RootUri, IReadOnlyList<WorkspaceFolder> WorkspaceFolders, JsonElement Capabilities, string Locale = "en");

/// <summary>Roslyn's own: open a solution, or the projects of a folder with none.</summary>
public sealed record OpenSolutionParams(string Solution);

public sealed record OpenProjectParams(IReadOnlyList<string> Projects);

// Completion

public sealed record CompletionContext(int TriggerKind, string? TriggerCharacter = null);

public sealed record CompletionParams(TextDocumentIdentifier TextDocument, LspPosition Position, CompletionContext Context);

public sealed class CompletionList
{
    public bool IsIncomplete { get; init; }
    public List<CompletionItem> Items { get; init; } = [];
    public CompletionItemDefaults? ItemDefaults { get; init; }
}

public sealed class CompletionItemDefaults
{
    public JsonElement? EditRange { get; init; }
    public List<string>? CommitCharacters { get; init; }
    public JsonElement? Data { get; init; }
}

public sealed class CompletionItem
{
    public string Label { get; init; } = string.Empty;
    public CompletionItemLabelDetails? LabelDetails { get; init; }
    public int? Kind { get; init; }
    public string? Detail { get; init; }
    public JsonElement? Documentation { get; init; }
    public string? SortText { get; init; }
    public string? FilterText { get; init; }
    public string? InsertText { get; init; }
    public int? InsertTextFormat { get; init; }
    public LspTextEdit? TextEdit { get; init; }
    public string? TextEditText { get; init; }
    public List<LspTextEdit>? AdditionalTextEdits { get; init; }
    public List<string>? CommitCharacters { get; init; }
    public bool? Preselect { get; init; }
    public LspCommand? Command { get; init; }
    public JsonElement? Data { get; init; }
}

public sealed record CompletionItemLabelDetails(string? Detail, string? Description);

// Hover, signature help, highlights

public sealed class Hover
{
    public JsonElement Contents { get; init; }
    public LspRange? Range { get; init; }
}

public sealed class SignatureHelp
{
    public List<SignatureInformation> Signatures { get; init; } = [];
    public int? ActiveSignature { get; init; }
    public int? ActiveParameter { get; init; }
}

public sealed class SignatureInformation
{
    public string Label { get; init; } = string.Empty;
    public JsonElement? Documentation { get; init; }
    public List<ParameterInformation>? Parameters { get; init; }
    public int? ActiveParameter { get; init; }
}

public sealed class ParameterInformation
{
    public JsonElement Label { get; init; }
    public JsonElement? Documentation { get; init; }
}

public sealed record DocumentHighlight(LspRange Range, int? Kind);

// References, rename, code actions, formatting

public sealed record ReferenceContext(bool IncludeDeclaration);

public sealed record ReferenceParams(TextDocumentIdentifier TextDocument, LspPosition Position, ReferenceContext Context);

public sealed record RenameParams(TextDocumentIdentifier TextDocument, LspPosition Position, string NewName);

public sealed record CodeActionContext(IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<string>? Only = null, int? TriggerKind = null);

public sealed record CodeActionParams(TextDocumentIdentifier TextDocument, LspRange Range, CodeActionContext Context);

public sealed class CodeAction
{
    public string Title { get; init; } = string.Empty;
    public string? Kind { get; init; }
    public bool? IsPreferred { get; init; }
    public JsonElement? Disabled { get; init; }
    public JsonElement? Edit { get; init; }
    public LspCommand? Command { get; init; }
    public JsonElement? Data { get; init; }
    public List<Diagnostic>? Diagnostics { get; init; }
}

/// <summary>A command a completion item or a code action carries, run once it is chosen, after its edit. Roslyn's are
/// the client's own to run (<see cref="ClientCommands"/>).</summary>
public sealed record LspCommand(string Title, string Command, IReadOnlyList<JsonElement>? Arguments = null);

/// <summary>Roslyn's codeAction/resolveFixAll: a fix-all's data, as its code action carried it, and the scope chosen.</summary>
public sealed record FixAllCodeActionParams(string Title, JsonElement Data, string Scope);

public sealed record FormattingOptions(int TabSize, bool InsertSpaces);

public sealed record DocumentFormattingParams(TextDocumentIdentifier TextDocument, FormattingOptions Options);

public sealed record DocumentRangeFormattingParams(TextDocumentIdentifier TextDocument, LspRange Range, FormattingOptions Options);

// Symbols

public sealed record WorkspaceSymbolParams(string Query);

public sealed record DocumentSymbolParams(TextDocumentIdentifier TextDocument);

public sealed record FoldingRangeParams(TextDocumentIdentifier TextDocument);

/// <summary>A symbol found by name across the workspace. Kind is the protocol's SymbolKind number.</summary>
public sealed record SymbolInformation(string Name, int Kind, LspLocation Location, string? ContainerName = null);

/// <summary>A symbol in a document's outline, its members as children. Range is the whole
/// declaration; SelectionRange is its name.</summary>
public sealed record DocumentSymbol(string Name, string? Detail, int Kind, LspRange Range, LspRange SelectionRange, IReadOnlyList<DocumentSymbol>? Children = null);

public static class SymbolKinds
{
    private static readonly string[] Names =
    [
        "file", "module", "namespace", "package", "class", "method", "property", "field", "constructor", "enum", "interface",
        "function", "variable", "constant", "string", "number", "boolean", "array", "object", "key", "null", "enum member",
        "struct", "event", "operator", "type parameter",
    ];

    public static string Name(int kind) => kind >= 1 && kind <= Names.Length ? Names[kind - 1] : "symbol";
}

// Semantic tokens

public sealed record SemanticTokensParams(TextDocumentIdentifier TextDocument);

public sealed record SemanticTokensRangeParams(TextDocumentIdentifier TextDocument, LspRange Range);

public sealed class SemanticTokens
{
    public string? ResultId { get; init; }

    [JsonConverter(typeof(TokenData))]
    public int[] Data { get; init; } = [];
}

/// <summary>A file's tokens, five numbers each, read into one array of their count: a long file's are a million numbers,
/// which the serializer's own reading grows a list to hold, doubling it, before copying it out.</summary>
public sealed class TokenData : JsonConverter<int[]>
{
    public override int[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Semantic tokens are an array of numbers, not {reader.TokenType}");
        }
        // A converter is given its whole value, so a copy of the reader can count it first.
        Utf8JsonReader counting = reader;
        int count = 0;
        while (counting.Read() && counting.TokenType == JsonTokenType.Number)
        {
            count++;
        }
        int[] data = new int[count];
        for (int i = 0; i < count; i++)
        {
            reader.Read();
            data[i] = reader.GetInt32();
        }
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("Semantic tokens hold something other than numbers");
        }
        return data;
    }

    public override void Write(Utf8JsonWriter writer, int[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (int number in value)
        {
            writer.WriteNumberValue(number);
        }
        writer.WriteEndArray();
    }
}

public sealed record SemanticTokensLegend(IReadOnlyList<string> TokenTypes, IReadOnlyList<string> TokenModifiers);

// Diagnostics

public sealed class Diagnostic
{
    public LspRange Range { get; init; } = new(new LspPosition(0, 0), new LspPosition(0, 0));
    public int? Severity { get; init; }
    public JsonElement? Code { get; init; }
    public string? Source { get; init; }
    public string Message { get; init; } = string.Empty;
    public List<int>? Tags { get; init; }
    public JsonElement? Data { get; init; }
}

public sealed record DocumentDiagnosticParams(TextDocumentIdentifier TextDocument, string? Identifier = null, string? PreviousResultId = null);

/// <summary>A workspace diagnostics request for one source, with the result each document last had, so documents that
/// have not changed come back as unchanged.</summary>
public sealed record WorkspaceDiagnosticParams(string? Identifier, IReadOnlyList<PreviousResultId> PreviousResultIds);

public sealed record PreviousResultId(string Uri, string Value);

public sealed class WorkspaceDiagnosticReport
{
    public List<WorkspaceDocumentDiagnosticReport> Items { get; init; } = [];
}

/// <summary>One document's diagnostics in a workspace report: all of them ("full"), or "unchanged" since its result id.</summary>
public sealed class WorkspaceDocumentDiagnosticReport
{
    public string Kind { get; init; } = "full";
    public string? ResultId { get; init; }
    public List<Diagnostic>? Items { get; init; }
    public string Uri { get; init; } = string.Empty;
    public int? Version { get; init; }
}

public sealed class DocumentDiagnosticReport
{
    public string Kind { get; init; } = "full";
    public string? ResultId { get; init; }
    public List<Diagnostic>? Items { get; init; }
}

public sealed record PublishDiagnosticsParams(string Uri, int? Version, IReadOnlyList<Diagnostic> Diagnostics);

// Messages from the server

/// <summary>A message the server logs or shows, its type the protocol's: 1 an error, 2 a warning, 3 information, 4 a log
/// line, 5 debug.</summary>
public sealed record LogMessageParams(int Type, string Message);

/// <summary>Roslyn's <c>window/_roslyn_showToast</c>: a message to show, as showMessage's.</summary>
public sealed record ShowToastParams(int MessageType, string Message);

/// <summary>A <c>$/progress</c> notification: the token the server created, and its value, which for work done is a
/// begin, a report or an end.</summary>
public sealed record ProgressParams(JsonElement Token, JsonElement Value);

/// <summary>Work the server reports on, as a status bar shows it: the token it goes by, whether this is its end, its
/// title (from its begin), the latest message and how far it is, in percent, when the server says.</summary>
public sealed record WorkProgress(string Token, bool Ended, string? Title, string? Message, int? Percentage)
{
    /// <summary>Work done progress from a <c>$/progress</c> notification; null for any other kind (partial results).</summary>
    public static WorkProgress? From(ProgressParams progress)
    {
        if (progress.Value.ValueKind != JsonValueKind.Object || !progress.Value.TryGetProperty("kind", out JsonElement kind)
            || kind.GetString() is not ("begin" or "report" or "end") || progress.Token.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
        {
            return null;
        }
        return new WorkProgress(
            progress.Token.ToString(),
            kind.GetString() == "end",
            Text(progress.Value, "title"),
            Text(progress.Value, "message"),
            progress.Value.TryGetProperty("percentage", out JsonElement percentage) && percentage.TryGetInt32(out int percent) ? percent : null);
    }

    private static string? Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
}

public sealed record ConfigurationItem(string? ScopeUri, string? Section);

public sealed record ConfigurationParams(IReadOnlyList<ConfigurationItem> Items);

public sealed record InlayHintParams(TextDocumentIdentifier TextDocument, LspRange Range);

/// <summary>A word the server would show inside a line, before the position it names: a parameter's name before its
/// argument (kind 2), a type (kind 1); padding asks for a space on that side. <see cref="Data"/> is what the server
/// knows it by, for its tooltip (<c>inlayHint/resolve</c>).</summary>
public sealed record LspInlayHint(LspPosition Position, string Label, int? Kind, bool PaddingLeft, bool PaddingRight, JsonElement? Data = null);

public sealed record Registration(string Id, string Method, JsonElement? RegisterOptions);

public sealed record RegistrationParams(IReadOnlyList<Registration> Registrations);

/// <summary>The serializer settings every message goes through: camelCase names, nulls left out.</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
