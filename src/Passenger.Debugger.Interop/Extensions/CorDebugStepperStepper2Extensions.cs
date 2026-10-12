using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugStepperStepper2Extensions
{
	extension(ICorDebugStepper instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugStepper2" />
		public bool SupportsStepper2
		{
			get
			{
				return instance is ICorDebugStepper2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugStepper2" />
		public ICorDebugStepper2 Stepper2
		{
			get
			{
				return (instance as ICorDebugStepper2) ?? throw new NotSupportedException("ICorDebugStepper does not support ICorDebugStepper2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStepper2.TrySetJMC(System.Boolean)" />
		public int TrySetJMC(bool fIsJMCStepper)
		{
			return get_Stepper2(instance).TrySetJMC(fIsJMCStepper);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStepper2.TrySetJMC(System.Boolean)" />
		public void SetJMC(bool fIsJMCStepper)
		{
			get_Stepper2(instance).SetJMC(fIsJMCStepper);
		}
	}
}
