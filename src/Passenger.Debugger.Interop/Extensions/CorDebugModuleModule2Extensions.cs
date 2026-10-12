using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugModuleModule2Extensions
{
	extension(ICorDebugModule instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugModule2" />
		public bool SupportsModule2
		{
			get
			{
				return instance is ICorDebugModule2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugModule2" />
		public ICorDebugModule2 Module2
		{
			get
			{
				return (instance as ICorDebugModule2) ?? throw new NotSupportedException("ICorDebugModule does not support ICorDebugModule2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule2.TryApplyChanges(System.UInt32,System.Byte[],System.UInt32,System.Byte[])" />
		public int TryApplyChanges(uint cbMetadata, byte[] pbMetadata, uint cbIL, byte[] pbIL)
		{
			return get_Module2(instance).TryApplyChanges(cbMetadata, pbMetadata, cbIL, pbIL);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule2.TryApplyChanges(System.UInt32,System.Byte[],System.UInt32,System.Byte[])" />
		public void ApplyChanges(int cbMetadata, byte[] pbMetadata, int cbIL, byte[] pbIL)
		{
			get_Module2(instance).ApplyChanges(cbMetadata, pbMetadata, cbIL, pbIL);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule2.TryGetJITCompilerFlags(ICorDebugSharp.CorDebugJITCompilerFlags@)" />
		public int TryGetJITCompilerFlags(out CorDebugJITCompilerFlags pdwFlags)
		{
			return get_Module2(instance).TryGetJITCompilerFlags(out pdwFlags);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule2.TryGetJITCompilerFlags(ICorDebugSharp.CorDebugJITCompilerFlags@)" />
		public CorDebugJITCompilerFlags JITCompilerFlags
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Module2(instance).TryGetJITCompilerFlags(out var pdwFlags));
				return pdwFlags;
			}
			set
			{
				Marshal.ThrowExceptionForHR(get_Module2(instance).TrySetJITCompilerFlags(value));
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule2.TryResolveAssembly(ICorDebugSharp.mdToken,ICorDebugSharp.ICorDebugAssembly@)" />
		public int TryResolveAssembly(mdToken tkAssemblyRef, out ICorDebugAssembly ppAssembly)
		{
			return get_Module2(instance).TryResolveAssembly(tkAssemblyRef, out ppAssembly);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule2.TryResolveAssembly(ICorDebugSharp.mdToken,ICorDebugSharp.ICorDebugAssembly@)" />
		public ICorDebugAssembly ResolveAssembly(mdToken tkAssemblyRef)
		{
			return get_Module2(instance).ResolveAssembly(tkAssemblyRef);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule2.TrySetJITCompilerFlags(ICorDebugSharp.CorDebugJITCompilerFlags)" />
		public int TrySetJITCompilerFlags(CorDebugJITCompilerFlags dwFlags)
		{
			return get_Module2(instance).TrySetJITCompilerFlags(dwFlags);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule2.TrySetJMCStatus(System.Boolean,System.UInt32,ICorDebugSharp.mdToken[])" />
		public int TrySetJMCStatus(bool bIsJustMyCode, uint cTokens, mdToken[] pTokens)
		{
			return get_Module2(instance).TrySetJMCStatus(bIsJustMyCode, cTokens, pTokens);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule2.TrySetJMCStatus(System.Boolean,System.UInt32,ICorDebugSharp.mdToken[])" />
		public void SetJMCStatus(bool bIsJustMyCode, int cTokens, mdToken[] pTokens)
		{
			get_Module2(instance).SetJMCStatus(bIsJustMyCode, cTokens, pTokens);
		}
	}
}
