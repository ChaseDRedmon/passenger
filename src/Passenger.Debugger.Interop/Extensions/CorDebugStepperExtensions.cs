using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugStepperExtensions
{
	extension(ICorDebugStepper instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStepper.TryIsActive(System.Boolean@)" />
		public bool IsActive
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryIsActive(out var pbActive));
				return pbActive;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStepper.TryDeactivate" />
		public void Deactivate()
		{
			Marshal.ThrowExceptionForHR(instance.TryDeactivate());
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStepper.TrySetInterceptMask(ICorDebugSharp.CorDebugIntercept)" />
		public void SetInterceptMask(CorDebugIntercept mask)
		{
			Marshal.ThrowExceptionForHR(instance.TrySetInterceptMask(mask));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStepper.TrySetRangeIL(System.Boolean)" />
		public void SetRangeIL(bool bIL)
		{
			Marshal.ThrowExceptionForHR(instance.TrySetRangeIL(bIL));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStepper.TrySetUnmappedStopMask(ICorDebugSharp.CorDebugUnmappedStop)" />
		public void SetUnmappedStopMask(CorDebugUnmappedStop mask)
		{
			Marshal.ThrowExceptionForHR(instance.TrySetUnmappedStopMask(mask));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStepper.TryStep(System.Boolean)" />
		public void Step(bool bStepIn)
		{
			Marshal.ThrowExceptionForHR(instance.TryStep(bStepIn));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStepper.TryStepOut" />
		public void StepOut()
		{
			Marshal.ThrowExceptionForHR(instance.TryStepOut());
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStepper.TryStepRange(System.Boolean,ICorDebugSharp.COR_DEBUG_STEP_RANGE[],System.UInt32)" />
		public void StepRange(bool bStepIn, COR_DEBUG_STEP_RANGE[] ranges, int cRangeCount)
		{
			Marshal.ThrowExceptionForHR(instance.TryStepRange(bStepIn, ranges, checked((uint)cRangeCount)));
		}
	}
}
