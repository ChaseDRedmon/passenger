using Passenger.Xaml.Dialects;
using Passenger.Xaml.Metadata;
using Passenger.Xaml.Syntax;

namespace Passenger.Xaml.Features;

/// <summary>What a completion item names, which decides its badge.</summary>
public enum XamlItemKind
{
    Element,
    Type,
    Property,
    AttachedProperty,
    Event,
    Directive,
    Namespace,
    EnumValue,
    Value,
    Colour,
    Resource,
    Extension,
    Member,
    StyleClass,
    PseudoClass,
    ElementName,
    Asset,
    ClosingTag,
}

/// <summary>One thing completion offers: what the list shows, what goes in (a snippet's <c>$0</c> marks where the caret
/// stays), and whether completion opens again after it goes in (a property's value list once its quotes are in).</summary>
public sealed record XamlItem(string Label, XamlItemKind Kind, string Insert, string? Detail = null, bool IsSnippet = false, bool Reopen = false,
    string? Documentation = null)
{
    /// <summary>The colour it shows a swatch of, for a colour's name.</summary>
    public string? Colour { get; init; }
}

/// <summary>A completion list: the items, where the text they replace starts (it ends at the caret), and the characters
/// beyond a name's letters and digits that typing narrows it by rather than closes it (a path's slashes).</summary>
public sealed record XamlCompletionList(int Start, IReadOnlyList<XamlItem> Items, string WordCharacters = "");

/// <summary>Completion in a XAML file, from the file read against its project: element names after <c>&lt;</c> (types
/// of the namespaces in scope, the parent's property elements), attributes in a start tag (properties, attached
/// properties, events, XAML's directives, xmlns), a value by what it sets (an enum's members, booleans, colours, types,
/// resources, handlers, style classes, assets), markup extensions and their arguments, binding paths against
/// <c>x:DataType</c>, style selectors, and an end tag's name.</summary>
public static class XamlCompletion
{
    public static XamlCompletionList? At(XamlFile file, int offset)
    {
        string text = file.Text;
        if (offset < 0 || offset > text.Length || file.Tree.InComment(offset))
        {
            return null;
        }
        if (ClosingTag(file, offset) is { } closing)
        {
            return closing;
        }
        XmlPlace place = file.Tree.PlaceAt(offset);
        return place.Kind switch
        {
            XmlPlaceKind.StartTagName when place.Element is { } element => ElementNames(file, element, offset),
            XmlPlaceKind.InStartTag when place.Element is { } element && offset > element.NameEnd => Attributes(file, element, null, offset),
            XmlPlaceKind.AttributeName when place.Element is { } element => Attributes(file, element, place.Attribute, offset),
            XmlPlaceKind.AttributeValue when place.Attribute is { } attribute => Value(file, attribute, offset),
            _ => null,
        };
    }

    /// <summary>After <c>&lt;/</c>: the name of the element it ends.</summary>
    private static XamlCompletionList? ClosingTag(XamlFile file, int offset)
    {
        string text = file.Text;
        int start = offset;
        while (start > 0 && XmlTree.IsNameChar(text[start - 1]))
        {
            start--;
        }
        if (start < 2 || text[start - 1] != '/' || text[start - 2] != '<')
        {
            return null;
        }
        XmlElement? open = file.Tree.ElementAt(start - 2);
        while (open is not null && open.IsClosed && open.EndTagStart != start - 2)
        {
            open = open.Parent;
        }
        if (open is null || open.Name.Length == 0)
        {
            return null;
        }
        bool closed = offset < text.Length && text[offset] == '>';
        return new XamlCompletionList(start, [new XamlItem(open.Name, XamlItemKind.ClosingTag, closed ? open.Name : open.Name + ">")], ".:");
    }

