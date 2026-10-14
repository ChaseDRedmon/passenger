using Passenger.Text;
using Passenger.Xaml.Dialects;
using Passenger.Xaml.Metadata;
using Passenger.Xaml.Syntax;

namespace Passenger.Xaml.Features;

public enum XamlSeverity
{
    Error,
    Warning,
}

/// <summary>A problem in a XAML file, where it is and in the words Problems shows, and the fixes it offers.</summary>
public sealed record XamlDiagnostic(TextRange Range, XamlSeverity Severity, string Message)
{
    /// <summary>What puts it right (a theme variant's missing key added), each a quick fix.</summary>
    public IReadOnlyList<XamlFix> Fixes { get; init; } = [];
}

/// <summary>A quick fix: its title, the file it changes, and its edits made from that file's text as it is when the fix
/// is chosen.</summary>
public sealed record XamlFix(string Title, string Path, Func<string, IReadOnlyList<TextEdit>> Edits);

/// <summary>The problems Avalonia's XAML compiler would find, found as the file is typed: markup that is not
/// well-formed, an undeclared prefix, a type, property, event handler, markup extension or directive nothing resolves, a
/// value its property's type does not take (an enum's member, a boolean, a colour), a resource no scope defines, a
/// compiled binding's path its <c>x:DataType</c> does not have, a setter's property its style's target does not have,
/// and a name given twice. A project whose assemblies are not read yet (not restored) is checked for its markup only.</summary>
public static class XamlDiagnostics
{
    private static readonly HashSet<string> KnownDirectives = new(StringComparer.Ordinal)
    {
        "Class", "ClassModifier", "Name", "Key", "DataType", "CompileBindings", "FieldModifier", "Shared", "Arguments", "TypeArguments", "Uid",
        "Subclass", "Code",
    };

    public static IReadOnlyList<XamlDiagnostic> Of(XamlFile file)
    {
        List<XamlDiagnostic> found = [.. file.Tree.Problems.Select(p => new XamlDiagnostic(p.Range, XamlSeverity.Error, p.Message))];
        if (!file.Types.XmlnsDefinitions.Any(d => d.Uri == file.Dialect.PresentationUri))
        {
            return found;
        }
        found.AddRange(ThemeVariants.Of(file));
        Dictionary<string, XmlAttribute> named = new(StringComparer.Ordinal);
        foreach (XmlElement element in file.Tree.Elements)
        {
            if (element.Name.Length == 0 || element.Prefix == "xml")
            {
                continue;
            }
            if (element.Prefix.Length > 0 && file.UriOf(element, element.Prefix) is null)
            {
                found.Add(new XamlDiagnostic(TextRange.FromBounds(element.NameStart, element.NameStart + element.Prefix.Length), XamlSeverity.Error,
                    $"Namespace prefix '{element.Prefix}' is not declared"));
                continue;
            }
            if (element.Prefix.Length > 0 && (file.KindOf(file.UriOf(element, element.Prefix)) == XamlNamespaceKind.Ignorable || file.IsIgnored(element, element.Prefix)))
            {
                continue;
            }
            if (element.IsPropertyElement)
            {
                PropertyElement(file, element, found);
            }
            else if (file.TypeOf(element) is null)
            {
                found.Add(new XamlDiagnostic(element.NameRange, XamlSeverity.Error, $"Cannot resolve symbol '{element.Name}'"));
                continue;
            }
            foreach (XmlAttribute attribute in element.Attributes)
            {
                Attribute(file, attribute, found);
            }
            if (file.NameAttribute(element) is { Value.Length: > 0 } name && !InTemplate(element))
            {
                if (named.TryGetValue(name.Value, out XmlAttribute? first))
                {
                    found.Add(new XamlDiagnostic(name.ValueRange, XamlSeverity.Error, $"'{name.Value}' names another element already, on line {Line(file, first.ValueStart) + 1}"));
                }
                else
                {
                    named[name.Value] = name;
                }
            }
        }
        return found;
    }

