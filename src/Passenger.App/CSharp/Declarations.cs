using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Passenger.Languages.CSharp;

namespace Passenger.App.CSharp;

/// <summary>What an open file's own syntax says of the declarations around a place: the override keyword Base Symbols
/// asks the language server about, and the types and member a place is in, which Recent Locations names.</summary>
internal static class Declarations
{
    /// <summary>Where the override keyword is of the member whose name in its declaration is at an offset, if it has one:
    /// the server's definition there is the member overridden, a library's included.</summary>
    public static int? OverrideKeywordAt(ParsedText syntax, int offset)
    {
        if (offset > syntax.Text.Length)
        {
            return null;
        }
        SyntaxToken token = syntax.Root.FindToken(offset);
        if (!token.IsKind(SyntaxKind.IdentifierToken) && offset > 0)
        {
            token = syntax.Root.FindToken(offset - 1);
        }
        SyntaxTokenList? modifiers = token.Parent switch
        {
            MethodDeclarationSyntax method when method.Identifier == token => method.Modifiers,
            PropertyDeclarationSyntax property when property.Identifier == token => property.Modifiers,
            EventDeclarationSyntax declared when declared.Identifier == token => declared.Modifiers,
            VariableDeclaratorSyntax { Parent.Parent: EventFieldDeclarationSyntax field } variable when variable.Identifier == token => field.Modifiers,
            _ => null,
        };
        SyntaxToken keyword = modifiers?.FirstOrDefault(m => m.IsKind(SyntaxKind.OverrideKeyword)) ?? default;
        return keyword.IsKind(SyntaxKind.OverrideKeyword) ? keyword.SpanStart : null;
    }

    /// <summary>The types and the member a place is in, outermost first ("Shop › Add(int)"), as the status bar's path names
    /// them; null outside any type.</summary>
    public static string? PathAt(ParsedText syntax, int offset)
    {
        if (offset > syntax.Text.Length)
        {
            return null;
        }
        List<string> parts = [];
        foreach (SyntaxNode node in syntax.Root.FindToken(offset).Parent?.AncestorsAndSelf() ?? [])
        {
            if (Name(node) is { } name)
            {
                parts.Insert(0, name);
            }
        }
        return parts.Count > 0 ? string.Join(" › ", parts) : null;
    }

    private static string? Name(SyntaxNode node) => node switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier.Text,
        MethodDeclarationSyntax method => $"{method.Identifier.Text}({Parameters(method.ParameterList)})",
        ConstructorDeclarationSyntax constructor => $"{constructor.Identifier.Text}({Parameters(constructor.ParameterList)})",
        PropertyDeclarationSyntax property => property.Identifier.Text,
        IndexerDeclarationSyntax => "this[]",
        EventDeclarationSyntax declared => declared.Identifier.Text,
        _ => null,
    };

    private static string Parameters(ParameterListSyntax list) => string.Join(", ", list.Parameters.Select(p => p.Type?.ToString() ?? p.Identifier.Text));
}
