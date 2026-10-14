using System.Collections.Concurrent;
using Passenger.Xaml.Dialects;
using Passenger.Xaml.Metadata;
using Passenger.Xaml.Syntax;

namespace Passenger.Xaml;

/// <summary>What an xmlns URI is to XAML.</summary>
public enum XamlNamespaceKind
{
    /// <summary>The framework's own (Avalonia's <c>https://github.com/avaloniaui</c>), or another its assemblies map.</summary>
    Mapped,

    /// <summary>XAML's language (<c>x:</c>).</summary>
    Language,

    /// <summary>The designer's or markup compatibility's, ignored at run time (<c>d:</c>, <c>mc:</c>).</summary>
    Ignorable,

    /// <summary>A CLR namespace named in the URI (<c>using:App.ViewModels</c>, <c>clr-namespace:App;assembly=App</c>).</summary>
    Clr,

    /// <summary>A URI nothing maps.</summary>
    Unknown,
}

/// <summary>What an attribute is to XAML.</summary>
public enum XamlAttributeKind
{
    /// <summary>An xmlns declaration.</summary>
    Namespace,

    /// <summary>One of XAML's own (<c>x:Name</c>, <c>x:Key</c>, <c>x:Class</c>, <c>x:DataType</c>).</summary>
    Directive,

    /// <summary>A property of the element's type.</summary>
    Property,

    /// <summary>An attached property of another type (<c>Grid.Row</c>).</summary>
    Attached,

    /// <summary>An event, its value a handler's name.</summary>
    Event,

    /// <summary>In an ignorable namespace (<c>d:DesignWidth</c>, <c>mc:Ignorable</c>).</summary>
    Ignorable,

    /// <summary>A style class an Avalonia element has while its value is true (<c>Classes.selected="{Binding}"</c>).</summary>
    StyleClass,

    /// <summary>Nothing the element's type or its namespaces have.</summary>
    Unknown,
}

/// <summary>What an attribute means: its kind, the property or event it sets, and for an attached property its owner.</summary>
public sealed record XamlAttributeMeaning(XamlAttributeKind Kind, XamlProperty? Property = null, ClrMember? Event = null, ClrType? Owner = null);

/// <summary>A XAML file's markup read against its project: which xmlns each prefix means where, which type each element
/// makes, what each attribute sets, the data type bindings resolve against and a style's target type. One per parse,
/// immutable but for answers it keeps as they are first asked, so any thread may ask it.</summary>
public sealed class XamlFile
{
    private readonly ConcurrentDictionary<XmlElement, ClrType?> types = new();

    public XamlFile(string path, XmlTree tree, XamlProject project)
    {
        Path = path;
        Tree = tree;
        Project = project;
    }

    public string Path { get; }

    public XmlTree Tree { get; }

    public XamlProject Project { get; }

    /// <summary>The theme variant (Dark, Light) whose resources a swatch shows where a theme has several.</summary>
    public string? Variant { get; init; }

    public TypeSystem Types => Project.Types;

    public XamlDialect Dialect => Project.Dialect;

    public string Text => Tree.Text;

    /// <summary>The type the root names in <c>x:Class</c>, its code-behind's.</summary>
    public string? ClassName => Tree.Root?.Attribute("x:Class")?.Value;

    public ClrType? ClassType => ClassName is { } name ? Types.Find(name) : null;

    /// <summary>The URI a prefix means at an element (the empty prefix, the default namespace).</summary>
    public string? UriOf(XmlElement? scope, string prefix)
    {
        string name = prefix.Length == 0 ? "xmlns" : "xmlns:" + prefix;
        for (XmlElement? at = scope; at is not null; at = at.Parent)
        {
            if (at.Attribute(name) is { } declared)
            {
                return declared.Value;
            }
        }
        return null;
    }

    /// <summary>Every prefix in scope at an element, the nearest declaration of each, the default one's as empty.</summary>
    public IReadOnlyList<(string Prefix, string Uri)> PrefixesAt(XmlElement? scope)
    {
        Dictionary<string, string> found = new(StringComparer.Ordinal);
        for (XmlElement? at = scope; at is not null; at = at.Parent)
        {
            foreach (XmlAttribute attribute in at.Attributes)
            {
                if (attribute.Name == "xmlns")
                {
                    found.TryAdd(string.Empty, attribute.Value);
                }
                else if (attribute.Prefix == "xmlns")
                {
                    found.TryAdd(attribute.LocalName, attribute.Value);
                }
            }
        }
        return [.. found.Select(f => (f.Key, f.Value))];
    }

