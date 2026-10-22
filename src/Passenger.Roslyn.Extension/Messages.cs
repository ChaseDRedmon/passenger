using System.Text.Json.Serialization;

namespace Passenger.Roslyn.Extension;

// What Passenger and its extension in Roslyn's server say to each other, compiled into both: each request is a message to
// a handler named by its type, about the document the caret is in, and each answer says what it found or why not. Names
// are given, since the server reads and writes them with System.Text.Json's defaults.

/// <summary>The handlers' names, as the server knows them: their types' full names.</summary>
public static class Handlers
{
    public const string DescribeSignature = "Passenger.Roslyn.Extension.DescribeSignature";
    public const string ChangeSignature = "Passenger.Roslyn.Extension.ChangeSignature";
    public const string DescribeInterface = "Passenger.Roslyn.Extension.DescribeInterface";
    public const string ExtractInterface = "Passenger.Roslyn.Extension.ExtractInterface";
    public const string DescribeType = "Passenger.Roslyn.Extension.DescribeType";
    public const string MoveTypeToFile = "Passenger.Roslyn.Extension.MoveTypeToFile";
    public const string MoveTypeToNamespace = "Passenger.Roslyn.Extension.MoveTypeToNamespace";
    public const string DescribeHierarchy = "Passenger.Roslyn.Extension.DescribeHierarchy";
    public const string PullMembersUp = "Passenger.Roslyn.Extension.PullMembersUp";
    public const string PushMembersDown = "Passenger.Roslyn.Extension.PushMembersDown";
    public const string DocumentTexts = "Passenger.Roslyn.Extension.DocumentTexts";
    public const string AspireProjectDeclaration = "Passenger.Roslyn.Extension.AspireProjectDeclaration";
    public const string AspireCodeHelp = "Passenger.Roslyn.Extension.AspireCodeHelp";
    public const string AspireReferenceFix = "Passenger.Roslyn.Extension.AspireReferenceFix";
    public const string AspireRunResources = "Passenger.Roslyn.Extension.AspireRunResources";

    public static readonly IReadOnlyList<string> All =
    [
        DescribeSignature, ChangeSignature, DescribeInterface, ExtractInterface, DescribeType, MoveTypeToFile, MoveTypeToNamespace,
        DescribeHierarchy, PullMembersUp, PushMembersDown, DocumentTexts, AspireProjectDeclaration, AspireCodeHelp, AspireReferenceFix, AspireRunResources,
    ];
}

/// <summary>Files the solution has, by their paths, whose texts as the server holds them are asked for: an edit to a file
/// not open is placed by the server's lines and columns, so it lands where meant only on the text the server read.</summary>
public sealed record TextsAsked([property: JsonPropertyName("paths")] IReadOnlyList<string> Paths);

/// <summary>Each file asked for, with its text's <see cref="TextHash"/> as the server holds it, or null when the server
/// has no document of that path.</summary>
public sealed record TextsHeld([property: JsonPropertyName("texts")] IReadOnlyList<TextHeld> Texts);

public sealed record TextHeld(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("hash")] string? Hash);

/// <summary>A text's fingerprint as both sides make it: SHA-256 of its characters with every line break as '\n', since a
/// server's line and column are the same whichever break a line ends in (and Passenger holds a CR file's as '\n').</summary>
public static class TextHash
{
    public static string Of(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.Unicode.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'))));
}

/// <summary>A place in a document as the server counts it: a line and a UTF-16 column, from zero.</summary>
public sealed record Place([property: JsonPropertyName("line")] int Line, [property: JsonPropertyName("character")] int Character);

/// <summary>Where the caret is: what a describing message asks about.</summary>
public sealed record Caret([property: JsonPropertyName("at")] Place At);

/// <summary>Where Go to Declaration goes in Aspire's code: an AddProject call's project file, or the AppHost's file and the
/// place (<paramref name="At"/>) its Add call names a resource a service reads; a null path when neither can be proven.</summary>
public sealed record AspireProjectTarget([property: JsonPropertyName("path")] string? Path, [property: JsonPropertyName("at")] Place? At = null);

/// <summary>Semantic Aspire help; a caret requests resource-name completion as well as diagnostics.</summary>
public sealed record AspireHelpRequest([property: JsonPropertyName("at")] Place? At, bool Explain = false);

/// <summary>The selected missing edge; its identity is checked again against the current semantic graph.</summary>
public sealed record AspireFixRequest(Place At, string Service, string Resource);

/// <summary>A proven absent AppHost reference, with its source range and proposed workspace change.</summary>
public sealed record AspireMissingReference(Place Start, Place End, string Service, string Resource, Refactored Fix);

/// <summary>Names valid at the caret's string argument and references missing from this service's AppHost.</summary>
public sealed record AspireHelp(Place? Start, Place? End, IReadOnlyList<string> Names, IReadOnlyList<AspireMissingReference> Missing, string? Evidence = null);

/// <summary>The AppHost whose proven project resources and source locations are read.</summary>
public sealed record AspireResourcesRequest(string AppHost);

/// <summary>A project resource and the source fingerprint that makes its gutter location current.</summary>
public sealed record AspireRunResource(string Name, string Project, string Source, Place At, string SourceHash);

/// <summary>Proven resources, or why the AppHost cannot be read yet.</summary>
public sealed record AspireRunResourcesResult(IReadOnlyList<AspireRunResource> Resources, string? Problem = null);

/// <summary>One edit to a file's text, between two places of it as it was.</summary>
public sealed record Edit(
    [property: JsonPropertyName("start")] Place Start,
    [property: JsonPropertyName("end")] Place End,
    [property: JsonPropertyName("text")] string Text);

/// <summary>What a refactoring does to one file: its edits, or, for a file it makes, its whole text as the one edit.</summary>
public sealed record FileChange(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("created")] bool Created,
    [property: JsonPropertyName("edits")] IReadOnlyList<Edit> Edits);

