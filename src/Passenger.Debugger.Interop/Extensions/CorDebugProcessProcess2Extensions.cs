using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugProcessProcess2Extensions
{
	extension(ICorDebugProcess instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess2" />
		public bool SupportsProcess2
		{
			get
			{
				return instance is ICorDebugProcess2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess2" />
		public ICorDebugProcess2 Process2
		{
			get
			{
				return (instance as ICorDebugProcess2) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess2.TryClearUnmanagedBreakpoint(ICorDebugSharp.CORDB_ADDRESS)" />
		public int TryClearUnmanagedBreakpoint(CORDB_ADDRESS address)
		{
			return get_Process2(instance).TryClearUnmanagedBreakpoint(address);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess2.TryClearUnmanagedBreakpoint(ICorDebugSharp.CORDB_ADDRESS)" />
		public void ClearUnmanagedBreakpoint(CORDB_ADDRESS address)
		{
			get_Process2(instance).ClearUnmanagedBreakpoint(address);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess2.TryGetDesiredNGENCompilerFlags(System.UInt32@)" />
		public int TryGetDesiredNGENCompilerFlags(out uint pdwFlags)
		{
			return get_Process2(instance).TryGetDesiredNGENCompilerFlags(out pdwFlags);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess2.TryGetDesiredNGENCompilerFlags(System.UInt32@)" />
		public int DesiredNGENCompilerFlags
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Process2(instance).TryGetDesiredNGENCompilerFlags(out var pdwFlags));
				return checked((int)pdwFlags);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess2.TryGetReferenceValueFromGCHandle(System.UIntPtr,ICorDebugSharp.ICorDebugReferenceValue@)" />
		public int TryGetReferenceValueFromGCHandle(nuint handle, out ICorDebugReferenceValue pOutValue)
		{
			return get_Process2(instance).TryGetReferenceValueFromGCHandle(handle, out pOutValue);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess2.TryGetReferenceValueFromGCHandle(System.UIntPtr,ICorDebugSharp.ICorDebugReferenceValue@)" />
		public ICorDebugReferenceValue GetReferenceValueFromGCHandle(nuint handle)
		{
			return get_Process2(instance).GetReferenceValueFromGCHandle(handle);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess2.TryGetThreadForTaskID(System.UInt64,ICorDebugSharp.ICorDebugThread2@)" />
		public int TryGetThreadForTaskID(ulong taskid, out ICorDebugThread2 ppThread)
		{
			return get_Process2(instance).TryGetThreadForTaskID(taskid, out ppThread);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess2.TryGetThreadForTaskID(System.UInt64,ICorDebugSharp.ICorDebugThread2@)" />
		public ICorDebugThread2 GetThreadForTaskID(ulong taskid)
		{
			return get_Process2(instance).GetThreadForTaskID(taskid);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess2.TryGetVersion(ICorDebugSharp.COR_VERSION@)" />
		public int TryGetVersion(out COR_VERSION version)
		{
			return get_Process2(instance).TryGetVersion(out version);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess2.TryGetVersion(ICorDebugSharp.COR_VERSION@)" />
		public COR_VERSION Version
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Process2(instance).TryGetVersion(out var version));
				return version;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess2.TrySetDesiredNGENCompilerFlags(System.UInt32)" />
		public int TrySetDesiredNGENCompilerFlags(uint pdwFlags)
		{
			return get_Process2(instance).TrySetDesiredNGENCompilerFlags(pdwFlags);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess2.TrySetDesiredNGENCompilerFlags(System.UInt32)" />
		public void SetDesiredNGENCompilerFlags(int pdwFlags)
		{
			get_Process2(instance).SetDesiredNGENCompilerFlags(pdwFlags);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess2.TrySetUnmanagedBreakpoint(ICorDebugSharp.CORDB_ADDRESS,System.UInt32,System.Byte[],System.UInt32@)" />
		public int TrySetUnmanagedBreakpoint(CORDB_ADDRESS address, uint bufsize, byte[] buffer, out uint bufLen)
		{
			return get_Process2(instance).TrySetUnmanagedBreakpoint(address, bufsize, buffer, out bufLen);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess2.TrySetUnmanagedBreakpoint(ICorDebugSharp.CORDB_ADDRESS,System.UInt32,System.Byte[],System.UInt32@)" />
		public (byte[] buffer, int bufLen) SetUnmanagedBreakpoint(CORDB_ADDRESS address, int bufsize)
		{
			return get_Process2(instance).SetUnmanagedBreakpoint(address, bufsize);
		}
	}
}