    private static XamlCompletionList ElementNames(XamlFile file, XmlElement element, int offset)
    {
        string typed = file.Text[element.NameStart..offset];
        XmlElement? scope = element.Parent;
        List<XamlItem> items = [];
        int dot = typed.LastIndexOf('.');
        if (dot > 0)
        {
            // <Grid. : the properties set as elements, the owner's own and its attached ones.
            ClrType? owner = file.ResolveType(element, typed[..dot]);
            if (owner is not null)
            {
                foreach (XamlProperty property in file.Dialect.PropertiesOf(file.Types, owner))
                {
                    items.Add(new XamlItem(property.Name, XamlItemKind.Property, property.Name, property.ValueType.Display));
                }
                foreach (XamlProperty property in file.Dialect.AttachedOf(file.Types, owner))
                {
                    items.Add(new XamlItem(property.Name, XamlItemKind.AttachedProperty, property.Name, property.ValueType.Display));
                }
            }
            return new XamlCompletionList(element.NameStart + dot + 1, items);
        }
        int colon = typed.IndexOf(':');
        if (colon >= 0)
        {
            string prefix = typed[..colon];
            string? uri = file.UriOf(element, prefix);
            if (file.KindOf(uri) == XamlNamespaceKind.Language)
            {
                items.AddRange(XamlFile.LanguageTypes.Keys.Select(t => new XamlItem(t, XamlItemKind.Type, t, "XAML")));
            }
            else
            {
                foreach (ClrType type in TypesOf(file, uri, t => file.Dialect.CanCreate(file.Types, t)))
                {
                    items.Add(new XamlItem(type.Name, XamlItemKind.Element, type.Name, type.Namespace));
                }
            }
            return new XamlCompletionList(element.NameStart + colon + 1, items);
        }
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach ((string prefix, string uri) in file.PrefixesAt(element))
        {
            if (file.KindOf(uri) == XamlNamespaceKind.Language)
            {
                items.AddRange(XamlFile.LanguageTypes.Keys.Select(t => new XamlItem($"{prefix}:{t}", XamlItemKind.Type, $"{prefix}:{t}", "XAML")));
                continue;
            }
            foreach (ClrType type in TypesOf(file, uri, t => file.Dialect.CanCreate(file.Types, t)))
            {
                string label = prefix.Length > 0 ? $"{prefix}:{type.Name}" : type.Name;
                if (seen.Add(label))
                {
                    items.Add(new XamlItem(label, XamlItemKind.Element, label, type.Namespace));
                }
            }
        }
        // The parent's properties written as elements, as Rider lists them (Grid.RowDefinitions).
        if (scope is not null && file.ObjectOf(scope) is { } parentType && !scope.IsPropertyElement)
        {
            string owner = scope.Name;
            foreach (XamlProperty property in file.Dialect.PropertiesOf(file.Types, parentType))
            {
                if (property.IsCollection || !IsSimple(file, property))
                {
                    string label = $"{owner}.{property.Name}";
                    items.Add(new XamlItem(label, XamlItemKind.Property, label, property.ValueType.Display));
                }
            }
        }
        return new XamlCompletionList(element.NameStart, items);
    }

    /// <summary>Whether a property's value is one an attribute writes better than an element (a number, a string, an enum).</summary>
    private static bool IsSimple(XamlFile file, XamlProperty property)
    {
        TypeRef value = file.Dialect.ValueTypeOf(property.ValueType);
        if (value.Name.StartsWith("System.", StringComparison.Ordinal) && value.Name != "System.Object" && !value.Name.Contains("Collections", StringComparison.Ordinal))
        {
            return true;
        }
        return file.Types.Resolve(value) is { Kind: ClrTypeKind.Enum or ClrTypeKind.Struct };
    }

    /// <summary>The types an xmlns URI's namespaces hold that pass a test.</summary>
    private static IEnumerable<ClrType> TypesOf(XamlFile file, string? uri, Func<ClrType, bool> keep)
    {
        foreach (string clr in file.ClrNamespacesOf(uri))
        {
            foreach (ClrType type in file.Types.InNamespace(clr))
            {
                if ((type.IsPublic || type.IsFromSource) && keep(type))
                {
                    yield return type;
                }
            }
        }
    }

