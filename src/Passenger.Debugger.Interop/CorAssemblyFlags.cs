namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Contains values that describe the metadata applied to an assembly compilation.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corassemblyflags-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorAssemblyFlags
{
    /// <summary>
    /// <para>Indicates that the assembly reference holds the full, unhashed public key.</para>
    /// </summary>
    afPublicKey = 1,
    /// <summary>
    /// <para>Indicates that the processor architecture is unspecified.</para>
    /// </summary>
    afPA_None = 0,
    /// <summary>
    /// <para>Indicates that the processor architecture is neutral (PE32).</para>
    /// </summary>
    afPA_MSIL = 16,
    /// <summary>
    /// <para>Indicates that the processor architecture is x86 (PE32).</para>
    /// </summary>
    afPA_x86 = 32,
    /// <summary>
    /// <para>Indicates that the processor architecture is Itanium (PE32+).</para>
    /// </summary>
    afPA_IA64 = 48,
    /// <summary>
    /// <para>Indicates that the processor architecture is x86-64 (PE32+).</para>
    /// </summary>
    afPA_AMD64 = 64,
    /// <summary>
    /// <para>Indicates that the processor architecture is ARM (PE32).</para>
    /// </summary>
    afPA_ARM = 80,
    /// <summary>
    /// <para>Indicates that the processor architecture is ARM64 (PE32+).</para>
    /// </summary>
    afPA_ARM64 = 96,
    /// <summary>
    /// <para>Indicates that the assembly is a reference assembly; that is, it applies to any architecture but cannot run on any architecture. Thus, the flag is the same as <c>afPA_Mask</c>.</para>
    /// </summary>
    afPA_NoPlatform = 112,
    /// <summary>
    /// <para>Indicates that the processor architecture flags should be propagated to the <c>AssemblyRef</c> record.</para>
    /// </summary>
    afPA_Specified = 128,
    /// <summary>
    /// <para>A mask that describes the processor architecture.</para>
    /// </summary>
    afPA_Mask = afPA_NoPlatform,
    /// <summary>
    /// <para>Specifies that the processor architecture description is included.</para>
    /// </summary>
    afPA_FullMask = 240,
    /// <summary>
    /// <para>Indicates a shift count in the processor architecture flags to and from the index.</para>
    /// </summary>
    afPA_Shift = 4,
    /// <summary>
    /// <para>Indicates the corresponding value from the  of the .</para>
    /// </summary>
    afEnableJITcompileTracking = 32768,
    /// <summary>
    /// <para>Indicates the corresponding value from the  of the .</para>
    /// </summary>
    afDisableJITcompileOptimizer = 16384,
    afDebuggableAttributeMask = 49152,
    /// <summary>
    /// <para>Indicates that the assembly can be retargeted at runtime to an assembly from a different publisher.</para>
    /// </summary>
    afRetargetable = 256,
    /// <summary>
    /// <para>Indicates the default content type.</para>
    /// </summary>
    afContentType_Default = afPA_None,
    /// <summary>
    /// <para>Indicates the Windows Runtime content type.</para>
    /// </summary>
    afContentType_WindowsRuntime = 512,
    /// <summary>
    /// <para>A mask that describes the content type.</para>
    /// </summary>
    afContentType_Mask = 3584
}
