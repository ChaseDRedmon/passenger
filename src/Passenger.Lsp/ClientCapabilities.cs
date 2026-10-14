using System.Text.Json;

namespace Passenger.Lsp;

/// <summary>What Passenger tells a server it can do. Kept as the protocol's own JSON so it reads like
/// the specification: pull diagnostics registered per source, semantic tokens over ranges and whole
/// files, completion with resolve, code actions with resolve, rename with prepare, and UTF-16
/// positions, which are what the editor's columns already are. Not workspace/applyEdit: Roslyn never
/// asks it, leaving the edits its commands make to the client (<see cref="ClientCommands"/>). The features a dynamic
/// registration is taken for are those Razor registers for .razor and .cshtml files at startup, its colours' legend
/// among them; Roslyn's own C# ones are its static capabilities either way.</summary>
public static class ClientCapabilities
{
    public static readonly JsonElement Value = JsonDocument.Parse("""
    {
      "general": { "positionEncodings": ["utf-16"] },
      "window": { "workDoneProgress": true },
      "workspace": {
        "configuration": true,
        "workspaceFolders": true,
        "workspaceEdit": { "documentChanges": true, "resourceOperations": ["create", "rename", "delete"] },
        "semanticTokens": { "refreshSupport": true },
        "diagnostics": { "refreshSupport": true },
        "inlayHint": { "refreshSupport": true },
        "didChangeWatchedFiles": { "dynamicRegistration": false },
        "symbol": {}
      },
      "textDocument": {
        "synchronization": { "didSave": true, "dynamicRegistration": false },
        "completion": {
          "dynamicRegistration": true,
          "completionItem": {
            "snippetSupport": false,
            "commitCharactersSupport": true,
            "documentationFormat": ["markdown", "plaintext"],
            "labelDetailsSupport": true,
            "resolveSupport": { "properties": ["documentation", "detail", "additionalTextEdits"] }
          },
          "completionList": { "itemDefaults": ["commitCharacters"] },
          "contextSupport": true
        },
        "hover": { "dynamicRegistration": true, "contentFormat": ["markdown", "plaintext"] },
        "signatureHelp": {
          "dynamicRegistration": true,
          "signatureInformation": {
            "documentationFormat": ["markdown", "plaintext"],
            "parameterInformation": { "labelOffsetSupport": true },
            "activeParameterSupport": true
          },
          "contextSupport": true
        },
        "definition": { "dynamicRegistration": true, "linkSupport": true },
        "typeDefinition": { "linkSupport": true },
        "implementation": { "dynamicRegistration": true, "linkSupport": true },
        "references": { "dynamicRegistration": true },
        "callHierarchy": { "dynamicRegistration": true },
        "typeHierarchy": { "dynamicRegistration": true },
        "documentHighlight": { "dynamicRegistration": true },
        "documentSymbol": { "dynamicRegistration": true, "hierarchicalDocumentSymbolSupport": true },
        "codeAction": {
          "dynamicRegistration": true,
          "codeActionLiteralSupport": {
            "codeActionKind": { "valueSet": ["", "quickfix", "refactor", "refactor.extract", "refactor.inline", "refactor.rewrite", "source", "source.organizeImports"] }
          },
          "isPreferredSupport": true,
          "dataSupport": true,
          "resolveSupport": { "properties": ["edit"] }
        },
        "formatting": { "dynamicRegistration": true },
        "rangeFormatting": { "dynamicRegistration": true },
        "onTypeFormatting": { "dynamicRegistration": true },
        "rename": { "dynamicRegistration": true, "prepareSupport": true },
        "semanticTokens": {
          "dynamicRegistration": true,
          "requests": { "range": true, "full": { "delta": false } },
          "tokenTypes": [],
          "tokenModifiers": [],
          "formats": ["relative"],
          "multilineTokenSupport": false,
          "overlappingTokenSupport": false
        },
        "inlayHint": { "dynamicRegistration": true, "resolveSupport": { "properties": ["tooltip"] } },
        "diagnostic": { "dynamicRegistration": true, "relatedDocumentSupport": false },
        "publishDiagnostics": { "relatedInformation": false, "tagSupport": { "valueSet": [1, 2] } }
      }
    }
    """).RootElement.Clone();
}