    private static XamlCompletionList? Attributes(XamlFile file, XmlElement element, XmlAttribute? typing, int offset)
    {
        int start = typing?.NameStart ?? offset;
        string typed = file.Text[start..offset];
        bool hasValue = typing?.HasValue == true;
        HashSet<string> present = new(element.Attributes.Where(a => a != typing).Select(a => a.Name), StringComparer.Ordinal);
        List<XamlItem> items = [];
        int colon = typed.IndexOf(':');
        int dot = typed.LastIndexOf('.');
        if (colon > 0 && dot < 0)
        {
            string prefix = typed[..colon];
            if (prefix == "xmlns")
            {
                return null;
            }
            string? uri = file.UriOf(element, prefix);
            switch (file.KindOf(uri))
            {
                case XamlNamespaceKind.Language:
                    foreach ((string name, string about) in Directives(element))
                    {
                        if (!present.Contains($"{prefix}:{name}"))
                        {
                            items.Add(Attribute(name, XamlItemKind.Directive, about, hasValue, reopen: name is "DataType" or "Class" or "CompileBindings"));
                        }
                    }
                    break;
                case XamlNamespaceKind.Ignorable when uri == XamlDialect.DesignerUri:
                    foreach (string name in (string[])["DesignWidth", "DesignHeight", "DataContext"])
                    {
                        items.Add(Attribute(name, XamlItemKind.Directive, "Designer", hasValue));
                    }
                    break;
                default:
                    // prefix:Owner.Property is written with its dot; before it, the prefix's types that have attached properties.
                    foreach (ClrType owner in TypesOf(file, uri, t => file.Dialect.AttachedOf(file.Types, t).Count > 0))
                    {
                        foreach (XamlProperty attached in file.Dialect.AttachedOf(file.Types, owner))
                        {
                            string label = $"{owner.Name}.{attached.Name}";
                            items.Add(Attribute(label, XamlItemKind.AttachedProperty, attached.ValueType.Display, hasValue, Reopens(file, attached)));
                        }
                    }
                    break;
            }
            return new XamlCompletionList(start + colon + 1, items, ".");
        }
        if (dot > 0)
        {
            // Owner. : its attached properties, or the element's own written through its type.
            ClrType? owner = file.ResolveType(element, typed[..dot]);
            if (owner is not null)
            {
                foreach (XamlProperty attached in file.Dialect.AttachedOf(file.Types, owner))
                {
                    items.Add(Attribute(attached.Name, XamlItemKind.AttachedProperty, attached.ValueType.Display, hasValue, Reopens(file, attached)));
                }
                if (file.ObjectOf(element) is { } self && file.Types.IsA(self, owner.FullName))
                {
                    foreach (XamlProperty property in file.Dialect.PropertiesOf(file.Types, owner))
                    {
                        items.Add(Attribute(property.Name, XamlItemKind.Property, property.ValueType.Display, hasValue, Reopens(file, property)));
                    }
                }
            }
            return new XamlCompletionList(start + dot + 1, items);
        }
        ClrType? type = file.ObjectOf(element);
        if (type is not null)
        {
            foreach (XamlProperty property in file.Dialect.PropertiesOf(file.Types, type))
            {
                if (!present.Contains(property.Name) && !(property.IsCollection && property.Name is not "Classes"))
                {
                    items.Add(Attribute(property.Name, XamlItemKind.Property, property.ValueType.Display, hasValue, Reopens(file, property)));
                }
            }
            foreach (ClrMember @event in file.Dialect.EventsOf(file.Types, type))
            {
                if (!present.Contains(@event.Name))
                {
                    items.Add(Attribute(@event.Name, XamlItemKind.Event, @event.Type.Display, hasValue, reopen: file.ClassType is not null));
                }
            }
        }
        // Attached properties of the types in scope by their owner (Grid.Row, DockPanel.Dock), with the owner's prefix.
        foreach ((string prefix, string uri) in file.PrefixesAt(element))
        {
            if (file.KindOf(uri) is not (XamlNamespaceKind.Mapped or XamlNamespaceKind.Clr))
            {
                continue;
            }
            foreach (ClrType owner in TypesOf(file, uri, t => file.Dialect.AttachedOf(file.Types, t).Count > 0))
            {
                foreach (XamlProperty attached in file.Dialect.AttachedOf(file.Types, owner))
                {
                    string label = $"{(prefix.Length > 0 ? prefix + ":" : string.Empty)}{owner.Name}.{attached.Name}";
                    if (!present.Contains(label))
                    {
                        items.Add(Attribute(label, XamlItemKind.AttachedProperty, attached.ValueType.Display, hasValue, Reopens(file, attached)));
                    }
                }
            }
        }
        foreach ((string prefix, string uri) in file.PrefixesAt(element))
        {
            if (file.KindOf(uri) == XamlNamespaceKind.Language)
            {
                foreach ((string name, string about) in Directives(element))
                {
                    string label = $"{prefix}:{name}";
                    if (!present.Contains(label))
                    {
                        items.Add(Attribute(label, XamlItemKind.Directive, about, hasValue, reopen: name is "DataType" or "Class" or "CompileBindings"));
                    }
                }
            }
            else if (uri == XamlDialect.DesignerUri)
            {
                foreach (string name in (string[])["DesignWidth", "DesignHeight", "DataContext"])
                {
                    items.Add(Attribute($"{prefix}:{name}", XamlItemKind.Directive, "Designer", hasValue));
                }
            }
        }
        items.Add(new XamlItem("xmlns:", XamlItemKind.Namespace, "xmlns:", "Namespace declaration"));
        return new XamlCompletionList(start, items, ".:");
    }

    private static XamlItem Attribute(string name, XamlItemKind kind, string? detail, bool hasValue, bool reopen = false) =>
        hasValue ? new XamlItem(name, kind, name, detail) : new XamlItem(name, kind, $"{name}=\"$0\"", detail, IsSnippet: true, Reopen: reopen);

    /// <summary>XAML's directives an element can carry: <c>x:Class</c> only on the root.</summary>
    private static IEnumerable<(string Name, string About)> Directives(XmlElement element)
    {
        if (element.Parent is null)
        {
            yield return ("Class", "The code-behind's class");
            yield return ("ClassModifier", "The class's access");
        }
        yield return ("Name", "A field for the element in the code-behind");
        yield return ("Key", "The resource's key");
        yield return ("DataType", "The type bindings resolve against");
        yield return ("CompileBindings", "Whether bindings compile");
        yield return ("FieldModifier", "The named field's access");
        yield return ("Shared", "Whether the resource is shared");
    }

    /// <summary>Whether a property's value has choices worth listing once its quotes are in.</summary>
    private static bool Reopens(XamlFile file, XamlProperty property)
    {
        TypeRef value = file.Dialect.ValueTypeOf(property.ValueType);
        if (value.Name is "System.Boolean" or "System.Type" || IsBrushOrColour(value.Name) || value.Name.EndsWith("Selector", StringComparison.Ordinal))
        {
            return true;
        }
        return file.Types.Resolve(value) is { Kind: ClrTypeKind.Enum };
    }