    private static int Line(XamlFile file, int offset)
    {
        int line = 0;
        string text = file.Text;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }
        return line;
    }

    /// <summary>Whether an element is inside a template, whose names are a namescope of their own.</summary>
    private static bool InTemplate(XmlElement element) =>
        element.Ancestors().Any(a => !a.IsPropertyElement && a.LocalName.EndsWith("Template", StringComparison.Ordinal));

    private static void PropertyElement(XamlFile file, XmlElement element, List<XamlDiagnostic> found)
    {
        (ClrType? owner, XamlProperty? property) = file.PropertyElement(element);
        string local = element.LocalName;
        int dot = local.LastIndexOf('.');
        int ownerEnd = element.NameStart + element.Name.Length - (local.Length - dot);
        if (owner is null)
        {
            found.Add(new XamlDiagnostic(TextRange.FromBounds(element.NameStart, ownerEnd), XamlSeverity.Error, $"Cannot resolve symbol '{element.Name[..(element.Name.Length - (local.Length - dot))]}'"));
        }
        else if (property is null)
        {
            found.Add(new XamlDiagnostic(TextRange.FromBounds(ownerEnd + 1, element.NameEnd), XamlSeverity.Error, $"Cannot resolve property '{local[(dot + 1)..]}' on {owner.Name}"));
        }
    }

    private static void Attribute(XamlFile file, XmlAttribute attribute, List<XamlDiagnostic> found)
    {
        if (attribute.Prefix is "xml" || attribute.Name.Length == 0)
        {
            return;
        }
        XmlElement element = attribute.Owner;
        if (attribute.Prefix.Length > 0 && attribute.Prefix != "xmlns" && file.UriOf(element, attribute.Prefix) is null)
        {
            found.Add(new XamlDiagnostic(TextRange.FromBounds(attribute.NameStart, attribute.NameStart + attribute.Prefix.Length), XamlSeverity.Error,
                $"Namespace prefix '{attribute.Prefix}' is not declared"));
            return;
        }
        XamlAttributeMeaning meaning = file.MeaningOf(attribute);
        switch (meaning.Kind)
        {
            case XamlAttributeKind.Ignorable:
                return;
            case XamlAttributeKind.Namespace:
                Namespace(file, attribute, found);
                return;
            case XamlAttributeKind.Directive:
                Directive(file, attribute, found);
                return;
            case XamlAttributeKind.Event:
                Handler(file, attribute, found);
                return;
            case XamlAttributeKind.Unknown:
                if (file.ObjectOf(element) is { } type)
                {
                    int dot = attribute.LocalName.LastIndexOf('.');
                    found.Add(dot > 0 && meaning.Owner is null
                        ? new XamlDiagnostic(TextRange.FromBounds(attribute.NameStart, attribute.NameStart + attribute.Name.Length - (attribute.LocalName.Length - dot)),
                            XamlSeverity.Error, $"Cannot resolve symbol '{attribute.Name[..(attribute.Name.Length - (attribute.LocalName.Length - dot))]}'")
                        : new XamlDiagnostic(attribute.NameRange, XamlSeverity.Error,
                            $"Cannot resolve property '{(dot > 0 ? attribute.LocalName[(dot + 1)..] : attribute.LocalName)}' on {(meaning.Owner ?? type).Name}"));
                }
                return;
        }
        if (!attribute.HasValue)
        {
            return;
        }
        if (MarkupExtension.Parse(file.Text, attribute.ValueStart, attribute.ValueEnd) is { } extension)
        {
            Extension(file, element, extension, found);
            return;
        }
        if (meaning.Property is { } property)
        {
            Value(file, attribute, property, found);
        }
        Asset(file, attribute, found);
        if (element.LocalName == "Setter" && attribute.Name == "Property" && file.StyleTargetAt(element) is { } target
            && XamlCompletion.SetterProperty(file, element) is null)
        {
            found.Add(new XamlDiagnostic(attribute.ValueRange, XamlSeverity.Error, $"Cannot resolve property '{attribute.Value}' on {target.Name}"));
        }
        if (element.LocalName == "Style" && attribute.Name == "Selector")
        {
            Selector(file, attribute, found);
        }
    }

    /// <summary>An asset a value names that is not there: an avares:// path of this project or one it references, or a
    /// path from its folder or the file's, where an include, an image or an icon is read from (a font's family after
    /// its #).</summary>
    private static void Asset(XamlFile file, XmlAttribute attribute, List<XamlDiagnostic> found)
    {
        string value = attribute.Value.Trim();
        bool named = value.StartsWith("avares://", StringComparison.Ordinal)
            || (attribute.Name is "Source" or "Icon" && value.Length > 0 && !value.Contains("://", StringComparison.Ordinal));
        if (!named)
        {
            return;
        }
        int hash = value.IndexOf('#');
        string path = hash < 0 ? value : value[..hash];
        if (file.Project.ResolveSource(path, file.Path) is { } local && !File.Exists(local) && !Directory.Exists(local))
        {
            found.Add(new XamlDiagnostic(attribute.ValueRange, XamlSeverity.Warning, $"'{path}' is not found"));
        }
    }

    private static void Namespace(XamlFile file, XmlAttribute attribute, List<XamlDiagnostic> found)
    {
        string uri = attribute.Value;
        if (file.KindOf(uri) != XamlNamespaceKind.Clr)
        {
            return;
        }
        string clr = file.ClrNamespacesOf(uri).FirstOrDefault() ?? string.Empty;
        if (clr.Length > 0 && !file.Types.Namespaces.Contains(clr, StringComparer.Ordinal) && !file.Types.Namespaces.Any(n => n.StartsWith(clr + ".", StringComparison.Ordinal)))
        {
            found.Add(new XamlDiagnostic(attribute.ValueRange, XamlSeverity.Warning, $"Namespace '{clr}' holds no types"));
        }
    }

    private static void Directive(XamlFile file, XmlAttribute attribute, List<XamlDiagnostic> found)
    {
        XmlElement element = attribute.Owner;
        if (!KnownDirectives.Contains(attribute.LocalName))
        {
            found.Add(new XamlDiagnostic(attribute.NameRange, XamlSeverity.Error, $"Cannot resolve directive '{attribute.Name}'"));
            return;
        }
        switch (attribute.LocalName)
        {
            case "Class" when element.Parent is not null:
                found.Add(new XamlDiagnostic(attribute.NameRange, XamlSeverity.Error, "x:Class is only allowed on the root element"));
                break;
            case "Class" when attribute.Value.Length > 0 && file.Types.Find(attribute.Value) is null:
                found.Add(new XamlDiagnostic(attribute.ValueRange, XamlSeverity.Error, $"Cannot resolve class '{attribute.Value}'"));
                break;
            case "DataType" when attribute.Value.Length > 0 && file.TypeFromValue(element, attribute.Value) is null:
                found.Add(new XamlDiagnostic(attribute.ValueRange, XamlSeverity.Error, $"Cannot resolve symbol '{attribute.Value}'"));
                break;
            case "CompileBindings" or "Shared" when !bool.TryParse(attribute.Value, out _):
                found.Add(new XamlDiagnostic(attribute.ValueRange, XamlSeverity.Error, $"'{attribute.Value}' is not True or False"));
                break;
        }
    }

    /// <summary>An event's handler: a method of the code-behind's class, private ones included.</summary>
    private static void Handler(XamlFile file, XmlAttribute attribute, List<XamlDiagnostic> found)
    {
        if (file.ClassType is not { IsFromSource: true } owner || attribute.Value.Length == 0 || attribute.Value.StartsWith('{'))
        {
            return;
        }
        if (!file.Types.SelfAndBases(owner).Any(t => t.Members.Any(m => m.Kind == MemberKind.Method && m.Name == attribute.Value)))
        {
            found.Add(new XamlDiagnostic(attribute.ValueRange, XamlSeverity.Error, $"Cannot resolve method '{attribute.Value}' in {owner.Name}"));
        }
    }

    /// <summary>A value as its property's type reads it: an enum's members (flags joined by commas), a boolean, a colour.</summary>
    private static void Value(XamlFile file, XmlAttribute attribute, XamlProperty property, List<XamlDiagnostic> found)
    {
        TypeRef type = file.Dialect.ValueTypeOf(property.ValueType);
        string value = attribute.Value.Trim();
        if (value.Length == 0)
        {
            return;
        }
        if (type.Name == "System.Boolean")
        {
            if (!bool.TryParse(value, out _))
            {
                found.Add(new XamlDiagnostic(attribute.ValueRange, XamlSeverity.Error, $"'{value}' is not True or False"));
            }
            return;
        }
        if (type.Name is "Avalonia.Media.IBrush" or "Avalonia.Media.Brush" or "Avalonia.Media.ISolidColorBrush" or "Avalonia.Media.SolidColorBrush" or "Avalonia.Media.Color")
        {
            // rgb(), hsl() and hsv() are Avalonia's too, left to its own parser.
            if (KnownColours.Parse(value) is null && !value.Contains('(', StringComparison.Ordinal))
            {
                found.Add(new XamlDiagnostic(attribute.ValueRange, XamlSeverity.Error, $"'{value}' is not a colour"));
            }
            return;
        }
        if (file.Types.Resolve(type) is { Kind: ClrTypeKind.Enum } @enum)
        {
            HashSet<string> members = new(@enum.Members.Where(m => m.Kind == MemberKind.Field && m.IsStatic).Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
            foreach (string part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!members.Contains(part) && !int.TryParse(part, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    found.Add(new XamlDiagnostic(attribute.ValueRange, XamlSeverity.Error, $"'{part}' is not a value of {@enum.Name}"));
                    return;
                }
            }
        }
    }

    private static void Extension(XamlFile file, XmlElement element, MarkupExtension extension, List<XamlDiagnostic> found)
    {
        if (extension.Name.Length == 0)
        {
            return;
        }
        ClrType? type = file.ResolveType(element, extension.Name, extension: true);
        bool language = extension.Prefix.Length > 0 && file.KindOf(file.UriOf(element, extension.Prefix)) == XamlNamespaceKind.Language;
        if (type is null && !(language && extension.LocalName is "Static" or "Type" or "Null" or "True" or "False" or "Reference" or "Array"))
        {
            found.Add(new XamlDiagnostic(extension.NameRange, XamlSeverity.Error, $"Cannot resolve markup extension '{extension.Name}'"));
            return;
        }
        foreach (MarkupArgument argument in extension.Arguments)
        {
            if (argument.Nested is { } nested)
            {
                Extension(file, element, nested, found);
            }
        }
        switch (extension.LocalName)
        {
            case "StaticResource" or "DynamicResource" when extension.Argument(null) is { Nested: null, Value.Length: > 0 } key:
                Resource(file, key, dynamic: extension.LocalName == "DynamicResource", found);
                break;
            case "Binding" or "CompiledBinding" when !language:
                Binding(file, element, extension, found);
                break;
            case "Type" when language && extension.Argument(null) is { Nested: null, Value.Length: > 0 } named && file.ResolveType(element, named.Value) is null:
                found.Add(new XamlDiagnostic(named.ValueRange, XamlSeverity.Error, $"Cannot resolve symbol '{named.Value}'"));
                break;
            case "Static" when language && extension.Argument(null) is { Nested: null, Value.Length: > 0 } member:
                int dot = member.Value.LastIndexOf('.');
                ClrType? owner = dot > 0 ? file.ResolveType(element, member.Value[..dot]) : null;
                if (owner is null)
                {
                    found.Add(new XamlDiagnostic(member.ValueRange, XamlSeverity.Error, $"Cannot resolve symbol '{(dot > 0 ? member.Value[..dot] : member.Value)}'"));
                }
                else if (!file.Types.SelfAndBases(owner).Any(t => t.Members.Any(m => m.IsStatic && m.Name == member.Value[(dot + 1)..])))
                {
                    found.Add(new XamlDiagnostic(member.ValueRange, XamlSeverity.Error, $"Cannot resolve member '{member.Value[(dot + 1)..]}' of {owner.Name}"));
                }
                break;
        }
    }

    /// <summary>A resource key nothing the file reaches defines: an error for a static resource, which Avalonia looks up
    /// once and must find; a warning for a dynamic one, which may come later (a theme the app loads).</summary>
    private static void Resource(XamlFile file, MarkupArgument key, bool dynamic, List<XamlDiagnostic> found)
    {
        XamlProject project = file.Project;
        XamlFileSummary own = project.Files.TryGetValue(file.Path, out XamlFileSummary? summary) ? summary : XamlFileSummary.Of(file.Path, file.Tree, project.ResolveSource);
        if (project.Definitions(own, key.Value).Count > 0)
        {
            if (ThemeVariants.Use(file, own, key.ValueRange, key.Value) is { } missing)
            {
                found.Add(missing);
            }
            return;
        }
        if (project.ThemeKeys.Contains(key.Value) || ReferencedKey(project, key.Value))
        {
            return;
        }
        found.Add(new XamlDiagnostic(key.ValueRange, dynamic ? XamlSeverity.Warning : XamlSeverity.Error, $"Resource '{key.Value}' is not found"));
    }

    /// <summary>Whether any of the project's XAML files defines a key, where a file this one reaches only through code
    /// (a dictionary loaded by the app at run time) does.</summary>
    private static bool ReferencedKey(XamlProject project, string key) =>
        project.Files.Values.Any(f => f.Resources.Any(r => r.Key == key));

    /// <summary>A compiled binding's path, read against the data type in scope: each step a property of the one before.
    /// A path from another source (an element, a relative source, a converter's own) is the compiler's to judge.</summary>
    private static void Binding(XamlFile file, XmlElement element, MarkupExtension binding, List<XamlDiagnostic> found)
    {
        if (binding.LocalName == "Binding" && !CompilesBindings(file, element))
        {
            return;
        }
        if (binding.Arguments.Any(a => a.Key is "ElementName" or "RelativeSource" or "Source" or "DataType"))
        {
            return;
        }
        MarkupArgument? path = binding.Argument(null) ?? binding.Argument("Path");
        if (path is null || path.Nested is not null || path.Value.Length == 0 || file.DataTypeAt(element, stopAtTemplates: true) is not { } type)
        {
            return;
        }
        string text = path.Value.TrimStart('!');
        if (text.Length == 0 || text == "." || text.IndexOfAny(['$', '#', '^', '(', '[', ' ']) >= 0)
        {
            return;
        }
        int at = path.ValueStart + (path.Value.Length - text.Length);
        ClrType current = type;
        foreach (string step in text.Split('.'))
        {
            ClrMember? property = file.Types.MembersOf(current).FirstOrDefault(m => m.Kind is MemberKind.Property or MemberKind.Method && !m.IsStatic && m.Name == step);
            if (property is null)
            {
                if (!current.IsFromSource && current.Kind == ClrTypeKind.Interface)
                {
                    return;
                }
                found.Add(new XamlDiagnostic(new TextRange(at, step.Length), XamlSeverity.Error, $"Cannot resolve property '{step}' on {current.Name}"));
                return;
            }
            if (file.Types.Resolve(property.Type) is not { } next || next.FullName == "System.Object")
            {
                return;
            }
            current = next;
            at += step.Length + 1;
        }
    }

    /// <summary>Whether a <c>{Binding}</c> at an element compiles: the project's default (Avalonia's own, on since 11),
    /// unless the nearest <c>x:CompileBindings</c> says otherwise.</summary>
    private static bool CompilesBindings(XamlFile file, XmlElement element)
    {
        for (XmlElement? at = element; at is not null; at = at.Parent)
        {
            if (file.Directive(at, "CompileBindings") is { } set)
            {
                return !string.Equals(set.Value.Trim(), "False", StringComparison.OrdinalIgnoreCase);
            }
        }
        return file.Project.CompilesBindings;
    }

    private static void Selector(XamlFile file, XmlAttribute attribute, List<XamlDiagnostic> found)
    {
        // A selector written with character references (&gt;) is placed as a whole, its pieces' places being the read text's.
        bool exact = attribute.Value == attribute.RawValue;
        foreach (SelectorToken token in Selectors.Tokenize(attribute.Value))
        {
            if (token.Kind == SelectorPart.Type && token.Text.Length > 0 && file.ResolveType(attribute.Owner, token.Text.Replace('|', ':')) is null)
            {
                found.Add(new XamlDiagnostic(exact ? new TextRange(attribute.ValueStart + token.Start, token.End - token.Start) : attribute.ValueRange, XamlSeverity.Error,
                    $"Cannot resolve symbol '{token.Text}'"));
            }
        }
    }
}
