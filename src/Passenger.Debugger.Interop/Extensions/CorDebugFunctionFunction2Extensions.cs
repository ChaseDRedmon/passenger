using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugFunctionFunction2Extensions
{
	extension(ICorDebugFunction instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugFunction2" />
		public bool SupportsFunction2
		{
			get
			{
				return instance is ICorDebugFunction2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugFunction2" />
		public ICorDebugFunction2 Function2
		{
			get
			{
				return (instance as ICorDebugFunction2) ?? throw new NotSupportedException("ICorDebugFunction does not support ICorDebugFunction2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction2.TryEnumerateNativeCode(ICorDebugSharp.ICorDebugCodeEnum@)" />
		public int TryEnumerateNativeCode(out ICorDebugCodeEnum ppCodeEnum)
		{
			return get_Function2(instance).TryEnumerateNativeCode(out ppCodeEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction2.TryEnumerateNativeCode(ICorDebugSharp.ICorDebugCodeEnum@)" />
		public IEnumerable<ICorDebugCode> EnumerateNativeCode()
		{
			return get_Function2(instance).EnumerateNativeCode();
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction2.TryEnumerateNativeCode(ICorDebugSharp.ICorDebugCodeEnum@)" />
		public ICorDebugCode[] NativeCode
		{
			get
			{
				return get_Function2(instance).EnumerateNativeCode().ToArray();
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction2.TryGetJMCStatus(System.Boolean@)" />
		public int TryGetJMCStatus(out bool pbIsJustMyCode)
		{
			return get_Function2(instance).TryGetJMCStatus(out pbIsJustMyCode);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction2.TryGetJMCStatus(System.Boolean@)" />
		public bool JMCStatus
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Function2(instance).TryGetJMCStatus(out var pbIsJustMyCode));
				return pbIsJustMyCode;
			}
			set
			{
				Marshal.ThrowExceptionForHR(get_Function2(instance).TrySetJMCStatus(value));
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction2.TryGetVersionNumber(System.UInt32@)" />
		public int TryGetVersionNumber(out uint pnVersion)
		{
			return get_Function2(instance).TryGetVersionNumber(out pnVersion);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction2.TryGetVersionNumber(System.UInt32@)" />
		public int VersionNumber
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Function2(instance).TryGetVersionNumber(out var pnVersion));
				return checked((int)pnVersion);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction2.TrySetJMCStatus(System.Boolean)" />
		public int TrySetJMCStatus(bool bIsJustMyCode)
		{
			return get_Function2(instance).TrySetJMCStatus(bIsJustMyCode);
		}
	}
}
