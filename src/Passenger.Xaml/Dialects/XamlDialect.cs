using System.Collections.Concurrent;
using Passenger.Xaml.Metadata;

namespace Passenger.Xaml.Dialects;

/// <summary>How a property is declared: a plain CLR property, or its framework's own kind (Avalonia's styled and direct
/// properties, WPF's dependency properties), or an attached property set on other elements.</summary>
public enum XamlPropertyKind
{
    Clr,
    Styled,
    Direct,
    Attached,
    Dependency,
}

/// <summary>A property an element can set, as an attribute or a property element: its name, the type that declares it,
/// the type its value is read as, and what declares it in code (its framework field, its CLR property, an attached
/// property's setter), for a description and go to declaration.</summary>
public sealed record XamlProperty(string Name, ClrType Owner, TypeRef ValueType, XamlPropertyKind Kind, ClrMember? Field = null, ClrMember? ClrProperty = null,
    ClrMember? Setter = null, bool IsCollection = false)
{
    public bool IsAttached => Kind == XamlPropertyKind.Attached;

    /// <summary>A property XAML reads but cannot set (WPF's <c>IsMouseOver</c>), which only a trigger names.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>What go to declaration lands on: the CLR property, else the framework's field, else the setter.</summary>
    public ClrMember? Declaration => ClrProperty ?? Field ?? Setter;
}

/// <summary>A XAML dialect: what Avalonia's XAML and WPF's share is the language (<c>x:</c>, xmlns, markup extensions,
/// property elements) the core reads; how each declares properties, content, events, templates and styles, and which
/// namespaces are its own, is the dialect's. Answers are kept per type, since a file asks the same types over and over.</summary>
public abstract class XamlDialect
{
    /// <summary>XAML's own namespace, the <c>x:</c> prefix's.</summary>
    public const string XamlUri = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The designer's namespace (<c>d:</c>), ignorable at run time.</summary>
    public const string DesignerUri = "http://schemas.microsoft.com/expression/blend/2008";

    /// <summary>Markup compatibility's namespace (<c>mc:</c>, its <c>Ignorable</c>).</summary>
    public const string CompatibilityUri = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>Each type system's answers, gone with it once a newer one replaces it.</summary>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TypeSystem, Answers> kept = [];

    private sealed class Answers
    {
        public ConcurrentDictionary<ClrType, IReadOnlyList<XamlProperty>> Properties { get; } = new();

        public ConcurrentDictionary<ClrType, IReadOnlyList<XamlProperty>> Attached { get; } = new();
    }

    public abstract string Name { get; }

    /// <summary>The namespace a file's elements are in by default.</summary>
    public abstract string PresentationUri { get; }

    /// <summary>The file extensions its XAML is written in.</summary>
    public abstract IReadOnlyList<string> Extensions { get; }

    /// <summary>The property every element's resources are set through (<c>Resources</c>).</summary>
    public virtual string ResourcesProperty => "Resources";

    /// <summary>The markup extensions its compiler knows by name though no type is written for them, beyond x:'s.</summary>
    public virtual IReadOnlyList<string> IntrinsicExtensions => [];

    /// <summary>The markup extensions of XAML's own namespace it takes (<c>x:Static</c>, <c>x:Type</c>).</summary>
    public virtual IReadOnlyList<string> LanguageExtensions { get; } = ["Static", "Type", "Null", "Array", "Reference"];

    /// <summary>Whether its compiler compiles bindings against <c>x:DataType</c> (Avalonia's), which <c>x:DataType</c> and
    /// <c>x:CompileBindings</c> are for.</summary>
    public virtual bool CompilesBindings => false;

    /// <summary>The markup extensions of the designer's namespace its designer reads (WPF's <c>d:DesignInstance</c>).</summary>
    public virtual IReadOnlyList<string> DesignerExtensions => [];