    private static bool IsBrushOrColour(string name) =>
        name is "Avalonia.Media.IBrush" or "Avalonia.Media.Brush" or "Avalonia.Media.ISolidColorBrush" or "Avalonia.Media.SolidColorBrush" or "Avalonia.Media.Color"
            or "System.Windows.Media.Brush" or "System.Windows.Media.Color";

    private static XamlCompletionList? Value(XamlFile file, XmlAttribute attribute, int offset)
    {
        XmlElement element = attribute.Owner;
        string text = file.Text;
        string before = text[attribute.ValueStart..offset];
        if (before.TrimStart().StartsWith('{') && !before.TrimStart().StartsWith("{}", StringComparison.Ordinal))
        {
            return Extension(file, attribute, offset);
        }
        XamlAttributeMeaning meaning = file.MeaningOf(attribute);
        switch (meaning.Kind)
        {
            case XamlAttributeKind.Namespace:
                return Namespaces(file, attribute);
            case XamlAttributeKind.Directive:
                return attribute.LocalName switch
                {
                    "Class" => new XamlCompletionList(attribute.ValueStart, [.. file.Types.All.Where(t => t.IsFromSource && t.Kind == ClrTypeKind.Class && !t.IsNested)
                        .Select(t => new XamlItem(t.FullName, XamlItemKind.Type, t.FullName, "Class"))], "."),
                    "DataType" => TypeNames(file, element, attribute.ValueStart, t => t.Kind is ClrTypeKind.Class or ClrTypeKind.Struct or ClrTypeKind.Interface),
                    "CompileBindings" or "Shared" => Booleans(attribute.ValueStart),
                    _ => null,
                };
            case XamlAttributeKind.Event:
                return Handlers(file, attribute.ValueStart);
        }
        if (attribute.LocalName == "Classes" && meaning.Kind == XamlAttributeKind.Property)
        {
            int wordStart = offset;
            while (wordStart > attribute.ValueStart && !char.IsWhiteSpace(text[wordStart - 1]))
            {
                wordStart--;
            }
            return new XamlCompletionList(wordStart, [.. StyleClasses(file).Select(c => new XamlItem(c, XamlItemKind.StyleClass, c))], "-");
        }
        if (element.LocalName == "Style" && attribute.Name == "Selector")
        {
            return Selector(file, attribute, offset);
        }
        if (attribute.Name == "Property" && element.LocalName == "Setter")
        {
            return SetterProperties(file, element, attribute.ValueStart);
        }
        if (attribute.Name == "Value" && element.LocalName == "Setter" && SetterProperty(file, element) is { } set)
        {
            return ValuesOf(file, element, attribute, file.Dialect.ValueTypeOf(set.ValueType), offset);
        }
        if (attribute.Name is "Source" or "Icon" || before.StartsWith("avares://", StringComparison.Ordinal) || before.StartsWith('/'))
        {
            return Assets(file, attribute, offset, xamlOnly: element.LocalName is "ResourceInclude" or "MergeResourceInclude" or "StyleInclude");
        }
        if (meaning.Property is { } property)
        {
            return ValuesOf(file, element, attribute, file.Dialect.ValueTypeOf(property.ValueType), offset);
        }
        return null;
    }

    private static XamlCompletionList? ValuesOf(XamlFile file, XmlElement element, XmlAttribute attribute, TypeRef type, int offset)
    {
        List<XamlItem> items = [];
        string name = type.Name;
        if (name == "System.Boolean")
        {
            return Booleans(attribute.ValueStart);
        }
        if (name == "System.Type")
        {
            return TypeNames(file, element, attribute.ValueStart, _ => true);
        }
        if (IsBrushOrColour(name))
        {
            foreach ((string colour, string value) in NamedColours(file))
            {
                items.Add(new XamlItem(colour, XamlItemKind.Colour, colour, value) { Colour = value });
            }
            return new XamlCompletionList(attribute.ValueStart, items);
        }
        if (name.EndsWith(".AvaloniaProperty", StringComparison.Ordinal))
        {
            return SetterProperties(file, element, attribute.ValueStart);
        }
        if (file.Types.Resolve(type) is { Kind: ClrTypeKind.Enum } @enum)
        {
            foreach (ClrMember member in @enum.Members.Where(m => m.Kind == MemberKind.Field && m.IsStatic))
            {
                items.Add(new XamlItem(member.Name, XamlItemKind.EnumValue, member.Name, @enum.Name));
            }
            return new XamlCompletionList(attribute.ValueStart, items);
        }
        return null;
    }

    private static XamlCompletionList Booleans(int start) =>
        new(start, [new XamlItem("True", XamlItemKind.Value, "True"), new XamlItem("False", XamlItemKind.Value, "False")]);

