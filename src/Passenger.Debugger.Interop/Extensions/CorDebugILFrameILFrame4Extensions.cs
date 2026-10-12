using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugILFrameILFrame4Extensions
{
	extension(ICorDebugILFrame instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugILFrame4" />
		public bool SupportsILFrame4
		{
			get
			{
				return instance is ICorDebugILFrame4;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugILFrame4" />
		public ICorDebugILFrame4 ILFrame4
		{
			get
			{
				return (instance as ICorDebugILFrame4) ?? throw new NotSupportedException("ICorDebugILFrame does not support ICorDebugILFrame4.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame4.TryEnumerateLocalVariablesEx(ICorDebugSharp.ILCodeKind,ICorDebugSharp.ICorDebugValueEnum@)" />
		public int TryEnumerateLocalVariablesEx(ILCodeKind flags, out ICorDebugValueEnum ppValueEnum)
		{
			return get_ILFrame4(instance).TryEnumerateLocalVariablesEx(flags, out ppValueEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame4.TryEnumerateLocalVariablesEx(ICorDebugSharp.ILCodeKind,ICorDebugSharp.ICorDebugValueEnum@)" />
		public IEnumerable<ICorDebugValue> EnumerateLocalVariablesEx(ILCodeKind flags)
		{
			return get_ILFrame4(instance).EnumerateLocalVariablesEx(flags);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame4.TryGetCodeEx(ICorDebugSharp.ILCodeKind,ICorDebugSharp.ICorDebugCode@)" />
		public int TryGetCodeEx(ILCodeKind flags, out ICorDebugCode ppCode)
		{
			return get_ILFrame4(instance).TryGetCodeEx(flags, out ppCode);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame4.TryGetCodeEx(ICorDebugSharp.ILCodeKind,ICorDebugSharp.ICorDebugCode@)" />
		public ICorDebugCode GetCodeEx(ILCodeKind flags)
		{
			return get_ILFrame4(instance).GetCodeEx(flags);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame4.TryGetLocalVariableEx(ICorDebugSharp.ILCodeKind,System.UInt32,ICorDebugSharp.ICorDebugValue@)" />
		public int TryGetLocalVariableEx(ILCodeKind flags, uint dwIndex, out ICorDebugValue ppValue)
		{
			return get_ILFrame4(instance).TryGetLocalVariableEx(flags, dwIndex, out ppValue);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame4.TryGetLocalVariableEx(ICorDebugSharp.ILCodeKind,System.UInt32,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetLocalVariableEx(ILCodeKind flags, int dwIndex)
		{
			return get_ILFrame4(instance).GetLocalVariableEx(flags, dwIndex);
		}
	}
}
