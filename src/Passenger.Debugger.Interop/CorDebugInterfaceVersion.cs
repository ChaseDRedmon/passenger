namespace ICorDebugSharp;

/// <summary>
/// <para>Specifies an interface, a version of .NET, or a version of .NET in which an interface was introduced.</para>
/// </summary>
/// <remarks>
/// <para>A debugger can use the <c>CorDebugInterfaceVersion</c> enumeration in the <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/createdebugginginterfacefromversion-function">CreateDebuggingInterfaceFromVersion</see> function to specify the highest version of .NET that the debugger supports.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebuginterfaceversion-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorDebugInterfaceVersion
{
	/// <summary>
	/// <para>The version of .NET Framework is invalid.</para>
	/// </summary>
	CorDebugInvalidVersion = 0,
	/// <summary>
	/// <para>The version of .NET Framework, including all its service packs, is 1.0.</para>
	/// </summary>
	CorDebugVersion_1_0 = 1,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugManagedCallback">ICorDebugManagedCallback</see></para>
	/// </summary>
	ver_ICorDebugManagedCallback = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugUnmanagedCallback">ICorDebugUnmanagedCallback</see></para>
	/// </summary>
	ver_ICorDebugUnmanagedCallback = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebug">ICorDebug</see></para>
	/// </summary>
	ver_ICorDebug = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugController">ICorDebugController</see></para>
	/// </summary>
	ver_ICorDebugController = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugAppDomain">ICorDebugAppDomain</see></para>
	/// </summary>
	ver_ICorDebugAppDomain = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugAssembly">ICorDebugAssembly</see></para>
	/// </summary>
	ver_ICorDebugAssembly = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugProcess">ICorDebugProcess</see></para>
	/// </summary>
	ver_ICorDebugProcess = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugBreakpoint">ICorDebugBreakpoint</see></para>
	/// </summary>
	ver_ICorDebugBreakpoint = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugFunctionBreakpoint">ICorDebugFunctionBreakpoint</see></para>
	/// </summary>
	ver_ICorDebugFunctionBreakpoint = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugModuleBreakpoint">ICorDebugModuleBreakpoint</see></para>
	/// </summary>
	ver_ICorDebugModuleBreakpoint = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugValueBreakpoint">ICorDebugValueBreakpoint</see></para>
	/// </summary>
	ver_ICorDebugValueBreakpoint = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugStepper">ICorDebugStepper</see></para>
	/// </summary>
	ver_ICorDebugStepper = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugRegisterSet">ICorDebugRegisterSet</see></para>
	/// </summary>
	ver_ICorDebugRegisterSet = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugThread">ICorDebugThread</see></para>
	/// </summary>
	ver_ICorDebugThread = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugChain">ICorDebugChain</see></para>
	/// </summary>
	ver_ICorDebugChain = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugFrame">ICorDebugFrame</see></para>
	/// </summary>
	ver_ICorDebugFrame = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugILFrame">ICorDebugILFrame</see></para>
	/// </summary>
	ver_ICorDebugILFrame = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugNativeFrame">ICorDebugNativeFrame</see></para>
	/// </summary>
	ver_ICorDebugNativeFrame = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugModule">ICorDebugModule</see></para>
	/// </summary>
	ver_ICorDebugModule = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugFunction">ICorDebugFunction</see></para>
	/// </summary>
	ver_ICorDebugFunction = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugCode">ICorDebugCode</see></para>
	/// </summary>
	ver_ICorDebugCode = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugClass">ICorDebugClass</see></para>
	/// </summary>
	ver_ICorDebugClass = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugEval">ICorDebugEval</see></para>
	/// </summary>
	ver_ICorDebugEval = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugValue">ICorDebugValue</see></para>
	/// </summary>
	ver_ICorDebugValue = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugGenericValue">ICorDebugGenericValue</see></para>
	/// </summary>
	ver_ICorDebugGenericValue = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugReferenceValue">ICorDebugReferenceValue</see></para>
	/// </summary>
	ver_ICorDebugReferenceValue = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugHeapValue">ICorDebugHeapValue</see></para>
	/// </summary>
	ver_ICorDebugHeapValue = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugObjectValue">ICorDebugObjectValue</see></para>
	/// </summary>
	ver_ICorDebugObjectValue = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugBoxValue">ICorDebugBoxValue</see></para>
	/// </summary>
	ver_ICorDebugBoxValue = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugStringValue">ICorDebugStringValue</see></para>
	/// </summary>
	ver_ICorDebugStringValue = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugArrayValue">ICorDebugArrayValue</see></para>
	/// </summary>
	ver_ICorDebugArrayValue = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugContext">ICorDebugContext</see></para>
	/// </summary>
	ver_ICorDebugContext = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugEnum">ICorDebugEnum</see></para>
	/// </summary>
	ver_ICorDebugEnum = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugObjectEnum">ICorDebugObjectEnum</see></para>
	/// </summary>
	ver_ICorDebugObjectEnum = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugBreakpointEnum">ICorDebugBreakpointEnum</see></para>
	/// </summary>
	ver_ICorDebugBreakpointEnum = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugStepperEnum">ICorDebugStepperEnum</see></para>
	/// </summary>
	ver_ICorDebugStepperEnum = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugProcessEnum">ICorDebugProcessEnum</see></para>
	/// </summary>
	ver_ICorDebugProcessEnum = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugThreadEnum">ICorDebugThreadEnum</see></para>
	/// </summary>
	ver_ICorDebugThreadEnum = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugFrameEnum">ICorDebugFrameEnum</see></para>
	/// </summary>
	ver_ICorDebugFrameEnum = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugChainEnum">ICorDebugChainEnum</see></para>
	/// </summary>
	ver_ICorDebugChainEnum = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugModuleEnum">ICorDebugModuleEnum</see></para>
	/// </summary>
	ver_ICorDebugModuleEnum = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugValueEnum">ICorDebugValueEnum</see></para>
	/// </summary>
	ver_ICorDebugValueEnum = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugCodeEnum">ICorDebugCodeEnum</see></para>
	/// </summary>
	ver_ICorDebugCodeEnum = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugTypeEnum">ICorDebugTypeEnum</see></para>
	/// </summary>
	ver_ICorDebugTypeEnum = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugErrorInfoEnum">ICorDebugErrorInfoEnum</see></para>
	/// </summary>
	ver_ICorDebugErrorInfoEnum = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugAppDomainEnum">ICorDebugAppDomainEnum</see></para>
	/// </summary>
	ver_ICorDebugAppDomainEnum = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugAssemblyEnum">ICorDebugAssemblyEnum</see></para>
	/// </summary>
	ver_ICorDebugAssemblyEnum = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugEditAndContinueErrorInfo">ICorDebugEditAndContinueErrorInfo</see></para>
	/// </summary>
	ver_ICorDebugEditAndContinueErrorInfo = CorDebugVersion_1_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugEditAndContinueSnapshot">ICorDebugEditAndContinueSnapshot</see></para>
	/// </summary>
	ver_ICorDebugEditAndContinueSnapshot = CorDebugVersion_1_0,
	/// <summary>
	/// <para>The version of .NET Framework, including all service packs, is 1.1.</para>
	/// </summary>
	CorDebugVersion_1_1 = 2,
	/// <summary>
	/// <para>The version of .NET Framework, including all service packs, is 2.0.</para>
	/// </summary>
	CorDebugVersion_2_0 = 3,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugManagedCallback2">ICorDebugManagedCallback2</see></para>
	/// </summary>
	ver_ICorDebugManagedCallback2 = CorDebugVersion_2_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugAppDomain2">ICorDebugAppDomain2</see></para>
	/// </summary>
	ver_ICorDebugAppDomain2 = CorDebugVersion_2_0,
	ver_ICorDebugAssembly2 = CorDebugVersion_2_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugProcess2">ICorDebugProcess2</see></para>
	/// </summary>
	ver_ICorDebugProcess2 = CorDebugVersion_2_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugStepper2">ICorDebugStepper2</see></para>
	/// </summary>
	ver_ICorDebugStepper2 = CorDebugVersion_2_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugRegisterSet2">ICorDebugRegisterSet2</see></para>
	/// </summary>
	ver_ICorDebugRegisterSet2 = CorDebugVersion_2_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugThread2">ICorDebugThread2</see></para>
	/// </summary>
	ver_ICorDebugThread2 = CorDebugVersion_2_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugILFrame2">ICorDebugILFrame2</see></para>
	/// </summary>
	ver_ICorDebugILFrame2 = CorDebugVersion_2_0,
	ver_ICorDebugInternalFrame = CorDebugVersion_2_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugModule2">ICorDebugModule2</see></para>
	/// </summary>
	ver_ICorDebugModule2 = CorDebugVersion_2_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugFunction2">ICorDebugFunction2</see></para>
	/// </summary>
	ver_ICorDebugFunction2 = CorDebugVersion_2_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugCode2">ICorDebugCode2</see></para>
	/// </summary>
	ver_ICorDebugCode2 = CorDebugVersion_2_0,
	/// <summary>
	/// <para>"ICorDebugClass2"</para>
	/// </summary>
	ver_ICorDebugClass2 = CorDebugVersion_2_0,
	/// <summary>
	/// <para>"ICorDebugValue2"</para>
	/// </summary>
	ver_ICorDebugValue2 = CorDebugVersion_2_0,
	/// <summary>
	/// <para>The "ICorDebugEval2".</para>
	/// </summary>
	ver_ICorDebugEval2 = CorDebugVersion_2_0,
	/// <summary>
	/// <para>"ICorDebugObjectValue2"</para>
	/// </summary>
	ver_ICorDebugObjectValue2 = CorDebugVersion_2_0,
	/// <summary>
	/// <para>The version of .NET Framework, including all service packs, is 4.</para>
	/// </summary>
	CorDebugVersion_4_0 = 4,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugThread3">ICorDebugThread3</see></para>
	/// </summary>
	ver_ICorDebugThread3 = CorDebugVersion_4_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugThread4">ICorDebugThread4</see></para>
	/// </summary>
	ver_ICorDebugThread4 = CorDebugVersion_4_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugStackWalk">ICorDebugStackWalk</see></para>
	/// </summary>
	ver_ICorDebugStackWalk = CorDebugVersion_4_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugNativeFrame2">ICorDebugNativeFrame2</see></para>
	/// </summary>
	ver_ICorDebugNativeFrame2 = CorDebugVersion_4_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugInternalFrame2">ICorDebugInternalFrame2</see></para>
	/// </summary>
	ver_ICorDebugInternalFrame2 = CorDebugVersion_4_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugRuntimeUnwindableFrame">ICorDebugRuntimeUnwindableFrame</see></para>
	/// </summary>
	ver_ICorDebugRuntimeUnwindableFrame = CorDebugVersion_4_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugHeapValue3">ICorDebugHeapValue3 Interface</see></para>
	/// </summary>
	ver_ICorDebugHeapValue3 = CorDebugVersion_4_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugBlockingObjectEnum">ICorDebugBlockingObjectEnum Interface</see></para>
	/// </summary>
	ver_ICorDebugBlockingObjectEnum = CorDebugVersion_4_0,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugValue3">ICorDebugValue3</see></para>
	/// </summary>
	ver_ICorDebugValue3 = CorDebugVersion_4_0,
	/// <summary>
	/// <para>The version of .NET Framework, including all service packs, is 4.5.</para>
	/// </summary>
	CorDebugVersion_4_5 = 5,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugComObjectValue">ICorDebugComObjectValue</see></para>
	/// </summary>
	ver_ICorDebugComObjectValue = CorDebugVersion_4_5,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugAppDomain3">ICorDebugAppDomain3</see></para>
	/// </summary>
	ver_ICorDebugAppDomain3 = CorDebugVersion_4_5,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugCode3">ICorDebugCode3</see></para>
	/// </summary>
	ver_ICorDebugCode3 = CorDebugVersion_4_5,
	/// <summary>
	/// <para><see cref="T:ICorDebugSharp.ICorDebugILFrame3">ICorDebugILFrame3</see></para>
	/// </summary>
	ver_ICorDebugILFrame3 = CorDebugVersion_4_5,
	/// <summary>
	/// <para>The version of .NET is the latest version.</para>
	/// </summary>
	CorDebugLatestVersion = CorDebugVersion_4_5
}