    /// <summary>The colours Avalonia's <c>Colors</c> names (or WPF's), each with its value for a swatch.</summary>
    public static IReadOnlyList<(string Name, string Value)> NamedColours(XamlFile file) => KnownColours.Names;

    /// <summary>Types a value can name, with the prefix each namespace has here (<c>vm:MainViewModel</c>).</summary>
    private static XamlCompletionList TypeNames(XamlFile file, XmlElement element, int start, Func<ClrType, bool> keep)
    {
        List<XamlItem> items = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach ((string prefix, string uri) in file.PrefixesAt(element))
        {
            if (file.KindOf(uri) == XamlNamespaceKind.Language)
            {
                items.AddRange(XamlFile.LanguageTypes.Keys.Select(t => new XamlItem($"{prefix}:{t}", XamlItemKind.Type, $"{prefix}:{t}", "XAML")));
                continue;
            }
            foreach (ClrType type in TypesOf(file, uri, t => t.Arity == 0 && !t.IsNested && keep(t)))
            {
                string label = prefix.Length > 0 ? $"{prefix}:{type.Name}" : type.Name;
                if (seen.Add(label))
                {
                    items.Add(new XamlItem(label, XamlItemKind.Type, label, type.Namespace));
                }
            }
        }
        return new XamlCompletionList(start, items, ":.");
    }

    /// <summary>An xmlns value: <c>using:</c> each CLR namespace (the project's own first), and the URIs the assemblies map.</summary>
    private static XamlCompletionList Namespaces(XamlFile file, XmlAttribute attribute)
    {
        List<XamlItem> items = [];
        HashSet<string> own = new(file.Types.All.Where(t => t.IsFromSource).Select(t => t.Namespace), StringComparer.Ordinal);
        foreach (string uri in file.Types.XmlnsDefinitions.Select(d => d.Uri).Distinct(StringComparer.Ordinal))
        {
            items.Add(new XamlItem(uri, XamlItemKind.Namespace, uri, "xmlns"));
        }
        items.Add(new XamlItem(XamlDialect.XamlUri, XamlItemKind.Namespace, XamlDialect.XamlUri, "XAML"));
        items.Add(new XamlItem(XamlDialect.DesignerUri, XamlItemKind.Namespace, XamlDialect.DesignerUri, "Designer"));
        items.Add(new XamlItem(XamlDialect.CompatibilityUri, XamlItemKind.Namespace, XamlDialect.CompatibilityUri, "Markup compatibility"));
        foreach (string clr in file.Types.Namespaces)
        {
            items.Add(new XamlItem($"using:{clr}", XamlItemKind.Namespace, $"using:{clr}", own.Contains(clr) ? "This project" : "CLR namespace"));
        }
        return new XamlCompletionList(attribute.ValueStart, items, ":./-");
    }

    /// <summary>The code-behind's methods an event can call.</summary>
    private static XamlCompletionList? Handlers(XamlFile file, int start)
    {
        if (file.ClassType is not { } owner)
        {
            return null;
        }
        List<XamlItem> items = [.. owner.Members.Where(m => m.Kind == MemberKind.Method && !m.IsStatic && IsHandler(m))
            .Select(m => m.Name).Distinct(StringComparer.Ordinal).Select(n => new XamlItem(n, XamlItemKind.Member, n, owner.Name))];
        return new XamlCompletionList(start, items);
    }

    /// <summary>Whether a method can handle an event: nothing returned, a sender and the event's arguments.</summary>
    public static bool IsHandler(ClrMember method) =>
        method.Parameters.Count == 2 && method.Type.Name is "System.Void" && method.Parameters[1].Name.EndsWith("EventArgs", StringComparison.Ordinal);

    /// <summary>The style classes the project's XAML uses or styles.</summary>
    private static IEnumerable<string> StyleClasses(XamlFile file) =>
        file.Project.Files.Values.SelectMany(f => f.StyleClasses).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    /// <summary>The setter's property: its <c>Property</c> on the style's target type, or an attached one's owner.</summary>
    public static XamlProperty? SetterProperty(XamlFile file, XmlElement setter)
    {
        if (setter.Attribute("Property") is not { } named)
        {
            return null;
        }
        string value = named.Value.Trim().Trim('(', ')');
        int dot = value.LastIndexOf('.');
        if (dot > 0 && file.ResolveType(setter, value[..dot]) is { } owner)
        {
            return file.Dialect.Attached(file.Types, owner, value[(dot + 1)..]) ?? file.Dialect.Property(file.Types, owner, value[(dot + 1)..]);
        }
        return file.StyleTargetAt(setter) is { } target ? file.Dialect.Property(file.Types, target, value) : null;
    }

