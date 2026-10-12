using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugDebugEventExtensions
{
	extension(ICorDebugDebugEvent instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugDebugEvent.TryGetEventKind(ICorDebugSharp.CorDebugDebugEventKind@)" />
		public CorDebugDebugEventKind EventKind
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetEventKind(out var pDebugEventKind));
				return pDebugEventKind;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugDebugEvent.TryGetThread(ICorDebugSharp.ICorDebugThread@)" />
		public ICorDebugThread Thread
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetThread(out ICorDebugThread ppThread));
				return ppThread;
			}
		}
	}
}
