using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugProcessProcess12Extensions
{
	extension(ICorDebugProcess instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess12" />
		public bool SupportsProcess12
		{
			get
			{
				return instance is ICorDebugProcess12;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess12" />
		public ICorDebugProcess12 Process12
		{
			get
			{
				return (instance as ICorDebugProcess12) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess12.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess12.TryGetAsyncStack(ICorDebugSharp.CORDB_ADDRESS,ICorDebugSharp.ICorDebugStackWalk@)" />
		public int TryGetAsyncStack(CORDB_ADDRESS continuationAddress, out ICorDebugStackWalk ppStackWalk)
		{
			return get_Process12(instance).TryGetAsyncStack(continuationAddress, out ppStackWalk);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess12.TryGetAsyncStack(ICorDebugSharp.CORDB_ADDRESS,ICorDebugSharp.ICorDebugStackWalk@)" />
		public ICorDebugStackWalk GetAsyncStack(CORDB_ADDRESS continuationAddress)
		{
			return get_Process12(instance).GetAsyncStack(continuationAddress);
		}
	}
}