    /// <summary>The prefix a URI is declared with at an element; null when it is not declared there.</summary>
    public string? PrefixOf(XmlElement? scope, string uri) =>
        PrefixesAt(scope).Where(p => p.Uri == uri).Select(p => p.Prefix).Cast<string?>().FirstOrDefault();

    public XamlNamespaceKind KindOf(string? uri)
    {
        if (uri is null)
        {
            return XamlNamespaceKind.Unknown;
        }
        if (uri == XamlDialect.XamlUri)
        {
            return XamlNamespaceKind.Language;
        }
        if (uri is XamlDialect.DesignerUri or XamlDialect.CompatibilityUri)
        {
            return XamlNamespaceKind.Ignorable;
        }
        if (uri.StartsWith("using:", StringComparison.Ordinal) || uri.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            return XamlNamespaceKind.Clr;
        }
        return uri == Dialect.PresentationUri || Types.XmlnsDefinitions.Any(d => d.Uri == uri) ? XamlNamespaceKind.Mapped : XamlNamespaceKind.Unknown;
    }

    /// <summary>The CLR namespaces a URI maps to.</summary>
    public IReadOnlyList<string> ClrNamespacesOf(string? uri)
    {
        if (uri is null)
        {
            return [];
        }
        if (uri.StartsWith("using:", StringComparison.Ordinal))
        {
            return [uri["using:".Length..].Trim()];
        }
        if (uri.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            string rest = uri["clr-namespace:".Length..];
            int semicolon = rest.IndexOf(';');
            return [(semicolon < 0 ? rest : rest[..semicolon]).Trim()];
        }
        return [.. Types.NamespacesOf(uri)];
    }

    /// <summary>The type a qualified name (<c>prefix:Name</c>) names at an element; for a markup extension, its
    /// <c>…Extension</c> type is tried first, as XAML does.</summary>
    public ClrType? ResolveType(XmlElement? scope, string qualified, bool extension = false)
    {
        int colon = qualified.IndexOf(':');
        string prefix = colon < 0 ? string.Empty : qualified[..colon];
        string name = colon < 0 ? qualified : qualified[(colon + 1)..];
        if (name.Length == 0)
        {
            return null;
        }
        string? uri = UriOf(scope, prefix);
        if (KindOf(uri) == XamlNamespaceKind.Language)
        {
            return LanguageType(name);
        }
        IReadOnlyList<string> namespaces = ClrNamespacesOf(uri);
        foreach (string candidate in extension ? [name + "Extension", name] : new[] { name })
        {
            foreach (string clr in namespaces)
            {
                if (Types.Find(clr.Length > 0 ? $"{clr}.{candidate}" : candidate) is { } found)
                {
                    return found;
                }
            }
        }
        return null;
    }

    /// <summary>The CLR types XAML's language names (<c>x:String</c>, <c>x:Double</c>).</summary>
    public ClrType? LanguageType(string name) => LanguageTypes.TryGetValue(name, out string? full) ? Types.Find(full) : null;

