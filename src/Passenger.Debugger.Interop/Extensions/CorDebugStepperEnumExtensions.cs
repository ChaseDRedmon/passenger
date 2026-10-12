using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugStepperEnumExtensions
{
	extension(ICorDebugStepperEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStepperEnum.TryNext(System.UInt32,ICorDebugSharp.ICorDebugStepper[],System.UInt32@)" />
		public (ICorDebugStepper[] steppers, int pceltFetched) Next(int celt)
		{
			ICorDebugStepper[] array = new ICorDebugStepper[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (steppers: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
