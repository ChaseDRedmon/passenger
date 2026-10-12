using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugCodeCode4Extensions
{
	extension(ICorDebugCode instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugCode4" />
		public bool SupportsCode4
		{
			get
			{
				return instance is ICorDebugCode4;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugCode4" />
		public ICorDebugCode4 Code4
		{
			get
			{
				return (instance as ICorDebugCode4) ?? throw new NotSupportedException("ICorDebugCode does not support ICorDebugCode4.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugCode4.TryEnumerateVariableHomes(ICorDebugSharp.ICorDebugVariableHomeEnum@)" />
		public int TryEnumerateVariableHomes(out ICorDebugVariableHomeEnum ppEnum)
		{
			return get_Code4(instance).TryEnumerateVariableHomes(out ppEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugCode4.TryEnumerateVariableHomes(ICorDebugSharp.ICorDebugVariableHomeEnum@)" />
		public IEnumerable<ICorDebugVariableHome> EnumerateVariableHomes()
		{
			return get_Code4(instance).EnumerateVariableHomes();
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugCode4.TryEnumerateVariableHomes(ICorDebugSharp.ICorDebugVariableHomeEnum@)" />
		public ICorDebugVariableHome[] VariableHomes
		{
			get
			{
				return get_Code4(instance).EnumerateVariableHomes().ToArray();
			}
		}
	}
}