    private static XamlCompletionList? SetterProperties(XamlFile file, XmlElement setter, int start)
    {
        if (file.StyleTargetAt(setter) is not { } target)
        {
            return null;
        }
        List<XamlItem> items = [.. file.Dialect.PropertiesOf(file.Types, target).Where(p => !p.IsCollection)
            .Select(p => new XamlItem(p.Name, XamlItemKind.Property, p.Name, p.ValueType.Display))];
        return new XamlCompletionList(start, items);
    }

    private static XamlCompletionList? Selector(XamlFile file, XmlAttribute attribute, int offset)
    {
        string selector = file.Text[attribute.ValueStart..attribute.ValueEnd];
        SelectorPlace place = Selectors.PlaceAt(selector, offset - attribute.ValueStart);
        XmlElement element = attribute.Owner;
        ClrType? type = place.Type is { } named ? file.ResolveType(element, named.Replace('|', ':')) : null;
        int start = attribute.ValueStart + place.Start;
        switch (place.Kind)
        {
            case SelectorPart.Type:
                List<XamlItem> types = [];
                HashSet<string> seen = new(StringComparer.Ordinal);
                foreach ((string prefix, string uri) in file.PrefixesAt(element))
                {
                    foreach (ClrType control in TypesOf(file, uri, t => t.Kind == ClrTypeKind.Class && t.Arity == 0 && !t.IsNested && file.Types.IsA(t, "Avalonia.StyledElement")))
                    {
                        string label = prefix.Length > 0 ? $"{prefix}|{control.Name}" : control.Name;
                        if (seen.Add(label))
                        {
                            types.Add(new XamlItem(label, XamlItemKind.Type, label, control.Namespace));
                        }
                    }
                }
                types.Add(new XamlItem("/template/", XamlItemKind.Value, "/template/ ", "Inside the template"));
                return new XamlCompletionList(start, types, "|");
            case SelectorPart.Class:
                return new XamlCompletionList(start, [.. StyleClasses(file).Select(c => new XamlItem(c, XamlItemKind.StyleClass, c))], "-");
            case SelectorPart.PseudoClass:
                List<XamlItem> pseudo = [.. Selectors.PseudoClassesOf(file.Types, type).Select(p => new XamlItem(p, XamlItemKind.PseudoClass, p))];
                pseudo.AddRange(((string[])[":is(", ":not(", ":nth-child(", ":nth-last-child("]).Select(f => new XamlItem(f, XamlItemKind.PseudoClass, f)));
                return new XamlCompletionList(start, pseudo, ":-(");
            case SelectorPart.Name:
                return new XamlCompletionList(start, [.. file.Names.Select(n => n.Name).Distinct(StringComparer.Ordinal).Select(n => new XamlItem(n, XamlItemKind.ElementName, n))]);
            case SelectorPart.Property when type is not null:
                return new XamlCompletionList(start, [.. file.Dialect.PropertiesOf(file.Types, type).Select(p => new XamlItem(p.Name, XamlItemKind.Property, p.Name, p.ValueType.Display))]);
        }
        return null;
    }

    /// <summary>Assets a value names: <c>avares://</c> paths of this project's and referenced projects' embedded files, or
    /// paths from its folder; only XAML files where the element merges one in.</summary>
    private static XamlCompletionList Assets(XamlFile file, XmlAttribute attribute, int offset, bool xamlOnly)
    {
        string typed = file.Text[attribute.ValueStart..offset];
        XamlProject project = file.Project;
        List<XamlItem> items = [];
        IEnumerable<string> own = project.Assets.Concat(project.Files.Keys.Select(f => Path.GetRelativePath(project.Folder, f).Replace('\\', '/')))
            .Distinct(StringComparer.Ordinal);
        foreach (string asset in own)
        {
            if (xamlOnly && !IsXaml(asset))
            {
                continue;
            }
            string full = $"avares://{project.AssemblyName}/{asset}";
            items.Add(new XamlItem(full, XamlItemKind.Asset, full, Path.GetFileName(asset)));
            if (typed.StartsWith('/'))
            {
                items.Add(new XamlItem("/" + asset, XamlItemKind.Asset, "/" + asset, Path.GetFileName(asset)));
            }
        }
        return new XamlCompletionList(attribute.ValueStart, items, "/:.-#");

        static bool IsXaml(string path) => Path.GetExtension(path).ToLowerInvariant() is ".axaml" or ".xaml" or ".paml";
    }

