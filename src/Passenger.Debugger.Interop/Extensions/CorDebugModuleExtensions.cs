using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugModuleExtensions
{
	extension(ICorDebugModule instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryGetName(System.UInt32,System.UInt32@,System.Char[])" />
		public string Name
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetName(0u, out var pcchName, null));
				if (pcchName == 0)
				{
					return string.Empty;
				}
				for (int i = 0; i < 3; i++)
				{
					uint num = pcchName;
					char[] array;
					int num2;
					checked
					{
						array = new char[(int)num];
						Marshal.ThrowExceptionForHR(instance.TryGetName(num, out var pcchName2, array));
						if (pcchName2 > num)
						{
							pcchName = pcchName2;
							continue;
						}
						num2 = (int)pcchName2;
					}
					if (num2 > 0 && array[num2 - 1] == '\0')
					{
						num2--;
					}
					return new string(array, 0, num2);
				}
				throw new InvalidOperationException("Native buffer size did not stabilize.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryCreateBreakpoint(ICorDebugSharp.ICorDebugModuleBreakpoint@)" />
		public ICorDebugModuleBreakpoint CreateBreakpoint()
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateBreakpoint(out ICorDebugModuleBreakpoint ppBreakpoint));
			return ppBreakpoint;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryGetAssembly(ICorDebugSharp.ICorDebugAssembly@)" />
		public ICorDebugAssembly Assembly
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetAssembly(out ICorDebugAssembly ppAssembly));
				return ppAssembly;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryGetBaseAddress(ICorDebugSharp.CORDB_ADDRESS@)" />
		public CORDB_ADDRESS BaseAddress
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetBaseAddress(out var pAddress));
				return pAddress;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryGetClassFromToken(ICorDebugSharp.mdTypeDef,ICorDebugSharp.ICorDebugClass@)" />
		public ICorDebugClass GetClassFromToken(mdTypeDef typeDef)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetClassFromToken(typeDef, out ICorDebugClass ppClass));
			return ppClass;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryGetEditAndContinueSnapshot(ICorDebugSharp.ICorDebugEditAndContinueSnapshot@)" />
		public ICorDebugEditAndContinueSnapshot EditAndContinueSnapshot
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetEditAndContinueSnapshot(out ICorDebugEditAndContinueSnapshot ppEditAndContinueSnapshot));
				return ppEditAndContinueSnapshot;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryGetFunctionFromRVA(ICorDebugSharp.CORDB_ADDRESS,ICorDebugSharp.ICorDebugFunction@)" />
		public ICorDebugFunction GetFunctionFromRVA(CORDB_ADDRESS rva)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetFunctionFromRVA(rva, out ICorDebugFunction ppFunction));
			return ppFunction;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryGetFunctionFromToken(ICorDebugSharp.mdMethodDef,ICorDebugSharp.ICorDebugFunction@)" />
		public ICorDebugFunction GetFunctionFromToken(mdMethodDef methodDef)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetFunctionFromToken(methodDef, out ICorDebugFunction ppFunction));
			return ppFunction;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryGetGlobalVariableValue(ICorDebugSharp.mdFieldDef,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetGlobalVariableValue(mdFieldDef fieldDef)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetGlobalVariableValue(fieldDef, out ICorDebugValue ppValue));
			return ppValue;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryGetMetaDataInterface(System.Guid@,System.Object@)" />
		public object? GetMetaDataInterface(ref Guid riid)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetMetaDataInterface(ref riid, out object ppObj));
			return ppObj;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryGetProcess(ICorDebugSharp.ICorDebugProcess@)" />
		public ICorDebugProcess Process
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetProcess(out ICorDebugProcess ppProcess));
				return ppProcess;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryGetSize(System.UInt32@)" />
		public int Size
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetSize(out var pcBytes));
				return checked((int)pcBytes);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryGetToken(ICorDebugSharp.mdModule@)" />
		public mdModule Token
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetToken(out var pToken));
				return pToken;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryIsDynamic(System.Boolean@)" />
		public bool IsDynamic
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryIsDynamic(out var pDynamic));
				return pDynamic;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryIsInMemory(System.Boolean@)" />
		public bool IsInMemory
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryIsInMemory(out var pInMemory));
				return pInMemory;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryEnableClassLoadCallbacks(System.Boolean)" />
		public void EnableClassLoadCallbacks(bool bClassLoadCallbacks)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnableClassLoadCallbacks(bClassLoadCallbacks));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule.TryEnableJITDebugging(System.Boolean,System.Boolean)" />
		public void EnableJITDebugging(bool bTrackJITInfo, bool bAllowJitOpts)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnableJITDebugging(bTrackJITInfo, bAllowJitOpts));
		}
	}
}
