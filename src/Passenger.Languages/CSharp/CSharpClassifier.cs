using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Passenger.Editor;

namespace Passenger.Languages.CSharp;

/// <summary>Colours C# from its syntax tree alone: keywords, literals, comments, directives, and
/// every name whose role the syntax settles (a declaration, an invoked method, a type in a type-only
/// position, a namespace). A name the syntax cannot place, like <c>a.b</c>, stays plain until the
/// language server's semantic colours say what it is.</summary>
internal static class CSharpClassifier
{
    private static readonly HashSet<SyntaxKind> ControlKeywords =
    [
        SyntaxKind.IfKeyword, SyntaxKind.ElseKeyword, SyntaxKind.WhileKeyword, SyntaxKind.ForKeyword,
        SyntaxKind.ForEachKeyword, SyntaxKind.DoKeyword, SyntaxKind.SwitchKeyword, SyntaxKind.CaseKeyword,
        SyntaxKind.DefaultKeyword, SyntaxKind.BreakKeyword, SyntaxKind.ContinueKeyword, SyntaxKind.ReturnKeyword,
        SyntaxKind.GotoKeyword, SyntaxKind.ThrowKeyword, SyntaxKind.TryKeyword, SyntaxKind.CatchKeyword,
        SyntaxKind.FinallyKeyword, SyntaxKind.YieldKeyword, SyntaxKind.AwaitKeyword, SyntaxKind.WhenKeyword,
    ];

    /// <summary>The spans of the tokens (and their trivia) that reach into <paramref name="range"/>,
    /// sorted. A comment or an excluded block that starts before the range is included whole. It
    /// finds the first token by position and walks token by token, never through a list of siblings,
    /// so a class with ten thousand members costs what the lines on screen cost.</summary>
    public static List<SyntaxSpan> Classify(SyntaxNode root, TextSpan range)
    {
        List<SyntaxSpan> spans = [];
        SyntaxToken token = root.FindToken(Math.Min(range.Start, Math.Max(0, root.FullSpan.End - 1)));
        while (token.RawKind != 0 && token.FullSpan.Start <= range.End)
        {
            AddTrivia(token.LeadingTrivia, spans);
            AddToken(token, spans);
            AddTrivia(token.TrailingTrivia, spans);
            token = token.GetNextToken(includeZeroWidth: false, includeSkipped: true);
        }
        return spans;
    }

    private static void AddTrivia(SyntaxTriviaList trivia, List<SyntaxSpan> spans)
    {
        foreach (SyntaxTrivia t in trivia)
        {
            TokenKind? kind = t.Kind() switch
            {
                SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia => TokenKind.Comment,
                SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia => TokenKind.DocComment,
                SyntaxKind.DisabledTextTrivia => TokenKind.Excluded,
                _ when t.IsDirective => TokenKind.Preprocessor,
                _ => null,
            };
            if (kind is { } k)
            {
                // A doc comment's first /// is its own leading trivia, outside its Span; FullSpan has it.
                Add(spans, t.FullSpan.Start, t.FullSpan.Length, k);
            }
        }
    }

    private static void AddToken(SyntaxToken token, List<SyntaxSpan> spans)
    {
        SyntaxKind kind = token.Kind();
        if (SyntaxFacts.IsKeywordKind(kind))
        {
            Add(spans, token.SpanStart, token.Span.Length, ControlKeywords.Contains(kind) ? TokenKind.ControlKeyword : TokenKind.Keyword);
            return;
        }
        switch (kind)
        {
            case SyntaxKind.NumericLiteralToken:
                Add(spans, token.SpanStart, token.Span.Length, TokenKind.Number);
                return;
            case SyntaxKind.StringLiteralToken or SyntaxKind.CharacterLiteralToken or SyntaxKind.InterpolatedStringTextToken:
                AddString(token, spans);
                return;
            case SyntaxKind.Utf8StringLiteralToken or SyntaxKind.SingleLineRawStringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken
                or SyntaxKind.Utf8SingleLineRawStringLiteralToken or SyntaxKind.Utf8MultiLineRawStringLiteralToken
                or SyntaxKind.InterpolatedStringStartToken or SyntaxKind.InterpolatedStringEndToken
                or SyntaxKind.InterpolatedVerbatimStringStartToken or SyntaxKind.InterpolatedSingleLineRawStringStartToken
                or SyntaxKind.InterpolatedMultiLineRawStringStartToken or SyntaxKind.InterpolatedRawStringEndToken:
                Add(spans, token.SpanStart, token.Span.Length, TokenKind.String);
                return;
            case SyntaxKind.IdentifierToken:
                if (Identifier(token) is { } role)
                {
                    Add(spans, token.SpanStart, token.Span.Length, role);
                }
                return;
        }
    }