/// <summary>A refactoring's answer: the files it changes, or why it changes none.</summary>
public sealed record Refactored(
    [property: JsonPropertyName("problem")] string? Problem,
    [property: JsonPropertyName("changes")] IReadOnlyList<FileChange> Changes);

/// <summary>A parameter as a signature has it: its modifier (ref, out, in, params, this, or none), type, name and the
/// default value its declaration gives (none when empty).</summary>
public sealed record Parameter(
    [property: JsonPropertyName("modifier")] string Modifier,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("defaultValue")] string DefaultValue);

/// <summary>The method or constructor at the caret, as Change Signature shows it: what it is ("method", "constructor"),
/// its type's name, its modifiers ("public static"), its name, its return type (empty for a constructor), its parameters,
/// and whether it extends a type, whose first parameter stays first. A problem instead when there is none to change.</summary>
public sealed record Signature(
    [property: JsonPropertyName("problem")] string? Problem,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("container")] string Container,
    [property: JsonPropertyName("modifiers")] string Modifiers,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("returnType")] string ReturnType,
    [property: JsonPropertyName("parameters")] IReadOnlyList<Parameter> Parameters,
    [property: JsonPropertyName("isExtension")] bool IsExtension);

/// <summary>A parameter of the signature asked for: the one it was (its place in the old signature), or -1 for a new one
/// with the value every call passes for it.</summary>
public sealed record ParameterChange(
    [property: JsonPropertyName("from")] int From,
    [property: JsonPropertyName("modifier")] string Modifier,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("defaultValue")] string DefaultValue,
    [property: JsonPropertyName("callValue")] string CallValue);

/// <summary>Change Signature's request: the method at the caret given this name, return type and parameters.</summary>
public sealed record SignatureChange(
    [property: JsonPropertyName("at")] Place At,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("returnType")] string ReturnType,
    [property: JsonPropertyName("parameters")] IReadOnlyList<ParameterChange> Parameters);

/// <summary>A member a type could give an interface or a base type: its id (its documentation comment id), how it reads
/// ("Add(int, int): int"), and what it is (method, property, indexer, event, field).</summary>
public sealed record Member(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("kind")] string Kind);

/// <summary>The type at the caret as Extract Interface shows it: its name, the interface's suggested name and file, its
/// namespace, and the members it could give the interface. A problem instead when there is none.</summary>
public sealed record InterfaceSource(
    [property: JsonPropertyName("problem")] string? Problem,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("namespace")] string Namespace,
    [property: JsonPropertyName("members")] IReadOnlyList<Member> Members);

/// <summary>Extract Interface's request: an interface of this name in a new file of this name beside the type's, holding
/// the members chosen, which the type then implements.</summary>
public sealed record InterfaceExtraction(
    [property: JsonPropertyName("at")] Place At,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("members")] IReadOnlyList<string> Members);

/// <summary>The type at the caret as Move shows it: its name and kind, its namespace (empty for the global one), the
/// file it is in, the file it would move to, and how many types that file declares. A problem instead when there is none.</summary>
public sealed record TypeSource(
    [property: JsonPropertyName("problem")] string? Problem,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("namespace")] string Namespace,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("targetFileName")] string TargetFileName,
    [property: JsonPropertyName("typesInFile")] int TypesInFile);

/// <summary>Move to Another File's request: the type at the caret into a new file of this name beside its own.</summary>
public sealed record TypeToFile(
    [property: JsonPropertyName("at")] Place At,
    [property: JsonPropertyName("fileName")] string FileName);

/// <summary>Move to Namespace's request: the type at the caret into this namespace, its users following.</summary>
public sealed record TypeToNamespace(
    [property: JsonPropertyName("at")] Place At,
    [property: JsonPropertyName("namespace")] string Namespace);

/// <summary>A type's place in its hierarchy as Pull Members Up and Push Members Down show it: its name, its members that
/// could move, the base types in the solution they could go up to, and its derived types they could go down to.</summary>
public sealed record Hierarchy(
    [property: JsonPropertyName("problem")] string? Problem,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("members")] IReadOnlyList<Member> Members,
    [property: JsonPropertyName("bases")] IReadOnlyList<Member> Bases,
    [property: JsonPropertyName("derived")] IReadOnlyList<Member> Derived);

/// <summary>Pull Members Up's or Push Members Down's request: these members of the class at the caret moved into the base
/// type named by its id (up), or into each derived class whose id is a line of the target (down); those listed as abstract
/// made abstract where they stay.</summary>
public sealed record MembersMove(
    [property: JsonPropertyName("at")] Place At,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("members")] IReadOnlyList<string> Members,
    [property: JsonPropertyName("abstract")] IReadOnlyList<string> Abstract);
