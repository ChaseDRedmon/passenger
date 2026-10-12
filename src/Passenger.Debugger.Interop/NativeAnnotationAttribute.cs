using System;
using System.Diagnostics;

namespace Passenger.Debugger.Interop;

/// <summary>Defines the annotation found in a native declaration.</summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = true, Inherited = false)]
[Conditional("DEBUG")]
internal sealed class NativeAnnotationAttribute : Attribute
{
    private readonly string _annotation;

    /// <summary>Gets the annotation that was used in the native declaration.</summary>
    public string Annotation => _annotation;

    /// <summary>Initializes a new instance of the <see cref="T:Passenger.Debugger.Interop.NativeAnnotationAttribute" /> class.</summary>
    /// <param name="annotation">The annotation that was used in the native declaration.</param>
    public NativeAnnotationAttribute(string annotation)
    {
        _annotation = annotation;
    }
}