    public static readonly IReadOnlyDictionary<string, string> LanguageTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["String"] = "System.String",
        ["Double"] = "System.Double",
        ["Single"] = "System.Single",
        ["Int32"] = "System.Int32",
        ["Int64"] = "System.Int64",
        ["Int16"] = "System.Int16",
        ["Byte"] = "System.Byte",
        ["Boolean"] = "System.Boolean",
        ["Char"] = "System.Char",
        ["Decimal"] = "System.Decimal",
        ["Object"] = "System.Object",
        ["TimeSpan"] = "System.TimeSpan",
        ["Uri"] = "System.Uri",
    };

    /// <summary>The type an object element makes; null for a property element, an x: directive's element, or a name
    /// nothing resolves.</summary>
    public ClrType? TypeOf(XmlElement element) =>
        element.IsPropertyElement ? null : types.GetOrAdd(element, e => ResolveType(e, e.Name));

    /// <summary>A property element's owner and the property it sets (<c>&lt;Grid.RowDefinitions&gt;</c>).</summary>
    public (ClrType? Owner, XamlProperty? Property) PropertyElement(XmlElement element)
    {
        if (!element.IsPropertyElement)
        {
            return (null, null);
        }
        string local = element.LocalName;
        int dot = local.LastIndexOf('.');
        string ownerName = (element.Prefix.Length > 0 ? element.Prefix + ":" : string.Empty) + local[..dot];
        ClrType? owner = ResolveType(element, ownerName);
        if (owner is null)
        {
            return (null, null);
        }
        string property = local[(dot + 1)..];
        // The parent's own property, as written through a base type (<Panel.Children> in a StackPanel), else an attached one.
        XamlProperty? found = Dialect.Property(Types, ObjectOf(element) ?? owner, property) ?? Dialect.Property(Types, owner, property)
            ?? Dialect.Attached(Types, owner, property);
        return (owner, found);
    }

    /// <summary>The object an element's attributes and content belong to: its own type, or for a property element the
    /// object element it is in.</summary>
    public ClrType? ObjectOf(XmlElement element)
    {
        if (!element.IsPropertyElement)
        {
            return TypeOf(element);
        }
        return element.Parent is { } parent ? ObjectOf(parent) : null;
    }

    /// <summary>What an attribute sets on its element.</summary>
    public XamlAttributeMeaning MeaningOf(XmlAttribute attribute)
    {
        if (attribute.Name == "xmlns" || attribute.Prefix == "xmlns")
        {
            return new XamlAttributeMeaning(XamlAttributeKind.Namespace);
        }
        XmlElement element = attribute.Owner;
        if (attribute.Prefix.Length > 0)
        {
            string? uri = UriOf(element, attribute.Prefix);
            switch (KindOf(uri))
            {
                case XamlNamespaceKind.Language:
                    return new XamlAttributeMeaning(XamlAttributeKind.Directive);
                case XamlNamespaceKind.Ignorable:
                    return new XamlAttributeMeaning(XamlAttributeKind.Ignorable);
            }
            if (IsIgnored(element, attribute.Prefix))
            {
                return new XamlAttributeMeaning(XamlAttributeKind.Ignorable);
            }
        }
        string local = attribute.LocalName;
        int dot = local.LastIndexOf('.');
        if (dot > 0)
        {
            string ownerName = (attribute.Prefix.Length > 0 ? attribute.Prefix + ":" : string.Empty) + local[..dot];
            string name = local[(dot + 1)..];
            ClrType? owner = ResolveType(element, ownerName);
            if (owner is null)
            {
                return new XamlAttributeMeaning(XamlAttributeKind.Unknown);
            }
            if (owner.FullName == "Avalonia.Controls.Classes")
            {
                return new XamlAttributeMeaning(XamlAttributeKind.StyleClass, Owner: owner);
            }
            if (Dialect.Attached(Types, owner, name) is { } attachedProperty)
            {
                return new XamlAttributeMeaning(XamlAttributeKind.Attached, attachedProperty, Owner: owner);
            }
            // Owner.Property written for the element's own property (Button.Background on a Button).
            if (ObjectOf(element) is { } self && Types.IsA(self, owner.FullName) && Dialect.Property(Types, owner, name) is { } own)
            {
                return new XamlAttributeMeaning(XamlAttributeKind.Property, own, Owner: owner);
            }
            if (Dialect.EventsOf(Types, owner).FirstOrDefault(e => e.Name == name) is { } ownerEvent)
            {
                return new XamlAttributeMeaning(XamlAttributeKind.Event, Event: ownerEvent, Owner: owner);
            }
            return new XamlAttributeMeaning(XamlAttributeKind.Unknown, Owner: owner);
        }
        if (ObjectOf(element) is not { } type)
        {
            return new XamlAttributeMeaning(XamlAttributeKind.Unknown);
        }
        if (Dialect.Property(Types, type, local) is { } property)
        {
            return new XamlAttributeMeaning(XamlAttributeKind.Property, property);
        }
        if (Dialect.EventsOf(Types, type).FirstOrDefault(e => e.Name == local) is { } @event)
        {
            return new XamlAttributeMeaning(XamlAttributeKind.Event, Event: @event);
        }
        return new XamlAttributeMeaning(XamlAttributeKind.Unknown);
    }

    /// <summary>Whether a prefix is one <c>mc:Ignorable</c> names where the element is.</summary>
    public bool IsIgnored(XmlElement element, string prefix)
    {
        for (XmlElement? at = element; at is not null; at = at.Parent)
        {
            foreach (XmlAttribute attribute in at.Attributes)
            {
                if (attribute.LocalName == "Ignorable" && UriOf(at, attribute.Prefix) == XamlDialect.CompatibilityUri
                    && attribute.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(prefix, StringComparer.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>The type a binding at an element resolves its path against: the nearest <c>x:DataType</c> above it (a data
    /// template's <c>DataType</c> too), as Avalonia's compiled bindings do.</summary>
    public ClrType? DataTypeAt(XmlElement element, bool inherit = true, bool stopAtTemplates = false)
    {
        for (XmlElement? at = element; at is not null; at = inherit ? at.Parent : null)
        {
            // A template with no data type of its own is bound to what it is applied to, which no x:DataType above it says.
            if (stopAtTemplates && at != element && !at.IsPropertyElement && at.LocalName.EndsWith("Template", StringComparison.Ordinal)
                && at.Attribute("DataType") is null && Directive(at, "DataType") is null)
            {
                return null;
            }
            XmlAttribute? named = at.Attributes.FirstOrDefault(a => a.LocalName == "DataType" && a.Prefix.Length > 0 && KindOf(UriOf(at, a.Prefix)) == XamlNamespaceKind.Language);
            if (named is null && !at.IsPropertyElement && at.LocalName is "DataTemplate" or "TreeDataTemplate")
            {
                named = at.Attribute("DataType");
            }
            if (named is not null)
            {
                return TypeFromValue(at, named.Value);
            }
        }
        return null;
    }

    /// <summary>A type named in a value (<c>vm:MainViewModel</c>, <c>{x:Type vm:MainViewModel}</c>).</summary>
    public ClrType? TypeFromValue(XmlElement scope, string value)
    {
        string trimmed = value.Trim();
        if (trimmed.StartsWith('{') && MarkupExtension.Parse(trimmed, 0, trimmed.Length) is { } extension && extension.LocalName == "Type"
            && extension.Argument(null) is { } argument)
        {
            trimmed = argument.Value;
        }
        return ResolveType(scope, trimmed);
    }

    /// <summary>The type a style's or control theme's setters set properties of: a <c>ControlTheme</c>'s
    /// <c>TargetType</c>, or the type a <c>Style</c>'s selector ends at, the nearest such element at or above
    /// <paramref name="element"/>; a nested style's <c>^</c> reaches its parent's.</summary>
    public ClrType? StyleTargetAt(XmlElement element)
    {
        for (XmlElement? at = element; at is not null; at = at.Parent)
        {
            if (at.IsPropertyElement)
            {
                continue;
            }
            switch (at.LocalName)
            {
                case "ControlTheme" or "Style" when at.Attribute("TargetType") is { } target:
                    return TypeFromValue(at, target.Value);
                case "Style" when at.Attribute("Selector") is { } selector:
                    if (Selectors.TargetOf(this, at, selector.Value) is { } selected)
                    {
                        return selected;
                    }
                    break;
            }
        }
        return null;
    }

    /// <summary>The elements named with <c>x:Name</c> or <c>Name</c>, with their names, in document order.</summary>
    public IReadOnlyList<(string Name, XmlAttribute Attribute)> Names
    {
        get
        {
            List<(string, XmlAttribute)> names = [];
            foreach (XmlElement element in Tree.Elements)
            {
                if (NameAttribute(element) is { } attribute && attribute.Value.Length > 0)
                {
                    names.Add((attribute.Value, attribute));
                }
            }
            return names;
        }
    }

    /// <summary>The attribute that names an element (<c>x:Name</c>, else <c>Name</c>).</summary>
    public XmlAttribute? NameAttribute(XmlElement element) =>
        element.Attributes.FirstOrDefault(a => a.LocalName == "Name" && a.Prefix.Length > 0 && KindOf(UriOf(element, a.Prefix)) == XamlNamespaceKind.Language)
        ?? (element.IsPropertyElement ? null : element.Attribute("Name"));

    /// <summary>The directive attribute of XAML's language with a name on an element (<c>x:Key</c>).</summary>
    public XmlAttribute? Directive(XmlElement element, string name) =>
        element.Attributes.FirstOrDefault(a => a.LocalName == name && a.Prefix.Length > 0 && KindOf(UriOf(element, a.Prefix)) == XamlNamespaceKind.Language);
}