    private static XamlCompletionList? Extension(XamlFile file, XmlAttribute attribute, int offset)
    {
        string text = file.Text;
        if (MarkupExtension.Parse(text, attribute.ValueStart, attribute.ValueEnd) is not { } root)
        {
            return null;
        }
        MarkupExtension extension = root.InnermostAt(offset);
        XmlElement element = attribute.Owner;
        if (offset <= extension.NameEnd || extension.Name.Length == 0 && offset == extension.Start + 1)
        {
            return ExtensionNames(file, element, extension.NameStart, offset);
        }
        ClrType? type = file.ResolveType(element, extension.Name, extension: true);
        string local = extension.LocalName;
        MarkupArgument? argument = extension.ArgumentAt(offset);
        bool inKey = argument is { Key: not null } && offset <= argument.KeyEnd;
        int valueStart = argument is null ? offset : inKey ? argument.KeyStart : argument.ValueStart;
        // An argument begun after a comma or a space is either a positional value or a key; both are offered.
        bool positional = argument is null ? extension.Arguments.All(a => a.ValueEnd < offset || a.Key is not null) && !extension.Arguments.Any(a => a.Key is not null)
            : argument.Key is null;
        if (inKey)
        {
            return new XamlCompletionList(argument!.KeyStart, Keys(file, type));
        }
        if (argument is { Key: { } key } named)
        {
            return ArgumentValue(file, element, type, key, named.ValueStart, offset);
        }
        bool first = argument?.Index == 0 || (argument is null && !extension.Arguments.Any(a => a.Key is null));
        List<XamlItem> items = [];
        switch (local)
        {
            case "StaticResource" or "DynamicResource" when first:
                items.AddRange(ResourceKeys(file, dynamic: local == "DynamicResource"));
                return new XamlCompletionList(valueStart, items, ".");
            case "Binding" or "CompiledBinding" or "ReflectionBinding" or "TemplateBinding" when positional:
                ClrType? source = local == "TemplateBinding" ? file.StyleTargetAt(element) : file.DataTypeAt(element);
                XamlCompletionList? path = BindingPath(file, element, source, valueStart, offset, local == "TemplateBinding");
                if (path is null)
                {
                    return new XamlCompletionList(valueStart, Keys(file, type));
                }
                // Where nothing of a path is typed yet, an argument's key may be as well.
                return offset == valueStart ? path with { Items = [.. path.Items, .. Keys(file, type)] } : path;
            case "Static" when first:
                return Statics(file, element, valueStart, offset);
            case "Type" when first:
                return TypeNames(file, element, valueStart, t => t.Arity == 0);
        }
        return new XamlCompletionList(valueStart, Keys(file, type));
    }

    /// <summary>The named arguments an extension takes: its type's settable properties, as <c>Key=</c>.</summary>
    private static IReadOnlyList<XamlItem> Keys(XamlFile file, ClrType? type)
    {
        if (type is null)
        {
            return [];
        }
        return [.. file.Types.MembersOf(type).Where(m => m.Kind == MemberKind.Property && m.CanWrite && !m.IsStatic && m.IsPublic)
            .Select(m => m.Name).Distinct(StringComparer.Ordinal)
            .Select(n => new XamlItem(n + "=", XamlItemKind.Property, n + "=", type.Name, Reopen: true))];
    }

    private static XamlCompletionList? ArgumentValue(XamlFile file, XmlElement element, ClrType? type, string key, int start, int offset)
    {
        if (type is null)
        {
            return null;
        }
        if (key == "ElementName")
        {
            return new XamlCompletionList(start, [.. file.Names.Select(n => n.Name).Distinct(StringComparer.Ordinal).Select(n => new XamlItem(n, XamlItemKind.ElementName, n))]);
        }
        if (key == "Path" && type.Name is "Binding" or "CompiledBindingExtension" or "ReflectionBindingExtension")
        {
            return BindingPath(file, element, file.DataTypeAt(element), start, offset, template: false);
        }
        if (key is "AncestorType" or "DataType")
        {
            return TypeNames(file, element, start, t => t.Arity == 0);
        }
        ClrMember? property = file.Types.MembersOf(type).FirstOrDefault(m => m.Kind == MemberKind.Property && m.Name == key);
        if (property is null)
        {
            return null;
        }
        if (file.Types.Resolve(property.Type) is { Kind: ClrTypeKind.Enum } @enum)
        {
            return new XamlCompletionList(start, [.. @enum.Members.Where(m => m.Kind == MemberKind.Field && m.IsStatic).Select(m => new XamlItem(m.Name, XamlItemKind.EnumValue, m.Name, @enum.Name))]);
        }
        if (property.Type.Name == "System.Boolean")
        {
            return Booleans(start);
        }
        return null;
    }

    private static XamlCompletionList ExtensionNames(XamlFile file, XmlElement element, int start, int offset)
    {
        List<XamlItem> items = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach ((string prefix, string uri) in file.PrefixesAt(element))
        {
            if (file.KindOf(uri) == XamlNamespaceKind.Language)
            {
                foreach (string intrinsic in (string[])["Static", "Type", "Null", "True", "False"])
                {
                    items.Add(new XamlItem($"{prefix}:{intrinsic}", XamlItemKind.Extension, $"{prefix}:{intrinsic}", "XAML"));
                }
                continue;
            }
            foreach (ClrType type in TypesOf(file, uri, t => file.Dialect.IsMarkupExtension(file.Types, t)))
            {
                string name = type.Name.EndsWith("Extension", StringComparison.Ordinal) ? type.Name[..^"Extension".Length] : type.Name;
                string label = prefix.Length > 0 ? $"{prefix}:{name}" : name;
                if (seen.Add(label))
                {
                    items.Add(new XamlItem(label, XamlItemKind.Extension, label, type.Namespace));
                }
            }
        }
        return new XamlCompletionList(start, items, ":");
    }