    /// <summary>A string with its escapes picked out, unless it is verbatim, where a backslash is
    /// just a backslash.</summary>
    private static void AddString(SyntaxToken token, List<SyntaxSpan> spans)
    {
        string text = token.Text;
        int start = token.SpanStart;
        bool verbatim = text.StartsWith('@') || (token.IsKind(SyntaxKind.InterpolatedStringTextToken) && IsVerbatimInterpolation(token));
        if (verbatim || !text.Contains('\\'))
        {
            Add(spans, start, text.Length, TokenKind.String);
            return;
        }
        int run = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length)
            {
                continue;
            }
            int length = EscapeLength(text, i);
            Add(spans, start + run, i - run, TokenKind.String);
            Add(spans, start + i, length, TokenKind.StringEscape);
            run = i + length;
            i += length - 1;
        }
        Add(spans, start + run, text.Length - run, TokenKind.String);
    }

    private static bool IsVerbatimInterpolation(SyntaxToken token) =>
        token.Parent?.Parent is InterpolatedStringExpressionSyntax outer && outer.StringStartToken.Text.Contains('@');

    private static int EscapeLength(string text, int at)
    {
        char next = text[at + 1];
        int digits = next switch
        {
            'x' => CountHex(text, at + 2, 4),
            'u' => 4,
            'U' => 8,
            _ => 0,
        };
        return Math.Min(2 + digits, text.Length - at);
    }

    private static int CountHex(string text, int from, int max)
    {
        int count = 0;
        while (count < max && from + count < text.Length && Uri.IsHexDigit(text[from + count]))
        {
            count++;
        }
        return count;
    }

    private static TokenKind? Identifier(SyntaxToken token) => token.Parent switch
    {
        ClassDeclarationSyntax => TokenKind.Class,
        RecordDeclarationSyntax record => record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? TokenKind.Struct : TokenKind.Class,
        StructDeclarationSyntax => TokenKind.Struct,
        InterfaceDeclarationSyntax => TokenKind.Interface,
        EnumDeclarationSyntax => TokenKind.Enum,
        DelegateDeclarationSyntax => TokenKind.Delegate,
        TypeParameterSyntax => TokenKind.TypeParameter,
        MethodDeclarationSyntax or LocalFunctionStatementSyntax => TokenKind.Method,
        ConstructorDeclarationSyntax or DestructorDeclarationSyntax => TokenKind.Class,
        PropertyDeclarationSyntax => TokenKind.Property,
        EventDeclarationSyntax => TokenKind.Event,
        EnumMemberDeclarationSyntax => TokenKind.EnumMember,
        ParameterSyntax => TokenKind.Parameter,
        VariableDeclaratorSyntax declarator => declarator.Parent?.Parent switch
        {
            FieldDeclarationSyntax field => field.Modifiers.Any(SyntaxKind.ConstKeyword) ? TokenKind.Constant : TokenKind.Field,
            EventFieldDeclarationSyntax => TokenKind.Event,
            LocalDeclarationStatementSyntax local => local.IsConst ? TokenKind.Constant : TokenKind.Local,
            _ => TokenKind.Local,
        },
        SingleVariableDesignationSyntax or ForEachStatementSyntax or CatchDeclarationSyntax => TokenKind.Local,
        LabeledStatementSyntax => TokenKind.Label,
        SimpleNameSyntax name => Name(name),
        _ => null,
    };

    /// <summary>A name used rather than declared, placed by where it stands.</summary>
    private static TokenKind? Name(SimpleNameSyntax name)
    {
        if (name is IdentifierNameSyntax { Identifier.Text: "var" or "dynamic" or "unmanaged" or "notnull" } contextual
            && SyntaxFacts.IsInTypeOnlyContext(contextual))
        {
            return TokenKind.Keyword;
        }
        if (name is IdentifierNameSyntax { Identifier.Text: "nameof", Parent: InvocationExpressionSyntax })
        {
            return TokenKind.Keyword;
        }
        if (IsNamespace(name))
        {
            return TokenKind.Namespace;
        }
        if (IsInvoked(name))
        {
            return TokenKind.Method;
        }
        if (name.Parent is AttributeSyntax || (name.Parent is QualifiedNameSyntax { Parent: AttributeSyntax } q && q.Right == name))
        {
            return TokenKind.Class;
        }
        ExpressionSyntax outermost = name;
        while (outermost.Parent is QualifiedNameSyntax qualified && qualified.Right == outermost)
        {
            outermost = qualified;
        }
        bool rightmost = name.Parent is not QualifiedNameSyntax left || left.Right == name;
        return rightmost && SyntaxFacts.IsInTypeOnlyContext(outermost) ? TokenKind.Class : null;
    }

    private static bool IsInvoked(SimpleNameSyntax name) => name.Parent switch
    {
        InvocationExpressionSyntax call => call.Expression == name,
        MemberAccessExpressionSyntax member => member.Name == name && member.Parent is InvocationExpressionSyntax call && call.Expression == member,
        MemberBindingExpressionSyntax binding => binding.Parent is InvocationExpressionSyntax call && call.Expression == binding,
        _ => false,
    };

    /// <summary>A name inside a using directive's or a namespace declaration's dotted name.</summary>
    private static bool IsNamespace(SimpleNameSyntax name)
    {
        SyntaxNode node = name;
        while (node.Parent is QualifiedNameSyntax)
        {
            node = node.Parent;
        }
        return node.Parent switch
        {
            UsingDirectiveSyntax directive => directive.Alias is null && directive.StaticKeyword.IsKind(SyntaxKind.None) && directive.NamespaceOrType == node,
            BaseNamespaceDeclarationSyntax declaration => declaration.Name == node,
            _ => false,
        };
    }

    private static void Add(List<SyntaxSpan> spans, int start, int length, TokenKind kind)
    {
        if (length > 0)
        {
            spans.Add(new SyntaxSpan(start, length, kind));
        }
    }
}
