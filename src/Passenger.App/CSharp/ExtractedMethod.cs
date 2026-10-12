using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Passenger.Text;

namespace Passenger.App.CSharp;

/// <summary>The selection Extract Method was asked for, followed through every change to its document from then, the
/// action's own edit included, which widens it to all it replaced: the method just made is the one the call that took
/// the selection's place names and the text did not declare before, whatever name the server gave it.</summary>
internal sealed class ExtractedMethod : IDisposable
{
    private readonly TextSnapshot before;
    private int start;
    private int end;

    public ExtractedMethod(TextDocument document, Selection selection)
    {
        Document = document;
        before = document.Snapshot;
        start = selection.Start;
        end = selection.End;
        document.Changed += OnChanged;
    }

    public TextDocument Document { get; }

    /// <summary>Where the new method's name starts in that call, in the document's text now; null when no call there
    /// names a method the text did not declare before.</summary>
    public int? Call()
    {
        TextSpan made = TextSpan.FromBounds(start, end);
        HashSet<string> old = Declared(CSharpSyntaxTree.ParseText(before.ToString()).GetRoot());
        SyntaxNode root = CSharpSyntaxTree.ParseText(Document.Snapshot.ToString()).GetRoot();
        HashSet<string> declared = Declared(root);
        foreach (InvocationExpressionSyntax call in root.DescendantNodes(made).OfType<InvocationExpressionSyntax>())
        {
            SyntaxToken name = call.Expression switch
            {
                SimpleNameSyntax simple => simple.Identifier,
                MemberAccessExpressionSyntax access => access.Name.Identifier,
                _ => default,
            };
            if (made.Contains(name.Span) && declared.Contains(name.ValueText) && !old.Contains(name.ValueText))
            {
                return name.SpanStart;
            }
        }
        return null;
    }

    public void Dispose() => Document.Changed -= OnChanged;

    private static HashSet<string> Declared(SyntaxNode root) =>
    [
        .. root.DescendantNodes().Select(n => n switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            LocalFunctionStatementSyntax function => function.Identifier.ValueText,
            _ => null,
        }).OfType<string>(),
    ];

    private void OnChanged(object? sender, DocumentChangedEventArgs e)
    {
        int shift = 0;
        int grown = 0;
        int from = start;
        int to = end;
        foreach (TextEdit edit in e.Edits)
        {
            if (edit.Start + edit.Length < start)
            {
                shift += edit.Text.Length - edit.Length;
            }
            else if (edit.Start <= end)
            {
                // An edit touching the selection is part of what replaced it.
                from = Math.Min(from, edit.Start);
                to = Math.Max(to, edit.Start + edit.Length);
                grown += edit.Text.Length - edit.Length;
            }
        }
        start = from + shift;
        end = to + shift + grown;
    }
}
