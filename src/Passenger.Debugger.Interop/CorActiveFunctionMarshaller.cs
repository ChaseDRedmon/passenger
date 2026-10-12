using System;
using System.Runtime.InteropServices.Marshalling;

namespace ICorDebugSharp;

[CustomMarshaller(typeof(COR_ACTIVE_FUNCTION), MarshalMode.Default, typeof(CorActiveFunctionMarshaller))]
internal static class CorActiveFunctionMarshaller
{
	internal struct Native
	{
		public nint pAppDomain;

		public nint pModule;

		public nint pFunction;

		public uint ilOffset;

		public uint flags;
	}

	public unsafe static Native ConvertToUnmanaged(in COR_ACTIVE_FUNCTION managed)
	{
		return new Native
		{
			pAppDomain = (nint)ComInterfaceMarshaller<ICorDebugAppDomain>.ConvertToUnmanaged(managed.pAppDomain),
			pModule = (nint)ComInterfaceMarshaller<ICorDebugModule>.ConvertToUnmanaged(managed.pModule),
			pFunction = (nint)ComInterfaceMarshaller<ICorDebugFunction2>.ConvertToUnmanaged(managed.pFunction),
			ilOffset = managed.ilOffset,
			flags = managed.flags
		};
	}

	public unsafe static COR_ACTIVE_FUNCTION ConvertToManaged(in Native unmanaged)
	{
		return new COR_ACTIVE_FUNCTION
		{
			pAppDomain = (ComInterfaceMarshaller<ICorDebugAppDomain>.ConvertToManaged((void*)unmanaged.pAppDomain) ?? throw new InvalidOperationException("Native COR_ACTIVE_FUNCTION.pAppDomain was null.")),
			pModule = (ComInterfaceMarshaller<ICorDebugModule>.ConvertToManaged((void*)unmanaged.pModule) ?? throw new InvalidOperationException("Native COR_ACTIVE_FUNCTION.pModule was null.")),
			pFunction = (ComInterfaceMarshaller<ICorDebugFunction2>.ConvertToManaged((void*)unmanaged.pFunction) ?? throw new InvalidOperationException("Native COR_ACTIVE_FUNCTION.pFunction was null.")),
			ilOffset = unmanaged.ilOffset,
			flags = unmanaged.flags
		};
	}

	public unsafe static void Free(in Native unmanaged)
	{
		ComInterfaceMarshaller<ICorDebugAppDomain>.Free((void*)unmanaged.pAppDomain);
		ComInterfaceMarshaller<ICorDebugModule>.Free((void*)unmanaged.pModule);
		ComInterfaceMarshaller<ICorDebugFunction2>.Free((void*)unmanaged.pFunction);
	}
}
