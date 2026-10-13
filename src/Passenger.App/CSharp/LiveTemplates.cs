using Passenger.Languages.CSharp;

namespace Passenger.App.CSharp;

/// <summary>A live template: its abbreviation, what it says it is, where it may go, its text in Rider's syntax and its
/// variables' defaults (a default naming another of Rider's macros is worked out where it is inserted). As Rider's, one
/// template may be typed by its abbreviation, offered by Surround With, or both.</summary>
public sealed record LiveTemplate(string Abbreviation, string Description, TemplateScope Scope, string Text, IReadOnlyDictionary<string, string> Defaults)
{
    /// <summary>Whether its abbreviation and Tab, or completion, expand it.</summary>
    public bool Live { get; init; } = true;

    /// <summary>Where Surround With offers it around the code chosen, none when it does not.</summary>
    public SurroundScope Surround { get; init; }

    /// <summary>The digit that chooses it in Surround With's list, as Rider marks its everyday ones.</summary>
    public char? Mnemonic { get; init; }
}

/// <summary>Rider's C# live templates, the ones used every day: the abbreviation then Tab, or picked from completion; and
/// its surround templates, offered by Surround With with their text from Rider's own.</summary>
public static class LiveTemplates
{
    /// <summary>The default that takes the name of the type the template goes in.</summary>
    public const string TypeName = "typeName()";

    private static Dictionary<string, string> D(params (string Name, string Value)[] values) => values.ToDictionary(v => v.Name, v => v.Value);

    private const SurroundScope Statements = SurroundScope.Statements;

    public static IReadOnlyList<LiveTemplate> All { get; } =
    [
        new("for", "Simple \"for\" loop", TemplateScope.Statement, "for (int $INDEX$ = 0; $INDEX$ < $UPPER$; $INDEX$++)\n{\n\t$SELECTION$$END$\n}", D(("INDEX", "i"), ("UPPER", "length")))
            { Surround = Statements, Mnemonic = '3' },
        new("forr", "Reverse \"for\" loop", TemplateScope.Statement, "for (int $INDEX$ = $MAX$ - 1; $INDEX$ >= 0; $INDEX$--)\n{\n\t$END$\n}", D(("INDEX", "i"), ("MAX", "length"))),
        new("foreach", "foreach block", TemplateScope.Statement, "foreach (var $VARIABLE$ in $COLLECTION$)\n{\n\t$SELECTION$$END$\n}", D(("VARIABLE", "item"), ("COLLECTION", "collection")))
            { Surround = Statements },
        new("if", "if statement", TemplateScope.Statement, "if ($CONDITION$)\n{\n\t$SELECTION$$END$\n}", D(("CONDITION", "true"))) { Surround = Statements, Mnemonic = '1' },
        new("else", "else statement", TemplateScope.Statement, "else\n{\n\t$END$\n}", D()),
        new("while", "while loop", TemplateScope.Statement, "while ($CONDITION$)\n{\n\t$SELECTION$$END$\n}", D(("CONDITION", "true"))) { Surround = Statements, Mnemonic = '2' },
        new("do", "do...while loop", TemplateScope.Statement, "do\n{\n\t$SELECTION$$END$\n} while ($CONDITION$);", D(("CONDITION", "true"))) { Surround = Statements, Mnemonic = '4' },
        new("switch", "switch statement", TemplateScope.Statement, "switch ($EXPRESSION$)\n{\n\t$END$\n}", D(("EXPRESSION", "expression"))),
        new("try", "try catch", TemplateScope.Statement,
            "try\n{\n\t$SELECTION$\n}\ncatch ($EXCEPTION$ $EX_NAME$)\n{\n\t$SELSTART$Console.WriteLine($EX_NAME$);\n\tthrow;$SELEND$\n}",
            D(("EXCEPTION", "Exception"), ("EX_NAME", "e"))) { Surround = Statements, Mnemonic = '8' },
        new("tryf", "try finally", TemplateScope.Statement, "try\n{\n\t$SELECTION$\n}\nfinally\n{\n\t$END$\n}", D()) { Surround = Statements, Mnemonic = '9' },
        new("using", "using statement", TemplateScope.Statement, "using ($RESOURCE$)\n{\n\t$SELECTION$$END$\n}", D(("RESOURCE", "resource"))) { Surround = Statements },
        new("lock", "lock statement", TemplateScope.Statement, "lock ($EXPRESSION$)\n{\n\t$SELECTION$$END$\n}", D(("EXPRESSION", "this"))) { Surround = Statements, Mnemonic = '5' },
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

        // Rider's surround templates that are not typed here by an abbreviation.
        new("cast", "cast", TemplateScope.None, "(($TYPE$)$SELECTION$)", D()) { Live = false, Surround = SurroundScope.Expression, Mnemonic = '0' },
        new("()", "parentheses", TemplateScope.None, "($SELECTION$$END$)", D()) { Live = false, Surround = SurroundScope.Everywhere, Mnemonic = '6' },
        new("{}", "braces", TemplateScope.None, "{\n\t$SELECTION$$END$\n}", D()) { Live = false, Surround = SurroundScope.Everywhere, Mnemonic = '7' },
        new("#if", "#if directive", TemplateScope.None, "#if $EXPRESSION$\n$SELECTION$$END$\n#endif", D(("EXPRESSION", "true")))
            { Live = false, Surround = SurroundScope.Lines },
        new("#region", "#region directive", TemplateScope.None, "#region $NAME$\n$SELECTION$$END$\n#endregion", D(("NAME", "MyRegion")))
            { Live = false, Surround = SurroundScope.Lines },
        new("namespace", "namespace declaration", TemplateScope.None, "namespace $NAME$\n{\n\t$END$$SELECTION$\n}", D(("NAME", "MyNamespace")))
            { Live = false, Surround = SurroundScope.Types },
    ];

    /// <summary>The templates that may go where a scope allows.</summary>
    public static IEnumerable<LiveTemplate> In(TemplateScope scope) => All.Where(t => t.Live && (t.Scope & scope) != 0);

    /// <summary>What Surround With offers around code standing in these scopes, in Rider's order: those with a digit by
    /// it (1 to 9, then 0), then the rest by name.</summary>
    public static IReadOnlyList<LiveTemplate> Around(SurroundScope scope) =>
        [.. All.Where(t => (t.Surround & scope) != 0)
            .OrderBy(t => t.Mnemonic is { } digit ? (digit == '0' ? 10 : digit - '0') : 11)
            .ThenBy(t => t.Abbreviation, StringComparer.Ordinal)];
}
