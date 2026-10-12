using Passenger.Languages.CSharp;

namespace Passenger.App.CSharp;

/// <summary>A live template: its abbreviation, what it says it is, where it may go, its text in Rider's syntax and its
/// variables' defaults (a default naming another of Rider's macros is worked out where it is inserted).</summary>
public sealed record LiveTemplate(string Abbreviation, string Description, TemplateScope Scope, string Text, IReadOnlyDictionary<string, string> Defaults);

/// <summary>Rider's C# live templates, the ones used every day: the abbreviation then Tab, or picked from completion.</summary>
public static class LiveTemplates
{
    /// <summary>The default that takes the name of the type the template goes in.</summary>
    public const string TypeName = "typeName()";

    private static Dictionary<string, string> D(params (string Name, string Value)[] values) => values.ToDictionary(v => v.Name, v => v.Value);

    public static IReadOnlyList<LiveTemplate> All { get; } =
    [
        new("for", "Simple \"for\" loop", TemplateScope.Statement, "for (int $INDEX$ = 0; $INDEX$ < $UPPER$; $INDEX$++)\n{\n\t$END$\n}", D(("INDEX", "i"), ("UPPER", "length"))),
        new("forr", "Reverse \"for\" loop", TemplateScope.Statement, "for (int $INDEX$ = $MAX$ - 1; $INDEX$ >= 0; $INDEX$--)\n{\n\t$END$\n}", D(("INDEX", "i"), ("MAX", "length"))),
        new("foreach", "foreach block", TemplateScope.Statement, "foreach (var $VARIABLE$ in $COLLECTION$)\n{\n\t$END$\n}", D(("VARIABLE", "item"), ("COLLECTION", "collection"))),
        new("if", "if statement", TemplateScope.Statement, "if ($CONDITION$)\n{\n\t$END$\n}", D(("CONDITION", "true"))),
        new("else", "else statement", TemplateScope.Statement, "else\n{\n\t$END$\n}", D()),
        new("while", "while loop", TemplateScope.Statement, "while ($CONDITION$)\n{\n\t$END$\n}", D(("CONDITION", "true"))),
        new("do", "do...while loop", TemplateScope.Statement, "do\n{\n\t$END$\n} while ($CONDITION$);", D(("CONDITION", "true"))),
        new("switch", "switch statement", TemplateScope.Statement, "switch ($EXPRESSION$)\n{\n\t$END$\n}", D(("EXPRESSION", "expression"))),
        new("try", "try catch", TemplateScope.Statement, "try\n{\n\t$SELECTION$$END$\n}\ncatch ($EXCEPTION$ e)\n{\n\tConsole.WriteLine(e);\n\tthrow;\n}", D(("EXCEPTION", "Exception"))),
        new("tryf", "try finally", TemplateScope.Statement, "try\n{\n\t$SELECTION$$END$\n}\nfinally\n{\n}", D()),
        new("using", "using statement", TemplateScope.Statement, "using ($RESOURCE$)\n{\n\t$SELECTION$$END$\n}", D(("RESOURCE", "resource"))),
        new("lock", "lock statement", TemplateScope.Statement, "lock ($EXPRESSION$)\n{\n\t$SELECTION$$END$\n}", D(("EXPRESSION", "this"))),
        new("cw", "Console.WriteLine", TemplateScope.Statement, "Console.WriteLine($END$);", D()),
        new("thr", "throw new", TemplateScope.Statement, "throw new $EXCEPTION$($END$);", D(("EXCEPTION", "InvalidOperationException"))),
        new("prop", "Property", TemplateScope.Member, "public $TYPE$ $NAME$ { get; set; }$END$", D(("TYPE", "int"), ("NAME", "Name"))),
        new("propg", "Property with a get accessor", TemplateScope.Member, "public $TYPE$ $NAME$ { get; }$END$", D(("TYPE", "int"), ("NAME", "Name"))),
        new("ctor", "Constructor", TemplateScope.Member, "public $TYPE$($END$)\n{\n}", D(("TYPE", TypeName))),
        new("svm", "static void Main", TemplateScope.Member, "static void Main(string[] args)\n{\n\t$END$\n}", D()),
        new("sim", "static int Main", TemplateScope.Member, "static int Main(string[] args)\n{\n\t$END$\n\treturn 0;\n}", D()),
        new("class", "Class", TemplateScope.Type, "public class $NAME$\n{\n\t$END$\n}", D(("NAME", "Name"))),
        new("record", "Record", TemplateScope.Type, "public record $NAME$($END$);", D(("NAME", "Name"))),
        new("interface", "Interface", TemplateScope.Type, "public interface $NAME$\n{\n\t$END$\n}", D(("NAME", "IName"))),
        new("struct", "Struct", TemplateScope.Type, "public struct $NAME$\n{\n\t$END$\n}", D(("NAME", "Name"))),
        new("enum", "Enum", TemplateScope.Type, "public enum $NAME$\n{\n\t$END$\n}", D(("NAME", "Name"))),
    ];

    /// <summary>The templates that may go where a scope allows.</summary>
    public static IEnumerable<LiveTemplate> In(TemplateScope scope) => All.Where(t => (t.Scope & scope) != 0);
}
