using System;
using System.Diagnostics;

namespace Passenger.Debugger.Interop;

/// <summary>Defines the base type of a struct as it was in the native signature.</summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
[Conditional("DEBUG")]
internal sealed class NativeInheritanceAttribute : Attribute
{
    private readonly string _name;

    /// <summary>Gets the name of the base type that was inherited from in the native signature.</summary>
    public string Name => _name;

    /// <summary>Initializes a new instance of the <see cref="T:Passenger.Debugger.Interop.NativeInheritanceAttribute" /> class.</summary>
    /// <param name="name">The name of the base type that was inherited from in the native signature.</param>
    public NativeInheritanceAttribute(string name)
    {
        _name = name;
    }
}