    /// <summary>The project file's item types that embed an asset a XAML value can name (Avalonia's
    /// <c>AvaloniaResource</c>, WPF's <c>Resource</c>).</summary>
    public virtual IReadOnlyList<string> AssetItems => [];

    /// <summary>The xmlns a CLR namespace is written as, of an assembly not the project's own when one is named.</summary>
    public virtual string ClrNamespaceUri(string clrNamespace, string? assembly) =>
        assembly is null ? $"clr-namespace:{clrNamespace}" : $"clr-namespace:{clrNamespace};assembly={assembly}";

    /// <summary>The properties a type's elements can set (not attached ones), its bases' included, by name.</summary>
    public IReadOnlyList<XamlProperty> PropertiesOf(TypeSystem types, ClrType type) =>
        kept.GetValue(types, _ => new Answers()).Properties.GetOrAdd(type, t => ReadProperties(types, t));

    /// <summary>The attached properties a type declares for other elements (<c>Grid.Row</c>).</summary>
    public IReadOnlyList<XamlProperty> AttachedOf(TypeSystem types, ClrType owner) =>
        kept.GetValue(types, _ => new Answers()).Attached.GetOrAdd(owner, t => ReadAttached(types, t));

    public XamlProperty? Property(TypeSystem types, ClrType type, string name) =>
        PropertiesOf(types, type).FirstOrDefault(p => p.Name == name);

    /// <summary>An attached property by its owner, as the dialect's compiler looks one up.</summary>
    public virtual XamlProperty? Attached(TypeSystem types, ClrType owner, string name) =>
        AttachedOf(types, owner).FirstOrDefault(p => p.Name == name);

    /// <summary>The events a type's elements can name a handler for.</summary>
    public virtual IEnumerable<ClrMember> EventsOf(TypeSystem types, ClrType type) =>
        types.MembersOf(type).Where(m => m.Kind == MemberKind.Event && !m.IsStatic && m.IsPublic);

    /// <summary>The property an element's content sets, if its type has one.</summary>
    public abstract XamlProperty? ContentProperty(TypeSystem types, ClrType type);

    /// <summary>Whether XAML can make an element of a type.</summary>
    public virtual bool CanCreate(TypeSystem types, ClrType type) =>
        type.Arity == 0 && type.Kind is ClrTypeKind.Class or ClrTypeKind.Struct && type.HasDefaultConstructor && !type.IsNested;

    /// <summary>Whether a type is a markup extension, written in braces.</summary>
    public abstract bool IsMarkupExtension(TypeSystem types, ClrType type);

    /// <summary>The type a value of the given type names, when that is a framework's wrapper (a property's
    /// <c>StyledProperty&lt;T&gt;</c> holds a <c>T</c>).</summary>
    public virtual TypeRef ValueTypeOf(TypeRef type) => type;

    protected abstract IReadOnlyList<XamlProperty> ReadProperties(TypeSystem types, ClrType type);

    protected abstract IReadOnlyList<XamlProperty> ReadAttached(TypeSystem types, ClrType owner);

    /// <summary>Whether a type is a collection XAML adds its children to, so a read-only property of it is still set as a
    /// property element.</summary>
    public static bool IsCollection(TypeSystem types, TypeRef type)
    {
        if (type.IsArray)
        {
            return true;
        }
        string name = type.Name;
        if (name is "System.Collections.IList" or "System.Collections.IDictionary" or "System.Collections.Generic.IList`1" or "System.Collections.Generic.ICollection`1"
            or "System.Collections.Generic.IDictionary`2")
        {
            return true;
        }
        if (types.Resolve(type) is not { } resolved || resolved.Kind is ClrTypeKind.Enum or ClrTypeKind.Struct || resolved.FullName == "System.String")
        {
            return false;
        }
        return types.IsA(resolved, "System.Collections.IList") || types.IsA(resolved, "System.Collections.Generic.ICollection`1")
            || types.IsA(resolved, "System.Collections.IDictionary") || types.IsA(resolved, "System.Collections.Generic.IDictionary`2");
    }
}
