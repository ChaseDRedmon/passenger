using System;
using System.Diagnostics;

namespace Passenger.Debugger.Interop;

/// <summary>Specifies that the given method sets the last system error and it can be retrieved via <see cref="M:System.Runtime.InteropServices.Marshal.GetLastSystemError" />.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
[Conditional("DEBUG")]
internal sealed class SetsLastSystemErrorAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="T:Passenger.Debugger.Interop.SetsLastSystemErrorAttribute" /> class.</summary>
    public SetsLastSystemErrorAttribute()
    {
    }
}
