using System.Text.Json;

namespace Passenger.Lsp;

/// <summary>What Passenger tells a server it can do. Kept as the protocol's own JSON so it reads like
/// the specification: pull diagnostics registered per source, semantic tokens over ranges and whole
/// files, completion with resolve, code actions with resolve, rename with prepare, and UTF-16
/// positions, which are what the editor's columns already are.</summary>
public static class ClientCapabilities
{
    public static readonly JsonElement Value = JsonDocument.Parse("""
    {
      "general": { "positionEncodings": ["utf-16"] },
      "window": { "workDoneProgress": true },
      "workspace": {
        "applyEdit": true,
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
          "completionItem": {
            "snippetSupport": false,
            "commitCharactersSupport": true,
            "documentationFormat": ["markdown", "plaintext"],
            "labelDetailsSupport": true,
            "resolveSupport": { "properties": ["documentation", "detail", "additionalTextEdits"] }
          },
          "contextSupport": true
        },
        "hover": { "contentFormat": ["markdown", "plaintext"] },
        "signatureHelp": {
          "signatureInformation": {
            "documentationFormat": ["markdown", "plaintext"],
            "parameterInformation": { "labelOffsetSupport": true },
            "activeParameterSupport": true
          },
          "contextSupport": true
        },
        "definition": { "linkSupport": true },
        "typeDefinition": { "linkSupport": true },
        "implementation": { "linkSupport": true },
        "references": {},
        "documentHighlight": {},
        "documentSymbol": { "hierarchicalDocumentSymbolSupport": true },
        "codeAction": {
          "codeActionLiteralSupport": {
            "codeActionKind": { "valueSet": ["", "quickfix", "refactor", "refactor.extract", "refactor.inline", "refactor.rewrite", "source", "source.organizeImports"] }
          },
          "isPreferredSupport": true,
          "dataSupport": true,
          "resolveSupport": { "properties": ["edit"] }
        },
        "formatting": {},
        "rangeFormatting": {},
        "onTypeFormatting": {},
        "rename": { "prepareSupport": true },
        "semanticTokens": {
          "dynamicRegistration": false,
          "requests": { "range": true, "full": { "delta": false } },
          "tokenTypes": [],
          "tokenModifiers": [],
          "formats": ["relative"],
          "multilineTokenSupport": false,
          "overlappingTokenSupport": false
        },
        "inlayHint": {},
        "diagnostic": { "dynamicRegistration": true, "relatedDocumentSupport": false },
        "publishDiagnostics": { "relatedInformation": false, "tagSupport": { "valueSet": [1, 2] } }
      }
    }
    """).RootElement.Clone();
}
