using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class MetaDataImport2Extensions
{
	extension(IMetaDataImport2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport2.TryEnumGenericParams(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdToken,ICorDebugSharp.mdGenericParam@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdGenericParam> EnumGenericParams(mdToken tk)
		{
			return EnumerateGenericParamsCore(instance, tk);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport2.TryEnumGenericParamConstraints(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdGenericParam,ICorDebugSharp.mdGenericParamConstraint@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdGenericParamConstraint> EnumGenericParamConstraints(mdGenericParam tk)
		{
			return EnumerateGenericParamConstraintsCore(instance, tk);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport2.TryEnumMethodSpecs(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdToken,ICorDebugSharp.mdMethodSpec@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdMethodSpec> EnumMethodSpecs(mdToken tk)
		{
			return EnumerateMethodSpecsCore(instance, tk);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport2.TryGetVersionString(System.Char[],System.UInt32,System.UInt32@)" />
		public string VersionString
		{
			get
			{
				return QueryGetVersionString(instance);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport2.TryGetGenericParamProps(ICorDebugSharp.mdGenericParam,System.UInt32@,ICorDebugSharp.CorGenericParamAttr@,ICorDebugSharp.mdToken@,System.UInt32@,System.Char[],System.UInt32,System.UInt32@)" />
		public (string wzname, int pulParamSeq, CorGenericParamAttr pdwParamFlags, mdToken ptOwner, int reserved) GetGenericParamProps(mdGenericParam gp)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetGenericParamProps(gp, out var pulParamSeq, out var pdwParamFlags, out var ptOwner, out var reserved, null, 0u, out var pchName));
			checked
			{
				if (pchName == 0)
				{
					return (wzname: string.Empty, pulParamSeq: (int)pulParamSeq, pdwParamFlags: pdwParamFlags, ptOwner: ptOwner, reserved: (int)reserved);
				}
				for (int i = 0; i < 3; i = unchecked(i + 1))
				{
					uint num = pchName;
					char[] array = new char[(int)num];
					Marshal.ThrowExceptionForHR(instance.TryGetGenericParamProps(gp, out var pulParamSeq2, out var pdwParamFlags2, out var ptOwner2, out var reserved2, array, num, out var pchName2));
					if (pchName2 > num)
					{
						pchName = pchName2;
						continue;
					}
					return (wzname: CreateString(array, pchName2, trimTerminator: true), pulParamSeq: (int)pulParamSeq2, pdwParamFlags: pdwParamFlags2, ptOwner: ptOwner2, reserved: (int)reserved2);
				}
				throw new InvalidOperationException("Native buffer size did not stabilize.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport2.TryEnumGenericParamConstraints(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdGenericParam,ICorDebugSharp.mdGenericParamConstraint@,System.UInt32,System.UInt32@)" />
		public (mdGenericParamConstraint rGenericParamConstraints, int pcGenericParamConstraints) EnumGenericParamConstraints(ref HCorEnum phEnum, mdGenericParam tk)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumGenericParamConstraints(ref phEnum, tk, out var rGenericParamConstraints, 1u, out var pcGenericParamConstraints));
			return (rGenericParamConstraints: rGenericParamConstraints, pcGenericParamConstraints: checked((int)pcGenericParamConstraints));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport2.TryEnumGenericParams(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdToken,ICorDebugSharp.mdGenericParam@,System.UInt32,System.UInt32@)" />
		public (mdGenericParam rGenericParams, int pcGenericParams) EnumGenericParams(ref HCorEnum phEnum, mdToken tk)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumGenericParams(ref phEnum, tk, out var rGenericParams, 1u, out var pcGenericParams));
			return (rGenericParams: rGenericParams, pcGenericParams: checked((int)pcGenericParams));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport2.TryEnumMethodSpecs(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdToken,ICorDebugSharp.mdMethodSpec@,System.UInt32,System.UInt32@)" />
		public (mdMethodSpec rMethodSpecs, int pcMethodSpecs) EnumMethodSpecs(ref HCorEnum phEnum, mdToken tk)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumMethodSpecs(ref phEnum, tk, out var rMethodSpecs, 1u, out var pcMethodSpecs));
			return (rMethodSpecs: rMethodSpecs, pcMethodSpecs: checked((int)pcMethodSpecs));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport2.TryGetGenericParamConstraintProps(ICorDebugSharp.mdGenericParamConstraint,ICorDebugSharp.mdGenericParam@,ICorDebugSharp.mdToken@)" />
		public (mdGenericParam ptGenericParam, mdToken ptkConstraintType) GetGenericParamConstraintProps(mdGenericParamConstraint gpc)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetGenericParamConstraintProps(gpc, out var ptGenericParam, out var ptkConstraintType));
			return (ptGenericParam: ptGenericParam, ptkConstraintType: ptkConstraintType);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport2.TryGetGenericParamProps(ICorDebugSharp.mdGenericParam,System.UInt32@,ICorDebugSharp.CorGenericParamAttr@,ICorDebugSharp.mdToken@,System.UInt32@,System.Char[],System.UInt32,System.UInt32@)" />
		public (int pulParamSeq, CorGenericParamAttr pdwParamFlags, mdToken ptOwner, int reserved, int pchName) GetGenericParamProps(mdGenericParam gp, char[]? wzname, int cchName)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetGenericParamProps(gp, out var pulParamSeq, out var pdwParamFlags, out var ptOwner, out var reserved, wzname, (uint)cchName, out var pchName));
				return (pulParamSeq: (int)pulParamSeq, pdwParamFlags: pdwParamFlags, ptOwner: ptOwner, reserved: (int)reserved, pchName: (int)pchName);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport2.TryGetMethodSpecProps(ICorDebugSharp.mdMethodSpec,ICorDebugSharp.mdToken@,System.Byte*@,System.UInt32@)" />
		public unsafe (mdToken tkParent, nint ppvSigBlob, int pcbSigBlob) GetMethodSpecProps(mdMethodSpec mi)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetMethodSpecProps(mi, out var tkParent, out var ppvSigBlob, out var pcbSigBlob));
			return (tkParent: tkParent, ppvSigBlob: (nint)ppvSigBlob, pcbSigBlob: checked((int)pcbSigBlob));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport2.TryGetPEKind(System.UInt32@,System.UInt32@)" />
		public (int pdwPEKind, int pdwMAchine) PEKind
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetPEKind(out var pdwPEKind, out var pdwMAchine));
				return checked((pdwPEKind: (int)pdwPEKind, pdwMAchine: (int)pdwMAchine));
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport2.TryGetVersionString(System.Char[],System.UInt32,System.UInt32@)" />
		public int GetVersionString(char[]? pwzBuf, int ccBufSize)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetVersionString(pwzBuf, (uint)ccBufSize, out var pccBufSize));
				return (int)pccBufSize;
			}
		}
	}

	private static IEnumerable<mdGenericParam> EnumerateGenericParamsCore(IMetaDataImport2 instance, mdToken tk)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumGenericParams(ref handle, tk, out var rGenericParams, 1u, out var pcGenericParams));
				switch (pcGenericParams)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rGenericParams;
					break;
				case 0u:
					yield break;
				}
			}
		}
		finally
		{
			if (!handle.IsNull)
			{
				instance.TryCloseEnum(handle);
			}
		}
	}

	private static IEnumerable<mdGenericParamConstraint> EnumerateGenericParamConstraintsCore(IMetaDataImport2 instance, mdGenericParam tk)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumGenericParamConstraints(ref handle, tk, out var rGenericParamConstraints, 1u, out var pcGenericParamConstraints));
				switch (pcGenericParamConstraints)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rGenericParamConstraints;
					break;
				case 0u:
					yield break;
				}
			}
		}
		finally
		{
			if (!handle.IsNull)
			{
				instance.TryCloseEnum(handle);
			}
		}
	}

	private static IEnumerable<mdMethodSpec> EnumerateMethodSpecsCore(IMetaDataImport2 instance, mdToken tk)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumMethodSpecs(ref handle, tk, out var rMethodSpecs, 1u, out var pcMethodSpecs));
				switch (pcMethodSpecs)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rMethodSpecs;
					break;
				case 0u:
					yield break;
				}
			}
		}
		finally
		{
			if (!handle.IsNull)
			{
				instance.TryCloseEnum(handle);
			}
		}
	}

	private static string QueryGetVersionString(IMetaDataImport2 instance)
	{
		Marshal.ThrowExceptionForHR(instance.TryGetVersionString(null, 0u, out var pccBufSize));
		if (pccBufSize == 0)
		{
			return string.Empty;
		}
		for (int i = 0; i < 3; i++)
		{
			uint num = pccBufSize;
			char[] array = new char[checked((int)num)];
			Marshal.ThrowExceptionForHR(instance.TryGetVersionString(array, num, out var pccBufSize2));
			if (pccBufSize2 > num)
			{
				pccBufSize = pccBufSize2;
				continue;
			}
			return CreateString(array, pccBufSize2, trimTerminator: true);
		}
		throw new InvalidOperationException("Native buffer size did not stabilize.");
	}

	private static string CreateString(char[] buffer, uint count, bool trimTerminator)
	{
		int num = checked((int)count);
		if (trimTerminator && num > 0 && buffer[num - 1] == '\0')
		{
			num--;
		}
		return new string(buffer, 0, num);
	}
}
