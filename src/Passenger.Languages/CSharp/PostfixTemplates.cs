using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Passenger.Text;

namespace Passenger.Languages.CSharp;

/// <summary>A postfix template: typed after an expression and a dot, it wraps the expression. A statement's one takes
/// an expression that is its whole statement; <c>$EXPR$</c> is the expression's text, the rest Rider's live template
/// syntax.</summary>
public sealed record PostfixTemplate(string Name, string Example, bool Statement, string Text, IReadOnlyDictionary<string, string> Defaults);

/// <summary>What a postfix template wraps: the whole expression ending at the dot (for a statement's template, which
/// needs it to be all of its statement) and the chain of member accesses and calls ending there (for an expression's,
/// so <c>a + b.not</c> negates <c>b</c>), each by where it starts, and the name .var suggests.</summary>
public readonly record struct PostfixTarget(int Start, string Text, bool WholeStatement, int ChainStart, string ChainText, string Name);

/// <summary>Rider's C# postfix templates, the everyday ones, and the expression before a dot they would wrap.</summary>
public static class PostfixTemplates
{
    private static readonly CSharpParseOptions Options = new(LanguageVersion.Preview, DocumentationMode.Parse);

    private static Dictionary<string, string> D(params (string Name, string Value)[] values) => values.ToDictionary(v => v.Name, v => v.Value);

    public static IReadOnlyList<PostfixTemplate> All { get; } =
    [
        new("if", "if (expr)", true, "if ($EXPR$)\n{\n\t$END$\n}", D()),
        new("null", "if (expr == null)", true, "if ($EXPR$ == null)\n{\n\t$END$\n}", D()),
        new("notnull", "if (expr != null)", true, "if ($EXPR$ != null)\n{\n\t$END$\n}", D()),
        new("var", "var x = expr;", true, "var $NAME$ = $EXPR$;$END$", D(("NAME", "$name"))),
        new("foreach", "foreach (var x in expr)", true, "foreach (var $VARIABLE$ in $EXPR$)\n{\n\t$END$\n}", D(("VARIABLE", "item"))),
        new("for", "for (var i = 0; i < expr; i++)", true, "for (var $INDEX$ = 0; $INDEX$ < $EXPR$; $INDEX$++)\n{\n\t$END$\n}", D(("INDEX", "i"))),
        new("forr", "for (var i = expr; i >= 0; i--)", true, "for (var $INDEX$ = $EXPR$ - 1; $INDEX$ >= 0; $INDEX$--)\n{\n\t$END$\n}", D(("INDEX", "i"))),
        new("while", "while (expr)", true, "while ($EXPR$)\n{\n\t$END$\n}", D()),
        new("return", "return expr;", true, "return $EXPR$;$END$", D()),
        new("throw", "throw expr;", true, "throw $EXPR$;$END$", D()),
        new("using", "using (expr)", true, "using ($EXPR$)\n{\n\t$END$\n}", D()),
        new("lock", "lock (expr)", true, "lock ($EXPR$)\n{\n\t$END$\n}", D()),
        new("switch", "switch (expr)", true, "switch ($EXPR$)\n{\n\t$END$\n}", D()),
        new("not", "!expr", false, "!$EXPR$$END$", D()),
        new("par", "(expr)", false, "($EXPR$)$END$", D()),
        new("await", "await expr", false, "await $EXPR$$END$", D()),
        new("arg", "Method(expr)", false, "$METHOD$($EXPR$)$END$", D(("METHOD", "Method"))),
    ];

    /// <summary>The expression a template typed after a dot would wrap: what ends at the dot, read from the text with
    /// the dot and the half-typed name taken out and the statement ended there. The whole chain before the dot
    /// (<c>a.b().c</c>); for a statement's template, the expression must be all of its statement.</summary>
    public static PostfixTarget? Before(TextSnapshot text, int dot, int caret)
    {
        if (dot <= 0 || dot >= text.Length || text[dot] != '.')
        {
            return null;
        }
        string source = text.GetText(0, dot) + ";" + text.GetText(caret, text.Length - caret);
        SyntaxNode root = CSharpSyntaxTree.ParseText(source, Options).GetRoot();
        SyntaxToken last = root.FindToken(dot - 1);
        if (last.Span.End != dot || last.Parent is null)
        {
            return null;
        }
        // Up from the last token: the chain while every step is a name, an access, a call and the like; the whole
        // expression as far as anything ends at the dot.
        ExpressionSyntax? outermost = null;
        ExpressionSyntax? chain = null;
        bool chaining = true;
        SyntaxNode? start = last.Parent;
        // A call's closing parenthesis belongs to its argument list, which is no expression; the call is one.
        while (start is not null and not ExpressionSyntax && start.Span.End == dot)
        {
            start = start.Parent;
        }
        for (SyntaxNode? node = start; node is ExpressionSyntax expression && node.Span.End == dot; node = node.Parent)
        {
            outermost = expression;
            chaining &= IsChain(expression);
            if (chaining || chain is null)
            {
                chain = expression;
            }
        }
        if (outermost is null || chain is null || outermost.IsMissing)
        {
            return null;
        }
        bool whole = outermost.Parent is ExpressionStatementSyntax statement && statement.Expression == outermost;
        return new PostfixTarget(outermost.SpanStart, source[outermost.SpanStart..dot], whole, chain.SpanStart, source[chain.SpanStart..dot], NameFor(outermost));
    }

    /// <summary>The kinds a chain before a dot is made of: names, accesses, calls, indexers, literals, parentheses.</summary>
    private static bool IsChain(ExpressionSyntax expression) => expression is SimpleNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax
        or ElementAccessExpressionSyntax or LiteralExpressionSyntax or ParenthesizedExpressionSyntax or ThisExpressionSyntax or BaseExpressionSyntax
        or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or PostfixUnaryExpressionSyntax or ConditionalAccessExpressionSyntax;

    /// <summary>The name .var suggests: an invocation's or a property's name without its Get, lower-cased at the
    /// start, a created type's name, else "value".</summary>
    private static string NameFor(ExpressionSyntax expression)
    {
        string? name = expression switch
        {
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } => access.Name.Identifier.ValueText,
            InvocationExpressionSyntax { Expression: IdentifierNameSyntax identifier } => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            ObjectCreationExpressionSyntax { Type: var type } => type.ToString().Split('<')[0].Split('.')[^1],
            _ => null,
        };
        if (string.IsNullOrEmpty(name))
        {
            return "value";
        }
        if (name.StartsWith("Get", StringComparison.Ordinal) && name.Length > 3 && char.IsUpper(name[3]))
        {
            name = name[3..];
        }
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
