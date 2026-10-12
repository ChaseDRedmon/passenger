using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Passenger.Editor;

namespace Passenger.Languages.CSharp;

/// <summary>What folds in C#, as Rider folds it: the usings, a namespace's, a type's, a member's, an accessor's and a
/// multi-line lambda's braces, #region to #endregion, and doc comments and block comments over several lines. A braced
/// region starts at the end of the line before its brace when the brace stands alone, as in the usual layout, so
/// folded it reads <c>void Run() {...}</c>.</summary>
public static class FoldRegions
{
    public static IReadOnlyList<FoldRegion> Of(SyntaxNode root)
    {
        SourceText text = root.SyntaxTree.GetText();
        List<FoldRegion> found = [];
        Usings(text, root, found);
        foreach (SyntaxNode node in root.DescendantNodes())
        {
            switch (node)
            {
                case NamespaceDeclarationSyntax block:
                    Braced(text, block.OpenBraceToken, block.CloseBraceToken, found);
                    Usings(text, block, found);
                    break;
                case FileScopedNamespaceDeclarationSyntax space:
                    Usings(text, space, found);
                    break;
                case BaseTypeDeclarationSyntax type when !type.OpenBraceToken.IsMissing:
                    Braced(text, type.OpenBraceToken, type.CloseBraceToken, found);
                    break;
                case BaseMethodDeclarationSyntax { Body: { } body }:
                    Braced(text, body.OpenBraceToken, body.CloseBraceToken, found);
                    break;
                case LocalFunctionStatementSyntax { Body: { } body }:
                    Braced(text, body.OpenBraceToken, body.CloseBraceToken, found);
                    break;
                case BasePropertyDeclarationSyntax { AccessorList: { } accessors }:
                    Braced(text, accessors.OpenBraceToken, accessors.CloseBraceToken, found);
                    break;
                case AccessorDeclarationSyntax { Body: { } body }:
                    Braced(text, body.OpenBraceToken, body.CloseBraceToken, found);
                    break;
                case AnonymousFunctionExpressionSyntax { Body: BlockSyntax body }:
                    Braced(text, body.OpenBraceToken, body.CloseBraceToken, found);
                    break;
            }
        }
        Regions(text, root, found);
        Comments(text, root, found);
        return [.. found.OrderBy(r => r.Start)];
    }

    /// <summary>A pair of braces over several lines: from the end of the line before an opening brace on its own line,
    /// or the end of the brace's line, to the closing brace.</summary>
    private static void Braced(SourceText text, SyntaxToken open, SyntaxToken close, List<FoldRegion> found)
    {
        if (open.IsMissing || close.IsMissing)
        {
            return;
        }
        TextLine braceLine = text.Lines.GetLineFromPosition(open.SpanStart);
        bool alone = text.ToString(TextSpan.FromBounds(braceLine.Start, open.SpanStart)).Trim().Length == 0 && braceLine.LineNumber > 0;
        TextLine header = alone ? text.Lines[braceLine.LineNumber - 1] : braceLine;
        if (text.Lines.GetLineFromPosition(close.SpanStart).LineNumber <= header.LineNumber)
        {
            return;
        }
        found.Add(new FoldRegion(header.End, close.Span.End, alone ? "{...}" : "...}"));
    }

    /// <summary>Two or more usings in a row, folded after the first.</summary>
    private static void Usings(SourceText text, SyntaxNode scope, List<FoldRegion> found)
    {
        SyntaxList<UsingDirectiveSyntax> usings = scope switch
        {
            CompilationUnitSyntax unit => unit.Usings,
            BaseNamespaceDeclarationSyntax space => space.Usings,
            _ => default,
        };
        if (usings.Count < 2)
        {
            return;
        }
        TextLine first = text.Lines.GetLineFromPosition(usings[0].SpanStart);
        if (text.Lines.GetLineFromPosition(usings[^1].Span.End).LineNumber > first.LineNumber)
        {
            found.Add(new FoldRegion(first.End, usings[^1].Span.End, "..."));
        }
    }

    /// <summary>Each #region to its #endregion, the region's name left showing.</summary>
    private static void Regions(SourceText text, SyntaxNode root, List<FoldRegion> found)
    {
        Stack<RegionDirectiveTriviaSyntax> open = [];
        foreach (DirectiveTriviaSyntax directive in root.DescendantNodes(descendIntoTrivia: true).OfType<DirectiveTriviaSyntax>())
        {
            if (directive is RegionDirectiveTriviaSyntax region)
            {
                open.Push(region);
            }
            else if (directive is EndRegionDirectiveTriviaSyntax end && open.Count > 0)
            {
                RegionDirectiveTriviaSyntax start = open.Pop();
                TextLine first = text.Lines.GetLineFromPosition(start.SpanStart);
                TextLine last = text.Lines.GetLineFromPosition(end.SpanStart);
                if (last.LineNumber > first.LineNumber)
                {
                    found.Add(new FoldRegion(first.End, last.End, "..."));
                }
            }
        }
    }

    /// <summary>Doc comments and block comments over several lines, folded after their first.</summary>
    private static void Comments(SourceText text, SyntaxNode root, List<FoldRegion> found)
    {
        foreach (SyntaxTrivia trivia in root.DescendantTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                && !trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                continue;
            }
            // A /// comment's trivia ends after its last line's break; its last line is the one before.
            int end = trivia.Span.End;
            while (end > trivia.SpanStart && text[end - 1] is '\n' or '\r')
            {
                end--;
            }
            TextLine first = text.Lines.GetLineFromPosition(trivia.SpanStart);
            TextLine last = text.Lines.GetLineFromPosition(end);
            if (last.LineNumber > first.LineNumber)
            {
                found.Add(new FoldRegion(first.End, end, "..."));
            }
        }
    }
}