    /// <summary>Resource keys a file can reach: its own, what it merges in, the application's; for a dynamic resource,
    /// the themes' too.</summary>
    private static IEnumerable<XamlItem> ResourceKeys(XamlFile file, bool dynamic)
    {
        XamlProject project = file.Project;
        HashSet<string> seen = new(StringComparer.Ordinal);
        XamlFileSummary own = project.Files.TryGetValue(file.Path, out XamlFileSummary? summary) ? summary : XamlFileSummary.Of(file.Path, file.Tree, project.ResolveSource);
        foreach (XamlFileSummary scope in project.ResourceScopes(own))
        {
            foreach (ResourceDefinition definition in scope.Resources)
            {
                if (seen.Add(definition.Key) && !definition.Key.StartsWith('{'))
                {
                    yield return new XamlItem(definition.Key, XamlItemKind.Resource, definition.Key, $"{definition.ElementName} in {Path.GetFileName(scope.Path)}")
                    {
                        Colour = definition.Colour,
                    };
                }
            }
        }
        if (dynamic)
        {
            foreach (string key in project.ThemeKeys)
            {
                if (seen.Add(key))
                {
                    yield return new XamlItem(key, XamlItemKind.Resource, key, "Theme");
                }
            }
        }
    }

    /// <summary>A binding's path: each step before the caret resolved from the data type through the properties named
    /// (<c>Person.Address.</c>), the properties of where it ends offered. <c>!</c> before the path, and <c>#name.</c>
    /// to start from a named element, are read as Avalonia reads them.</summary>
    private static XamlCompletionList? BindingPath(XamlFile file, XmlElement element, ClrType? source, int start, int offset, bool template)
    {
        string typed = file.Text[start..offset];
        int at = 0;
        while (at < typed.Length && typed[at] == '!')
        {
            at++;
        }
        ClrType? type = source;
        if (at < typed.Length && typed[at] == '#')
        {
            int end = typed.IndexOf('.', at);
            if (end < 0)
            {
                return new XamlCompletionList(start + at + 1, [.. file.Names.Select(n => n.Name).Distinct(StringComparer.Ordinal).Select(n => new XamlItem(n, XamlItemKind.ElementName, n))]);
            }
            string name = typed[(at + 1)..end];
            XmlAttribute? named = file.Names.Where(n => n.Name == name).Select(n => n.Attribute).FirstOrDefault();
            type = named is null ? null : file.ObjectOf(named.Owner);
            at = end + 1;
        }
        if (type is null)
        {
            return null;
        }
        string[] steps = typed[at..].Split('.');
        for (int i = 0; i < steps.Length - 1; i++)
        {
            string step = steps[i].Trim();
            int bracket = step.IndexOf('[');
            string member = bracket < 0 ? step : step[..bracket];
            ClrMember? property = file.Types.MembersOf(type).FirstOrDefault(m => m.Kind == MemberKind.Property && m.Name == member && !m.IsStatic);
            ClrType? next = property is null ? null : file.Types.Resolve(property.Type);
            if (next is not null && bracket >= 0 && property!.Type.Arguments.Count > 0)
            {
                next = file.Types.Resolve(property.Type.Arguments[^1]);
            }
            if (next is null)
            {
                return null;
            }
            type = next;
        }
        int last = start + typed.Length - steps[^1].Length;
        IEnumerable<ClrMember> members = file.Types.MembersOf(type).Where(m => m.Kind == MemberKind.Property && !m.IsStatic && (m.IsPublic || template));
        List<XamlItem> items = [.. members.Select(m => m.Name).Distinct(StringComparer.Ordinal).Select(n =>
        {
            ClrMember member = members.First(m => m.Name == n);
            return new XamlItem(n, XamlItemKind.Member, n, $"{member.Type.Display} in {member.DeclaringType.Name}");
        })];
        return new XamlCompletionList(last, items);
    }

    /// <summary><c>{x:Static}</c>: types before the dot, then a type's static members (its enum's members).</summary>
    private static XamlCompletionList? Statics(XamlFile file, XmlElement element, int start, int offset)
    {
        string typed = file.Text[start..offset];
        int dot = typed.LastIndexOf('.');
        if (dot < 0)
        {
            return TypeNames(file, element, start, t => t.Arity == 0);
        }
        if (file.ResolveType(element, typed[..dot]) is not { } type)
        {
            return null;
        }
        return new XamlCompletionList(start + dot + 1, [.. type.Members.Where(m => m.IsStatic && m.Kind is MemberKind.Property or MemberKind.Field)
            .Select(m => new XamlItem(m.Name, type.Kind == ClrTypeKind.Enum ? XamlItemKind.EnumValue : XamlItemKind.Member, m.Name, m.Type.Display))]);
    }
}
