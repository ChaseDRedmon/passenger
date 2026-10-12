namespace ICorDebugSharp;

/// <summary>
/// <para>Specifies options for a PInvoke call.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corpinvokemap-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorPinvokeMap
{
	/// <summary>
	/// <para>Use each member name as specified.</para>
	/// </summary>
	pmNoMangle = 1,
	/// <summary>
	/// <para>Reserved.</para>
	/// </summary>
	pmCharSetMask = 6,
	/// <summary>
	/// <para>Reserved.</para>
	/// </summary>
	pmCharSetNotSpec = 0,
	/// <summary>
	/// <para>Marshal strings as multiple-byte character strings.</para>
	/// </summary>
	pmCharSetAnsi = 2,
	/// <summary>
	/// <para>Marshal strings as Unicode 2-byte characters.</para>
	/// </summary>
	pmCharSetUnicode = 4,
	/// <summary>
	/// <para>Automatically marshal strings appropriately for the target operating system. The default is Unicode on Windows.</para>
	/// </summary>
	pmCharSetAuto = pmCharSetMask,
	/// <summary>
	/// <para>Reserved.</para>
	/// </summary>
	pmBestFitUseAssem = pmCharSetNotSpec,
	/// <summary>
	/// <para>Perform best-fit mapping of Unicode characters that lack an exact match in the ANSI character set.</para>
	/// </summary>
	pmBestFitEnabled = 16,
	/// <summary>
	/// <para>Do not perform best-fit mapping of Unicode characters. In this case, all unmappable characters will be replaced by a ‘?’.</para>
	/// </summary>
	pmBestFitDisabled = 32,
	/// <summary>
	/// <para>Reserved.</para>
	/// </summary>
	pmBestFitMask = 48,
	/// <summary>
	/// <para>Reserved.</para>
	/// </summary>
	pmThrowOnUnmappableCharUseAssem = pmCharSetNotSpec,
	/// <summary>
	/// <para>Throw an exception when the interop marshaller encounters an unmappable character.</para>
	/// </summary>
	pmThrowOnUnmappableCharEnabled = 4096,
	/// <summary>
	/// <para>Do not throw an exception when the interop marshaller encounters an unmappable character.</para>
	/// </summary>
	pmThrowOnUnmappableCharDisabled = 8192,
	/// <summary>
	/// <para>Reserved</para>
	/// </summary>
	pmThrowOnUnmappableCharMask = 12288,
	/// <summary>
	/// <para>Allow the callee to call the Win32 <c>SetLastError</c> function before returning from the attributed method.</para>
	/// </summary>
	pmSupportsLastError = 64,
	/// <summary>
	/// <para>Reserved</para>
	/// </summary>
	pmCallConvMask = 1792,
	/// <summary>
	/// <para>Use the default platform calling convention. For example, on Windows the default is <c>StdCall</c> and on Windows CE .NET it is <c>Cdecl</c>.</para>
	/// </summary>
	pmCallConvWinapi = 256,
	/// <summary>
	/// <para>Use the <c>Cdecl</c> calling convention. In this case, the caller cleans the stack. This enables calling functions with <c>varargs</c> (that is, functions that accept a variable number of parameters).</para>
	/// </summary>
	pmCallConvCdecl = 512,
	/// <summary>
	/// <para>Use the <c>StdCall</c> calling convention. In this case, the callee cleans the stack. This is the default convention for calling unmanaged functions with platform invoke.</para>
	/// </summary>
	pmCallConvStdcall = 768,
	/// <summary>
	/// <para>Use the <c>ThisCall</c> calling convention. In this case, the first parameter is the <c>this</c> pointer and is stored in register ECX. Other parameters are pushed on the stack. The <c>ThisCall</c> calling convention is used to call methods on classes exported from an unmanaged DLL.</para>
	/// </summary>
	pmCallConvThiscall = 1024,
	/// <summary>
	/// <para>Reserved.</para>
	/// </summary>
	pmCallConvFastcall = 1280,
	/// <summary>
	/// <para>Reserved.</para>
	/// </summary>
	pmMaxValue = 65535
}
