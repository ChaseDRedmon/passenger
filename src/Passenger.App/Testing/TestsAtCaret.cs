using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Passenger.Languages.CSharp;
using Passenger.Text;

namespace Passenger.App.Testing;

/// <summary>What Rider's Run Tests runs from the editor: the test method the caret is in, else every test of the
/// class it is in, found from the file's own syntax so it needs no build to know where it is; and the test methods
/// a file declares, for the gutter's marks. The syntax is the file's shared parse; lines are the editor's.</summary>
public static class TestsAtCaret
{
    private static readonly HashSet<string> TestAttributes = new(StringComparer.Ordinal)
    {
        "Fact", "Theory", "AvaloniaFact", "AvaloniaTheory", "Test", "TestCase", "TestCaseSource", "TestMethod", "DataTestMethod",
    };

    /// <summary>The type (its full name, nested types after a '+') and method the caret's line (from zero, the editor's
    /// line, which Roslyn may count as several) is in; the method is null in a type but outside its methods.</summary>
    public static (string Type, string? Method)? At(ParsedText syntax, int line)
    {
        TextSnapshot text = syntax.Text;
        if (line < 0 || line >= text.LineCount)
        {
            return null;
        }
        string content = text.GetLineText(line);
        SyntaxNode root = syntax.Root;
        SyntaxNode? node = root.FindToken(Math.Min(text.GetLineStart(line) + (content.Length - content.TrimStart().Length), root.FullSpan.End)).Parent;
        MethodDeclarationSyntax? method = node?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        TypeDeclarationSyntax? type = (method?.Parent as TypeDeclarationSyntax) ?? node?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        return type is null ? null : (FullName(type), method?.Identifier.ValueText);
    }

    /// <summary>Every method with a test attribute the file declares, with its type's full name and the editor's line
    /// (from zero) its name is on.</summary>
    public static IReadOnlyList<(string Type, string Method, int Line)> Declared(ParsedText syntax) =>
        [.. Types(syntax.Root).SelectMany(t => t.Members.OfType<MethodDeclarationSyntax>()).Where(IsTest)
            .Select(m => (FullName((TypeDeclarationSyntax)m.Parent!), m.Identifier.ValueText, syntax.Text.GetLineFromOffset(m.Identifier.SpanStart)))];

    /// <summary>Every type the file declares that has a test method, with the editor's line (from zero) its name is on.</summary>
    public static IReadOnlyList<(string Type, int Line)> DeclaredTypes(ParsedText syntax) =>
        [.. Types(syntax.Root).Where(t => t.Members.OfType<MethodDeclarationSyntax>().Any(IsTest))
            .Select(t => (FullName(t), syntax.Text.GetLineFromOffset(t.Identifier.SpanStart)))];

    /// <summary>Whether a discovered test belongs to a type and method (a theory's rows, NUnit's cases included).</summary>
    public static bool Matches(string fullyQualifiedName, string type, string? method)
    {
        string name = fullyQualifiedName.Split('(')[0];
        string normalized = name.Replace('+', '.');
        string owner = type.Replace('+', '.');
        return method is null ? normalized.StartsWith(owner + ".", StringComparison.Ordinal) : normalized == $"{owner}.{method}";
    }

    /// <summary>The types a file declares, nested ones too, found without going into any member's body.</summary>
    private static IEnumerable<TypeDeclarationSyntax> Types(SyntaxNode root) =>
        root.DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax).OfType<TypeDeclarationSyntax>();

    private static bool IsTest(MethodDeclarationSyntax method) => method.AttributeLists.SelectMany(l => l.Attributes).Any(IsTest);

    private static bool IsTest(AttributeSyntax attribute)
    {
        string name = attribute.Name switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            _ => attribute.Name.ToString(),
        };
        if (name.EndsWith("Attribute", StringComparison.Ordinal))
        {
            name = name[..^"Attribute".Length];
        }
        return TestAttributes.Contains(name);
    }

    private static string FullName(TypeDeclarationSyntax type)
    {
        List<string> names = [type.Identifier.ValueText];
        SyntaxNode? parent = type.Parent;
        string? space = null;
        while (parent is not null)
        {
            switch (parent)
            {
                case TypeDeclarationSyntax outer:
                    names.Insert(0, outer.Identifier.ValueText + "+");
                    break;
                case BaseNamespaceDeclarationSyntax ns:
                    space = space is null ? ns.Name.ToString() : $"{ns.Name}.{space}";
                    break;
            }
            parent = parent.Parent;
        }
        string typeName = string.Concat(names);
        return space is null ? typeName : $"{space}.{typeName}";
    }
}
