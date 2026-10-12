using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class MetaDataAssemblyImportExtensions
{
	extension(IMetaDataAssemblyImport instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryEnumAssemblyRefs(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdAssemblyRef@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdAssemblyRef> EnumAssemblyRefs()
		{
			return EnumerateAssemblyRefsCore(instance);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryEnumFiles(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdFile@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdFile> EnumFiles()
		{
			return EnumerateFilesCore(instance);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryEnumExportedTypes(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdExportedType@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdExportedType> EnumExportedTypes()
		{
			return EnumerateExportedTypesCore(instance);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryEnumManifestResources(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdManifestResource@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdManifestResource> EnumManifestResources()
		{
			return EnumerateManifestResourcesCore(instance);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryGetFileProps(ICorDebugSharp.mdFile,System.Char[],System.UInt32,System.UInt32@,System.IntPtr@,System.UInt32@,ICorDebugSharp.CorFileFlags@)" />
		public string GetFileProps(mdFile file)
		{
			return QueryGetFileProps(instance, file);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryGetExportedTypeProps(ICorDebugSharp.mdExportedType,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.mdToken@,ICorDebugSharp.mdTypeDef@,ICorDebugSharp.CorTypeAttr@)" />
		public string GetExportedTypeProps(mdExportedType exportedType)
		{
			return QueryGetExportedTypeProps(instance, exportedType);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryGetManifestResourceProps(ICorDebugSharp.mdManifestResource,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.mdToken@,System.UInt32@,ICorDebugSharp.CorManifestResourceFlags@)" />
		public string GetManifestResourceProps(mdManifestResource resource)
		{
			return QueryGetManifestResourceProps(instance, resource);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryGetAssemblyProps(ICorDebugSharp.mdAssembly,System.IntPtr@,System.UInt32@,System.UInt32@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.ASSEMBLYMETADATA@,ICorDebugSharp.CorAssemblyFlags@)" />
		public (string szName, nint ppbPublicKey, int pcbPublicKey, int pulHashAlgId, ASSEMBLYMETADATA pMetaData, CorAssemblyFlags pdwAssemblyFlags) GetAssemblyProps(mdAssembly mda)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetAssemblyProps(mda, out var ppbPublicKey, out var pcbPublicKey, out var pulHashAlgId, null, 0u, out var pchName, out var pMetaData, out var pdwAssemblyFlags));
			checked
			{
				if (pchName == 0)
				{
					return (szName: string.Empty, ppbPublicKey: ppbPublicKey, pcbPublicKey: (int)pcbPublicKey, pulHashAlgId: (int)pulHashAlgId, pMetaData: pMetaData, pdwAssemblyFlags: pdwAssemblyFlags);
				}
				for (int i = 0; i < 3; i = unchecked(i + 1))
				{
					uint num = pchName;
					char[] array = new char[(int)num];
					Marshal.ThrowExceptionForHR(instance.TryGetAssemblyProps(mda, out var ppbPublicKey2, out var pcbPublicKey2, out var pulHashAlgId2, array, num, out var pchName2, out var pMetaData2, out var pdwAssemblyFlags2));
					if (pchName2 > num)
					{
						pchName = pchName2;
						continue;
					}
					return (szName: CreateString(array, pchName2, trimTerminator: true), ppbPublicKey: ppbPublicKey2, pcbPublicKey: (int)pcbPublicKey2, pulHashAlgId: (int)pulHashAlgId2, pMetaData: pMetaData2, pdwAssemblyFlags: pdwAssemblyFlags2);
				}
				throw new InvalidOperationException("Native buffer size did not stabilize.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryGetAssemblyRefProps(ICorDebugSharp.mdAssemblyRef,System.IntPtr@,System.UInt32@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.ASSEMBLYMETADATA@,System.IntPtr@,System.UInt32@,ICorDebugSharp.CorAssemblyFlags@)" />
		public (string szName, nint ppbPublicKeyOrToken, int pcbPublicKeyOrToken, ASSEMBLYMETADATA pMetaData, nint ppbHashValue, int pcbHashValue, CorAssemblyFlags pdwAssemblyRefFlags) GetAssemblyRefProps(mdAssemblyRef mdar)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetAssemblyRefProps(mdar, out var ppbPublicKeyOrToken, out var pcbPublicKeyOrToken, null, 0u, out var pchName, out var pMetaData, out var ppbHashValue, out var pcbHashValue, out var pdwAssemblyRefFlags));
			checked
			{
				if (pchName == 0)
				{
					return (szName: string.Empty, ppbPublicKeyOrToken: ppbPublicKeyOrToken, pcbPublicKeyOrToken: (int)pcbPublicKeyOrToken, pMetaData: pMetaData, ppbHashValue: ppbHashValue, pcbHashValue: (int)pcbHashValue, pdwAssemblyRefFlags: pdwAssemblyRefFlags);
				}
				for (int i = 0; i < 3; i = unchecked(i + 1))
				{
					uint num = pchName;
					char[] array = new char[(int)num];
					Marshal.ThrowExceptionForHR(instance.TryGetAssemblyRefProps(mdar, out var ppbPublicKeyOrToken2, out var pcbPublicKeyOrToken2, array, num, out var pchName2, out var pMetaData2, out var ppbHashValue2, out var pcbHashValue2, out var pdwAssemblyRefFlags2));
					if (pchName2 > num)
					{
						pchName = pchName2;
						continue;
					}
					return (szName: CreateString(array, pchName2, trimTerminator: true), ppbPublicKeyOrToken: ppbPublicKeyOrToken2, pcbPublicKeyOrToken: (int)pcbPublicKeyOrToken2, pMetaData: pMetaData2, ppbHashValue: ppbHashValue2, pcbHashValue: (int)pcbHashValue2, pdwAssemblyRefFlags: pdwAssemblyRefFlags2);
				}
				throw new InvalidOperationException("Native buffer size did not stabilize.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryEnumAssemblyRefs(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdAssemblyRef@,System.UInt32,System.UInt32@)" />
		public (mdAssemblyRef rAssemblyRefs, int pcTokens) EnumAssemblyRefs(ref HCorEnum phEnum)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumAssemblyRefs(ref phEnum, out var rAssemblyRefs, 1u, out var pcTokens));
			return (rAssemblyRefs: rAssemblyRefs, pcTokens: checked((int)pcTokens));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryEnumExportedTypes(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdExportedType@,System.UInt32,System.UInt32@)" />
		public (mdExportedType rExportedTypes, int pcTokens) EnumExportedTypes(ref HCorEnum phEnum)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumExportedTypes(ref phEnum, out var rExportedTypes, 1u, out var pcTokens));
			return (rExportedTypes: rExportedTypes, pcTokens: checked((int)pcTokens));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryEnumFiles(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdFile@,System.UInt32,System.UInt32@)" />
		public (mdFile rFiles, int pcTokens) EnumFiles(ref HCorEnum phEnum)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumFiles(ref phEnum, out var rFiles, 1u, out var pcTokens));
			return (rFiles: rFiles, pcTokens: checked((int)pcTokens));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryEnumManifestResources(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdManifestResource@,System.UInt32,System.UInt32@)" />
		public (mdManifestResource rManifestResources, int pcTokens) EnumManifestResources(ref HCorEnum phEnum)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumManifestResources(ref phEnum, out var rManifestResources, 1u, out var pcTokens));
			return (rManifestResources: rManifestResources, pcTokens: checked((int)pcTokens));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryFindAssembliesByName(System.String,System.String,System.String,System.IntPtr@,System.UInt32,System.UInt32@)" />
		public (nint ppIUnk, int pcAssemblies) FindAssembliesByName(string szAppBase, string szPrivateBin, string szAssemblyName)
		{
			Marshal.ThrowExceptionForHR(instance.TryFindAssembliesByName(szAppBase, szPrivateBin, szAssemblyName, out var ppIUnk, 1u, out var pcAssemblies));
			return (ppIUnk: ppIUnk, pcAssemblies: checked((int)pcAssemblies));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryFindExportedTypeByName(System.String,ICorDebugSharp.mdToken,ICorDebugSharp.mdExportedType@)" />
		public mdExportedType FindExportedTypeByName(string szName, mdToken mdtExportedType)
		{
			Marshal.ThrowExceptionForHR(instance.TryFindExportedTypeByName(szName, mdtExportedType, out var ptkExportedType));
			return ptkExportedType;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryFindManifestResourceByName(System.String,ICorDebugSharp.mdManifestResource@)" />
		public mdManifestResource FindManifestResourceByName(string szName)
		{
			Marshal.ThrowExceptionForHR(instance.TryFindManifestResourceByName(szName, out var ptkManifestResource));
			return ptkManifestResource;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryGetAssemblyFromScope(ICorDebugSharp.mdAssembly@)" />
		public mdAssembly AssemblyFromScope
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetAssemblyFromScope(out var ptkAssembly));
				return ptkAssembly;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryGetAssemblyProps(ICorDebugSharp.mdAssembly,System.IntPtr@,System.UInt32@,System.UInt32@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.ASSEMBLYMETADATA@,ICorDebugSharp.CorAssemblyFlags@)" />
		public (nint ppbPublicKey, int pcbPublicKey, int pulHashAlgId, int pchName, ASSEMBLYMETADATA pMetaData, CorAssemblyFlags pdwAssemblyFlags) GetAssemblyProps(mdAssembly mda, char[]? szName, int cchName)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetAssemblyProps(mda, out var ppbPublicKey, out var pcbPublicKey, out var pulHashAlgId, szName, (uint)cchName, out var pchName, out var pMetaData, out var pdwAssemblyFlags));
				return (ppbPublicKey: ppbPublicKey, pcbPublicKey: (int)pcbPublicKey, pulHashAlgId: (int)pulHashAlgId, pchName: (int)pchName, pMetaData: pMetaData, pdwAssemblyFlags: pdwAssemblyFlags);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryGetAssemblyRefProps(ICorDebugSharp.mdAssemblyRef,System.IntPtr@,System.UInt32@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.ASSEMBLYMETADATA@,System.IntPtr@,System.UInt32@,ICorDebugSharp.CorAssemblyFlags@)" />
		public (nint ppbPublicKeyOrToken, int pcbPublicKeyOrToken, int pchName, ASSEMBLYMETADATA pMetaData, nint ppbHashValue, int pcbHashValue, CorAssemblyFlags pdwAssemblyRefFlags) GetAssemblyRefProps(mdAssemblyRef mdar, char[]? szName, int cchName)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetAssemblyRefProps(mdar, out var ppbPublicKeyOrToken, out var pcbPublicKeyOrToken, szName, (uint)cchName, out var pchName, out var pMetaData, out var ppbHashValue, out var pcbHashValue, out var pdwAssemblyRefFlags));
				return (ppbPublicKeyOrToken: ppbPublicKeyOrToken, pcbPublicKeyOrToken: (int)pcbPublicKeyOrToken, pchName: (int)pchName, pMetaData: pMetaData, ppbHashValue: ppbHashValue, pcbHashValue: (int)pcbHashValue, pdwAssemblyRefFlags: pdwAssemblyRefFlags);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryGetExportedTypeProps(ICorDebugSharp.mdExportedType,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.mdToken@,ICorDebugSharp.mdTypeDef@,ICorDebugSharp.CorTypeAttr@)" />
		public (int pchName, mdToken ptkImplementation, mdTypeDef ptkTypeDef, CorTypeAttr pdwExportedTypeFlags) GetExportedTypeProps(mdExportedType mdct, char[]? szName, int cchName)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetExportedTypeProps(mdct, szName, (uint)cchName, out var pchName, out var ptkImplementation, out var ptkTypeDef, out var pdwExportedTypeFlags));
				return (pchName: (int)pchName, ptkImplementation: ptkImplementation, ptkTypeDef: ptkTypeDef, pdwExportedTypeFlags: pdwExportedTypeFlags);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryGetFileProps(ICorDebugSharp.mdFile,System.Char[],System.UInt32,System.UInt32@,System.IntPtr@,System.UInt32@,ICorDebugSharp.CorFileFlags@)" />
		public (int pchName, nint ppbHashValue, int pcbHashValue, CorFileFlags pdwFileFlags) GetFileProps(mdFile mdf, char[]? szName, int cchName)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetFileProps(mdf, szName, (uint)cchName, out var pchName, out var ppbHashValue, out var pcbHashValue, out var pdwFileFlags));
				return (pchName: (int)pchName, ppbHashValue: ppbHashValue, pcbHashValue: (int)pcbHashValue, pdwFileFlags: pdwFileFlags);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataAssemblyImport.TryGetManifestResourceProps(ICorDebugSharp.mdManifestResource,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.mdToken@,System.UInt32@,ICorDebugSharp.CorManifestResourceFlags@)" />
		public (int pchName, mdToken ptkImplementation, int pdwOffset, CorManifestResourceFlags pdwResourceFlags) GetManifestResourceProps(mdManifestResource mdmr, char[]? szName, int cchName)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetManifestResourceProps(mdmr, szName, (uint)cchName, out var pchName, out var ptkImplementation, out var pdwOffset, out var pdwResourceFlags));
				return (pchName: (int)pchName, ptkImplementation: ptkImplementation, pdwOffset: (int)pdwOffset, pdwResourceFlags: pdwResourceFlags);
			}
		}
	}

	private static IEnumerable<mdAssemblyRef> EnumerateAssemblyRefsCore(IMetaDataAssemblyImport instance)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumAssemblyRefs(ref handle, out var rAssemblyRefs, 1u, out var pcTokens));
				switch (pcTokens)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rAssemblyRefs;
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

	private static IEnumerable<mdFile> EnumerateFilesCore(IMetaDataAssemblyImport instance)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumFiles(ref handle, out var rFiles, 1u, out var pcTokens));
				switch (pcTokens)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rFiles;
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

	private static IEnumerable<mdExportedType> EnumerateExportedTypesCore(IMetaDataAssemblyImport instance)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumExportedTypes(ref handle, out var rExportedTypes, 1u, out var pcTokens));
				switch (pcTokens)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rExportedTypes;
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

	private static IEnumerable<mdManifestResource> EnumerateManifestResourcesCore(IMetaDataAssemblyImport instance)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumManifestResources(ref handle, out var rManifestResources, 1u, out var pcTokens));
				switch (pcTokens)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rManifestResources;
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

	private static string QueryGetFileProps(IMetaDataAssemblyImport instance, mdFile file)
	{
		Marshal.ThrowExceptionForHR(instance.TryGetFileProps(file, null, 0u, out var pchName, out var ppbHashValue, out var pcbHashValue, out var pdwFileFlags));
		if (pchName == 0)
		{
			return string.Empty;
		}
		for (int i = 0; i < 3; i++)
		{
			uint num = pchName;
			char[] array = new char[checked((int)num)];
			Marshal.ThrowExceptionForHR(instance.TryGetFileProps(file, array, num, out var pchName2, out ppbHashValue, out pcbHashValue, out pdwFileFlags));
			if (pchName2 > num)
			{
				pchName = pchName2;
				continue;
			}
			return CreateString(array, pchName2, trimTerminator: true);
		}
		throw new InvalidOperationException("Native buffer size did not stabilize.");
	}

	private static string QueryGetExportedTypeProps(IMetaDataAssemblyImport instance, mdExportedType exportedType)
	{
		Marshal.ThrowExceptionForHR(instance.TryGetExportedTypeProps(exportedType, null, 0u, out var pchName, out var ptkImplementation, out var ptkTypeDef, out var pdwExportedTypeFlags));
		if (pchName == 0)
		{
			return string.Empty;
		}
		for (int i = 0; i < 3; i++)
		{
			uint num = pchName;
			char[] array = new char[checked((int)num)];
			Marshal.ThrowExceptionForHR(instance.TryGetExportedTypeProps(exportedType, array, num, out var pchName2, out ptkImplementation, out ptkTypeDef, out pdwExportedTypeFlags));
			if (pchName2 > num)
			{
				pchName = pchName2;
				continue;
			}
			return CreateString(array, pchName2, trimTerminator: true);
		}
		throw new InvalidOperationException("Native buffer size did not stabilize.");
	}

	private static string QueryGetManifestResourceProps(IMetaDataAssemblyImport instance, mdManifestResource resource)
	{
		Marshal.ThrowExceptionForHR(instance.TryGetManifestResourceProps(resource, null, 0u, out var pchName, out var ptkImplementation, out var pdwOffset, out var pdwResourceFlags));
		if (pchName == 0)
		{
			return string.Empty;
		}
		for (int i = 0; i < 3; i++)
		{
			uint num = pchName;
			char[] array = new char[checked((int)num)];
			Marshal.ThrowExceptionForHR(instance.TryGetManifestResourceProps(resource, array, num, out var pchName2, out ptkImplementation, out pdwOffset, out pdwResourceFlags));
			if (pchName2 > num)
			{
				pchName = pchName2;
				continue;
			}
			return CreateString(array, pchName2, trimTerminator: true);
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
