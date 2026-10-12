using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class MetaDataImportExtensions
{
	extension(IMetaDataImport instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumTypeDefs(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdTypeDef> EnumTypeDefs()
		{
			return EnumerateTypeDefsCore(instance);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumInterfaceImpls(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,ICorDebugSharp.mdInterfaceImpl@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdInterfaceImpl> EnumInterfaceImpls(mdTypeDef td)
		{
			return EnumerateInterfaceImplsCore(instance, td);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumTypeRefs(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeRef@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdTypeRef> EnumTypeRefs()
		{
			return EnumerateTypeRefsCore(instance);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumMembers(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,ICorDebugSharp.mdToken@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdToken> EnumMembers(mdTypeDef cl)
		{
			return EnumerateMembersCore(instance, cl);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumMembersWithName(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,System.String,ICorDebugSharp.mdToken@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdToken> EnumMembersWithName(mdTypeDef cl, string szName)
		{
			return EnumerateMembersWithNameCore(instance, cl, szName);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumMethods(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,ICorDebugSharp.mdMethodDef@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdMethodDef> EnumMethods(mdTypeDef cl)
		{
			return EnumerateMethodsCore(instance, cl);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumMethodsWithName(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,System.String,ICorDebugSharp.mdMethodDef@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdMethodDef> EnumMethodsWithName(mdTypeDef cl, string szName)
		{
			return EnumerateMethodsWithNameCore(instance, cl, szName);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumFields(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,ICorDebugSharp.mdFieldDef@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdFieldDef> EnumFields(mdTypeDef cl)
		{
			return EnumerateFieldsCore(instance, cl);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumFieldsWithName(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,System.String,ICorDebugSharp.mdFieldDef@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdFieldDef> EnumFieldsWithName(mdTypeDef cl, string szName)
		{
			return EnumerateFieldsWithNameCore(instance, cl, szName);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumParams(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdMethodDef,ICorDebugSharp.mdParamDef@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdParamDef> EnumParams(mdMethodDef mb)
		{
			return EnumerateParamsCore(instance, mb);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumMemberRefs(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdToken,ICorDebugSharp.mdMemberRef@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdMemberRef> EnumMemberRefs(mdToken tkParent)
		{
			return EnumerateMemberRefsCore(instance, tkParent);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumMethodImpls(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,ICorDebugSharp.mdToken@,ICorDebugSharp.mdToken@,System.UInt32,System.UInt32@)" />
		public IEnumerable<(mdToken MethodBody, mdToken MethodDeclaration)> EnumMethodImpls(mdTypeDef td)
		{
			return EnumerateMethodImplsCore(instance, td);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumPermissionSets(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdToken,System.UInt32,ICorDebugSharp.mdPermission@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdPermission> EnumPermissionSets(mdToken tk, int dwActions)
		{
			return EnumeratePermissionSetsCore(instance, tk, dwActions);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumProperties(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,ICorDebugSharp.mdProperty@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdProperty> EnumProperties(mdTypeDef td)
		{
			return EnumeratePropertiesCore(instance, td);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumEvents(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,ICorDebugSharp.mdEvent@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdEvent> EnumEvents(mdTypeDef td)
		{
			return EnumerateEventsCore(instance, td);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumMethodSemantics(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdMethodDef,ICorDebugSharp.mdToken@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdToken> EnumMethodSemantics(mdMethodDef mb)
		{
			return EnumerateMethodSemanticsCore(instance, mb);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumModuleRefs(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdModuleRef@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdModuleRef> EnumModuleRefs()
		{
			return EnumerateModuleRefsCore(instance);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumUnresolvedMethods(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdToken@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdToken> EnumUnresolvedMethods()
		{
			return EnumerateUnresolvedMethodsCore(instance);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumSignatures(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdSignature@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdSignature> EnumSignatures()
		{
			return EnumerateSignaturesCore(instance);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumTypeSpecs(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeSpec@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdTypeSpec> EnumTypeSpecs()
		{
			return EnumerateTypeSpecsCore(instance);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumUserStrings(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdString@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdString> EnumUserStrings()
		{
			return EnumerateUserStringsCore(instance);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumCustomAttributes(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdToken,ICorDebugSharp.mdToken,ICorDebugSharp.mdCustomAttribute@,System.UInt32,System.UInt32@)" />
		public IEnumerable<mdCustomAttribute> EnumCustomAttributes(mdToken tk, mdToken tkType)
		{
			return EnumerateCustomAttributesCore(instance, tk, tkType);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetScopeProps(System.Char[],System.UInt32,System.UInt32@,System.Guid@)" />
		public (string szName, Guid pmvid) ScopeProps
		{
			get
			{
				return QueryGetScopeProps(instance);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetTypeDefProps(ICorDebugSharp.mdTypeDef,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.CorTypeAttr@,ICorDebugSharp.mdToken@)" />
		public (string szTypeDef, CorTypeAttr pdwTypeDefFlags, mdToken ptkExtends) GetTypeDefProps(mdTypeDef typeDefinition)
		{
			return QueryGetTypeDefProps(instance, typeDefinition);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetTypeRefProps(ICorDebugSharp.mdTypeRef,ICorDebugSharp.mdToken@,System.Char[],System.UInt32,System.UInt32@)" />
		public (mdToken ptkResolutionScope, string szName) GetTypeRefProps(mdTypeRef typeReference)
		{
			return QueryGetTypeRefProps(instance, typeReference);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetModuleRefProps(ICorDebugSharp.mdModuleRef,System.Char[],System.UInt32,System.UInt32@)" />
		public string GetModuleRefProps(mdModuleRef moduleReference)
		{
			return QueryGetModuleRefProps(instance, moduleReference);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetUserString(ICorDebugSharp.mdString,System.Char[],System.UInt32,System.UInt32@)" />
		public string GetUserString(mdString userString)
		{
			return QueryGetUserString(instance, userString);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryIsValidToken(ICorDebugSharp.mdToken)" />
		public bool IsValidToken(mdToken tk)
		{
			return instance.TryIsValidToken(tk) != 0;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetEventProps(ICorDebugSharp.mdEvent,ICorDebugSharp.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.CorEventAttr@,ICorDebugSharp.mdToken@,ICorDebugSharp.mdMethodDef@,ICorDebugSharp.mdMethodDef@,ICorDebugSharp.mdMethodDef@,ICorDebugSharp.mdMethodDef@,System.UInt32,System.UInt32@)" />
		public (string szEvent, mdTypeDef pClass, CorEventAttr pdwEventFlags, mdToken ptkEventType, mdMethodDef pmdAddOn, mdMethodDef pmdRemoveOn, mdMethodDef pmdFire, mdMethodDef rmdOtherMethod, int pcOtherMethod) GetEventProps(mdEvent ev)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetEventProps(ev, out var pClass, null, 0u, out var pchEvent, out var pdwEventFlags, out var ptkEventType, out var pmdAddOn, out var pmdRemoveOn, out var pmdFire, out var rmdOtherMethod, 1u, out var pcOtherMethod));
			checked
			{
				if (pchEvent == 0)
				{
					return (szEvent: string.Empty, pClass: pClass, pdwEventFlags: pdwEventFlags, ptkEventType: ptkEventType, pmdAddOn: pmdAddOn, pmdRemoveOn: pmdRemoveOn, pmdFire: pmdFire, rmdOtherMethod: rmdOtherMethod, pcOtherMethod: (int)pcOtherMethod);
				}
				for (int i = 0; i < 3; i = unchecked(i + 1))
				{
					uint num = pchEvent;
					char[] array = new char[(int)num];
					Marshal.ThrowExceptionForHR(instance.TryGetEventProps(ev, out var pClass2, array, num, out var pchEvent2, out var pdwEventFlags2, out var ptkEventType2, out var pmdAddOn2, out var pmdRemoveOn2, out var pmdFire2, out var rmdOtherMethod2, 1u, out var pcOtherMethod2));
					if (pchEvent2 > num)
					{
						pchEvent = pchEvent2;
						continue;
					}
					return (szEvent: CreateString(array, pchEvent2, trimTerminator: true), pClass: pClass2, pdwEventFlags: pdwEventFlags2, ptkEventType: ptkEventType2, pmdAddOn: pmdAddOn2, pmdRemoveOn: pmdRemoveOn2, pmdFire: pmdFire2, rmdOtherMethod: rmdOtherMethod2, pcOtherMethod: (int)pcOtherMethod2);
				}
				throw new InvalidOperationException("Native buffer size did not stabilize.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetFieldProps(ICorDebugSharp.mdFieldDef,ICorDebugSharp.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.CorFieldAttr@,System.Byte*@,System.UInt32@,ICorDebugSharp.CorElementType@,System.IntPtr@,System.UInt32@)" />
		public unsafe (string szField, mdTypeDef pClass, CorFieldAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) GetFieldProps(mdFieldDef mb)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetFieldProps(mb, out var pClass, null, 0u, out var pchField, out var pdwAttr, out var ppvSigBlob, out var pcbSigBlob, out var pdwCPlusTypeFlag, out var ppValue, out var pcchValue));
			checked
			{
				if (pchField == 0)
				{
					return (szField: string.Empty, pClass: pClass, pdwAttr: pdwAttr, ppvSigBlob: unchecked((nint)ppvSigBlob), pcbSigBlob: (int)pcbSigBlob, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppValue: ppValue, pcchValue: (int)pcchValue);
				}
				for (int i = 0; i < 3; i = unchecked(i + 1))
				{
					uint num = pchField;
					char[] array = new char[(int)num];
					Marshal.ThrowExceptionForHR(instance.TryGetFieldProps(mb, out var pClass2, array, num, out var pchField2, out var pdwAttr2, out var ppvSigBlob2, out var pcbSigBlob2, out var pdwCPlusTypeFlag2, out var ppValue2, out var pcchValue2));
					if (pchField2 > num)
					{
						pchField = pchField2;
						continue;
					}
					return (szField: CreateString(array, pchField2, trimTerminator: true), pClass: pClass2, pdwAttr: pdwAttr2, ppvSigBlob: unchecked((nint)ppvSigBlob2), pcbSigBlob: (int)pcbSigBlob2, pdwCPlusTypeFlag: pdwCPlusTypeFlag2, ppValue: ppValue2, pcchValue: (int)pcchValue2);
				}
				throw new InvalidOperationException("Native buffer size did not stabilize.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetMemberProps(ICorDebugSharp.mdToken,ICorDebugSharp.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,System.UInt32@,System.Byte*@,System.UInt32@,System.UInt32@,System.UInt32@,ICorDebugSharp.CorElementType@,System.IntPtr@,System.UInt32@)" />
		public unsafe (string szMember, mdTypeDef pClass, int pdwAttr, nint ppvSigBlob, int pcbSigBlob, int pulCodeRVA, int pdwImplFlags, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) GetMemberProps(mdToken mb)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetMemberProps(mb, out var pClass, null, 0u, out var pchMember, out var pdwAttr, out var ppvSigBlob, out var pcbSigBlob, out var pulCodeRVA, out var pdwImplFlags, out var pdwCPlusTypeFlag, out var ppValue, out var pcchValue));
			checked
			{
				if (pchMember == 0)
				{
					return (szMember: string.Empty, pClass: pClass, pdwAttr: (int)pdwAttr, ppvSigBlob: unchecked((nint)ppvSigBlob), pcbSigBlob: (int)pcbSigBlob, pulCodeRVA: (int)pulCodeRVA, pdwImplFlags: (int)pdwImplFlags, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppValue: ppValue, pcchValue: (int)pcchValue);
				}
				for (int i = 0; i < 3; i = unchecked(i + 1))
				{
					uint num = pchMember;
					char[] array = new char[(int)num];
					Marshal.ThrowExceptionForHR(instance.TryGetMemberProps(mb, out var pClass2, array, num, out var pchMember2, out var pdwAttr2, out var ppvSigBlob2, out var pcbSigBlob2, out var pulCodeRVA2, out var pdwImplFlags2, out var pdwCPlusTypeFlag2, out var ppValue2, out var pcchValue2));
					if (pchMember2 > num)
					{
						pchMember = pchMember2;
						continue;
					}
					return (szMember: CreateString(array, pchMember2, trimTerminator: true), pClass: pClass2, pdwAttr: (int)pdwAttr2, ppvSigBlob: unchecked((nint)ppvSigBlob2), pcbSigBlob: (int)pcbSigBlob2, pulCodeRVA: (int)pulCodeRVA2, pdwImplFlags: (int)pdwImplFlags2, pdwCPlusTypeFlag: pdwCPlusTypeFlag2, ppValue: ppValue2, pcchValue: (int)pcchValue2);
				}
				throw new InvalidOperationException("Native buffer size did not stabilize.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetMemberRefProps(ICorDebugSharp.mdMemberRef,ICorDebugSharp.mdToken@,System.Char[],System.UInt32,System.UInt32@,System.Byte*@,System.UInt32@)" />
		public unsafe (string szMember, mdToken ptk, nint ppvSigBlob, int pbSig) GetMemberRefProps(mdMemberRef mr)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetMemberRefProps(mr, out var ptk, null, 0u, out var pchMember, out var ppvSigBlob, out var pbSig));
			if (pchMember == 0)
			{
				return (szMember: string.Empty, ptk: ptk, ppvSigBlob: (nint)ppvSigBlob, pbSig: checked((int)pbSig));
			}
			for (int i = 0; i < 3; i++)
			{
				uint num = pchMember;
				char[] array = new char[checked((int)num)];
				Marshal.ThrowExceptionForHR(instance.TryGetMemberRefProps(mr, out var ptk2, array, num, out var pchMember2, out var ppvSigBlob2, out var pbSig2));
				if (pchMember2 > num)
				{
					pchMember = pchMember2;
					continue;
				}
				return (szMember: CreateString(array, pchMember2, trimTerminator: true), ptk: ptk2, ppvSigBlob: (nint)ppvSigBlob2, pbSig: checked((int)pbSig2));
			}
			throw new InvalidOperationException("Native buffer size did not stabilize.");
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetMethodProps(ICorDebugSharp.mdMethodDef,ICorDebugSharp.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.CorMethodAttr@,System.Byte*@,System.UInt32@,System.UInt32@,System.UInt32@)" />
		public unsafe (string szMethod, mdTypeDef pClass, CorMethodAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, int pulCodeRVA, int pdwImplFlags) GetMethodProps(mdMethodDef mb)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetMethodProps(mb, out var pClass, null, 0u, out var pchMethod, out var pdwAttr, out var ppvSigBlob, out var pcbSigBlob, out var pulCodeRVA, out var pdwImplFlags));
			checked
			{
				if (pchMethod == 0)
				{
					return (szMethod: string.Empty, pClass: pClass, pdwAttr: pdwAttr, ppvSigBlob: unchecked((nint)ppvSigBlob), pcbSigBlob: (int)pcbSigBlob, pulCodeRVA: (int)pulCodeRVA, pdwImplFlags: (int)pdwImplFlags);
				}
				for (int i = 0; i < 3; i = unchecked(i + 1))
				{
					uint num = pchMethod;
					char[] array = new char[(int)num];
					Marshal.ThrowExceptionForHR(instance.TryGetMethodProps(mb, out var pClass2, array, num, out var pchMethod2, out var pdwAttr2, out var ppvSigBlob2, out var pcbSigBlob2, out var pulCodeRVA2, out var pdwImplFlags2));
					if (pchMethod2 > num)
					{
						pchMethod = pchMethod2;
						continue;
					}
					return (szMethod: CreateString(array, pchMethod2, trimTerminator: true), pClass: pClass2, pdwAttr: pdwAttr2, ppvSigBlob: unchecked((nint)ppvSigBlob2), pcbSigBlob: (int)pcbSigBlob2, pulCodeRVA: (int)pulCodeRVA2, pdwImplFlags: (int)pdwImplFlags2);
				}
				throw new InvalidOperationException("Native buffer size did not stabilize.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetParamProps(ICorDebugSharp.mdParamDef,ICorDebugSharp.mdMethodDef@,System.UInt32@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.CorParamAttr@,ICorDebugSharp.CorElementType@,System.IntPtr@,System.UInt32@)" />
		public (string szName, mdMethodDef pmd, int pulSequence, CorParamAttr pdwAttr, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) GetParamProps(mdParamDef tk)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetParamProps(tk, out var pmd, out var pulSequence, null, 0u, out var pchName, out var pdwAttr, out var pdwCPlusTypeFlag, out var ppValue, out var pcchValue));
			checked
			{
				if (pchName == 0)
				{
					return (szName: string.Empty, pmd: pmd, pulSequence: (int)pulSequence, pdwAttr: pdwAttr, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppValue: ppValue, pcchValue: (int)pcchValue);
				}
				for (int i = 0; i < 3; i = unchecked(i + 1))
				{
					uint num = pchName;
					char[] array = new char[(int)num];
					Marshal.ThrowExceptionForHR(instance.TryGetParamProps(tk, out var pmd2, out var pulSequence2, array, num, out var pchName2, out var pdwAttr2, out var pdwCPlusTypeFlag2, out var ppValue2, out var pcchValue2));
					if (pchName2 > num)
					{
						pchName = pchName2;
						continue;
					}
					return (szName: CreateString(array, pchName2, trimTerminator: true), pmd: pmd2, pulSequence: (int)pulSequence2, pdwAttr: pdwAttr2, pdwCPlusTypeFlag: pdwCPlusTypeFlag2, ppValue: ppValue2, pcchValue: (int)pcchValue2);
				}
				throw new InvalidOperationException("Native buffer size did not stabilize.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetPinvokeMap(ICorDebugSharp.mdToken,System.UInt32@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.mdModuleRef@)" />
		public (string szImportName, int pdwMappingFlags, mdModuleRef pmrImportDLL) GetPinvokeMap(mdToken tk)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetPinvokeMap(tk, out var pdwMappingFlags, null, 0u, out var pchImportName, out var pmrImportDLL));
			checked
			{
				if (pchImportName == 0)
				{
					return (szImportName: string.Empty, pdwMappingFlags: (int)pdwMappingFlags, pmrImportDLL: pmrImportDLL);
				}
				for (int i = 0; i < 3; i = unchecked(i + 1))
				{
					uint num = pchImportName;
					char[] array = new char[(int)num];
					Marshal.ThrowExceptionForHR(instance.TryGetPinvokeMap(tk, out var pdwMappingFlags2, array, num, out var pchImportName2, out var pmrImportDLL2));
					if (pchImportName2 > num)
					{
						pchImportName = pchImportName2;
						continue;
					}
					return (szImportName: CreateString(array, pchImportName2, trimTerminator: true), pdwMappingFlags: (int)pdwMappingFlags2, pmrImportDLL: pmrImportDLL2);
				}
				throw new InvalidOperationException("Native buffer size did not stabilize.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetPropertyProps(ICorDebugSharp.mdProperty,ICorDebugSharp.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.CorPropertyAttr@,System.Byte*@,System.UInt32@,ICorDebugSharp.CorElementType@,System.IntPtr@,System.UInt32@,ICorDebugSharp.mdMethodDef@,ICorDebugSharp.mdMethodDef@,ICorDebugSharp.mdMethodDef@,System.UInt32,System.UInt32@)" />
		public unsafe (string szProperty, mdTypeDef pClass, CorPropertyAttr pdwPropFlags, nint ppvSig, int pbSig, CorElementType pdwCPlusTypeFlag, nint ppDefaultValue, int pcchDefaultValue, mdMethodDef pmdSetter, mdMethodDef pmdGetter, mdMethodDef rmdOtherMethod, int pcOtherMethod) GetPropertyProps(mdProperty prop)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetPropertyProps(prop, out var pClass, null, 0u, out var pchProperty, out var pdwPropFlags, out var ppvSig, out var pbSig, out var pdwCPlusTypeFlag, out var ppDefaultValue, out var pcchDefaultValue, out var pmdSetter, out var pmdGetter, out var rmdOtherMethod, 1u, out var pcOtherMethod));
			checked
			{
				if (pchProperty == 0)
				{
					return (szProperty: string.Empty, pClass: pClass, pdwPropFlags: pdwPropFlags, ppvSig: unchecked((nint)ppvSig), pbSig: (int)pbSig, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppDefaultValue: ppDefaultValue, pcchDefaultValue: (int)pcchDefaultValue, pmdSetter: pmdSetter, pmdGetter: pmdGetter, rmdOtherMethod: rmdOtherMethod, pcOtherMethod: (int)pcOtherMethod);
				}
				for (int i = 0; i < 3; i = unchecked(i + 1))
				{
					uint num = pchProperty;
					char[] array = new char[(int)num];
					Marshal.ThrowExceptionForHR(instance.TryGetPropertyProps(prop, out var pClass2, array, num, out var pchProperty2, out var pdwPropFlags2, out var ppvSig2, out var pbSig2, out var pdwCPlusTypeFlag2, out var ppDefaultValue2, out var pcchDefaultValue2, out var pmdSetter2, out var pmdGetter2, out var rmdOtherMethod2, 1u, out var pcOtherMethod2));
					if (pchProperty2 > num)
					{
						pchProperty = pchProperty2;
						continue;
					}
					return (szProperty: CreateString(array, pchProperty2, trimTerminator: true), pClass: pClass2, pdwPropFlags: pdwPropFlags2, ppvSig: unchecked((nint)ppvSig2), pbSig: (int)pbSig2, pdwCPlusTypeFlag: pdwCPlusTypeFlag2, ppDefaultValue: ppDefaultValue2, pcchDefaultValue: (int)pcchDefaultValue2, pmdSetter: pmdSetter2, pmdGetter: pmdGetter2, rmdOtherMethod: rmdOtherMethod2, pcOtherMethod: (int)pcOtherMethod2);
				}
				throw new InvalidOperationException("Native buffer size did not stabilize.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryCountEnum(ICorDebugSharp.HCorEnum,System.UInt32@)" />
		public int CountEnum(HCorEnum hEnum)
		{
			Marshal.ThrowExceptionForHR(instance.TryCountEnum(hEnum, out var pulCount));
			return checked((int)pulCount);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumCustomAttributes(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdToken,ICorDebugSharp.mdToken,ICorDebugSharp.mdCustomAttribute@,System.UInt32,System.UInt32@)" />
		public (mdCustomAttribute rCustomAttributes, int pcCustomAttributes) EnumCustomAttributes(ref HCorEnum phEnum, mdToken tk, mdToken tkType)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumCustomAttributes(ref phEnum, tk, tkType, out var rCustomAttributes, 1u, out var pcCustomAttributes));
			return (rCustomAttributes: rCustomAttributes, pcCustomAttributes: checked((int)pcCustomAttributes));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumEvents(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,ICorDebugSharp.mdEvent@,System.UInt32,System.UInt32@)" />
		public (mdEvent rEvents, int pcEvents) EnumEvents(ref HCorEnum phEnum, mdTypeDef td)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumEvents(ref phEnum, td, out var rEvents, 1u, out var pcEvents));
			return (rEvents: rEvents, pcEvents: checked((int)pcEvents));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumFields(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,ICorDebugSharp.mdFieldDef@,System.UInt32,System.UInt32@)" />
		public (mdFieldDef rFields, int pcTokens) EnumFields(ref HCorEnum phEnum, mdTypeDef cl)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumFields(ref phEnum, cl, out var rFields, 1u, out var pcTokens));
			return (rFields: rFields, pcTokens: checked((int)pcTokens));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumFieldsWithName(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,System.String,ICorDebugSharp.mdFieldDef@,System.UInt32,System.UInt32@)" />
		public (mdFieldDef rFields, int pcTokens) EnumFieldsWithName(ref HCorEnum phEnum, mdTypeDef cl, string szName)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumFieldsWithName(ref phEnum, cl, szName, out var rFields, 1u, out var pcTokens));
			return (rFields: rFields, pcTokens: checked((int)pcTokens));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumInterfaceImpls(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,ICorDebugSharp.mdInterfaceImpl@,System.UInt32,System.UInt32@)" />
		public (mdInterfaceImpl rImpls, int pcImpls) EnumInterfaceImpls(ref HCorEnum phEnum, mdTypeDef td)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumInterfaceImpls(ref phEnum, td, out var rImpls, 1u, out var pcImpls));
			return (rImpls: rImpls, pcImpls: checked((int)pcImpls));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumMemberRefs(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdToken,ICorDebugSharp.mdMemberRef@,System.UInt32,System.UInt32@)" />
		public (mdMemberRef rMemberRefs, int pcTokens) EnumMemberRefs(ref HCorEnum phEnum, mdToken tkParent)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumMemberRefs(ref phEnum, tkParent, out var rMemberRefs, 1u, out var pcTokens));
			return (rMemberRefs: rMemberRefs, pcTokens: checked((int)pcTokens));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumMembers(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,ICorDebugSharp.mdToken@,System.UInt32,System.UInt32@)" />
		public (mdToken rMembers, int pcTokens) EnumMembers(ref HCorEnum phEnum, mdTypeDef cl)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumMembers(ref phEnum, cl, out var rMembers, 1u, out var pcTokens));
			return (rMembers: rMembers, pcTokens: checked((int)pcTokens));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumMembersWithName(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,System.String,ICorDebugSharp.mdToken@,System.UInt32,System.UInt32@)" />
		public (mdToken rMembers, int pcTokens) EnumMembersWithName(ref HCorEnum phEnum, mdTypeDef cl, string szName)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumMembersWithName(ref phEnum, cl, szName, out var rMembers, 1u, out var pcTokens));
			return (rMembers: rMembers, pcTokens: checked((int)pcTokens));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumMethodImpls(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,ICorDebugSharp.mdToken@,ICorDebugSharp.mdToken@,System.UInt32,System.UInt32@)" />
		public (mdToken rMethodBody, mdToken rMethodDecl, int pcTokens) EnumMethodImpls(ref HCorEnum phEnum, mdTypeDef td)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumMethodImpls(ref phEnum, td, out var rMethodBody, out var rMethodDecl, 1u, out var pcTokens));
			return (rMethodBody: rMethodBody, rMethodDecl: rMethodDecl, pcTokens: checked((int)pcTokens));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumMethodSemantics(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdMethodDef,ICorDebugSharp.mdToken@,System.UInt32,System.UInt32@)" />
		public (mdToken rEventProp, int pcEventProp) EnumMethodSemantics(ref HCorEnum phEnum, mdMethodDef mb)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumMethodSemantics(ref phEnum, mb, out var rEventProp, 1u, out var pcEventProp));
			return (rEventProp: rEventProp, pcEventProp: checked((int)pcEventProp));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumMethods(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,ICorDebugSharp.mdMethodDef@,System.UInt32,System.UInt32@)" />
		public (mdMethodDef rMethods, int pcTokens) EnumMethods(ref HCorEnum phEnum, mdTypeDef cl)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumMethods(ref phEnum, cl, out var rMethods, 1u, out var pcTokens));
			return (rMethods: rMethods, pcTokens: checked((int)pcTokens));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumMethodsWithName(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,System.String,ICorDebugSharp.mdMethodDef@,System.UInt32,System.UInt32@)" />
		public (mdMethodDef rMethods, int pcTokens) EnumMethodsWithName(ref HCorEnum phEnum, mdTypeDef cl, string szName)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumMethodsWithName(ref phEnum, cl, szName, out var rMethods, 1u, out var pcTokens));
			return (rMethods: rMethods, pcTokens: checked((int)pcTokens));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumModuleRefs(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdModuleRef@,System.UInt32,System.UInt32@)" />
		public (mdModuleRef rModuleRefs, int pcModuleRefs) EnumModuleRefs(ref HCorEnum phEnum)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumModuleRefs(ref phEnum, out var rModuleRefs, 1u, out var pcModuleRefs));
			return (rModuleRefs: rModuleRefs, pcModuleRefs: checked((int)pcModuleRefs));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumParams(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdMethodDef,ICorDebugSharp.mdParamDef@,System.UInt32,System.UInt32@)" />
		public (mdParamDef rParams, int pcTokens) EnumParams(ref HCorEnum phEnum, mdMethodDef mb)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumParams(ref phEnum, mb, out var rParams, 1u, out var pcTokens));
			return (rParams: rParams, pcTokens: checked((int)pcTokens));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumPermissionSets(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdToken,System.UInt32,ICorDebugSharp.mdPermission@,System.UInt32,System.UInt32@)" />
		public (mdPermission rPermission, int pcTokens) EnumPermissionSets(ref HCorEnum phEnum, mdToken tk, int dwActions)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumPermissionSets(ref phEnum, tk, (uint)dwActions, out var rPermission, 1u, out var pcTokens));
				return (rPermission: rPermission, pcTokens: (int)pcTokens);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumProperties(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef,ICorDebugSharp.mdProperty@,System.UInt32,System.UInt32@)" />
		public (mdProperty rProperties, int pcProperties) EnumProperties(ref HCorEnum phEnum, mdTypeDef td)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumProperties(ref phEnum, td, out var rProperties, 1u, out var pcProperties));
			return (rProperties: rProperties, pcProperties: checked((int)pcProperties));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumSignatures(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdSignature@,System.UInt32,System.UInt32@)" />
		public (mdSignature rSignatures, int pcSignatures) EnumSignatures(ref HCorEnum phEnum)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumSignatures(ref phEnum, out var rSignatures, 1u, out var pcSignatures));
			return (rSignatures: rSignatures, pcSignatures: checked((int)pcSignatures));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumTypeDefs(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeDef@,System.UInt32,System.UInt32@)" />
		public (mdTypeDef rTypeDefs, int pcTypeDefs) EnumTypeDefs(ref HCorEnum phEnum)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumTypeDefs(ref phEnum, out var rTypeDefs, 1u, out var pcTypeDefs));
			return (rTypeDefs: rTypeDefs, pcTypeDefs: checked((int)pcTypeDefs));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumTypeRefs(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeRef@,System.UInt32,System.UInt32@)" />
		public (mdTypeRef rTypeRefs, int pcTypeRefs) EnumTypeRefs(ref HCorEnum phEnum)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumTypeRefs(ref phEnum, out var rTypeRefs, 1u, out var pcTypeRefs));
			return (rTypeRefs: rTypeRefs, pcTypeRefs: checked((int)pcTypeRefs));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumTypeSpecs(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdTypeSpec@,System.UInt32,System.UInt32@)" />
		public (mdTypeSpec rTypeSpecs, int pcTypeSpecs) EnumTypeSpecs(ref HCorEnum phEnum)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumTypeSpecs(ref phEnum, out var rTypeSpecs, 1u, out var pcTypeSpecs));
			return (rTypeSpecs: rTypeSpecs, pcTypeSpecs: checked((int)pcTypeSpecs));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumUnresolvedMethods(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdToken@,System.UInt32,System.UInt32@)" />
		public (mdToken rMethods, int pcTokens) EnumUnresolvedMethods(ref HCorEnum phEnum)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumUnresolvedMethods(ref phEnum, out var rMethods, 1u, out var pcTokens));
			return (rMethods: rMethods, pcTokens: checked((int)pcTokens));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryEnumUserStrings(ICorDebugSharp.HCorEnum@,ICorDebugSharp.mdString@,System.UInt32,System.UInt32@)" />
		public (mdString rStrings, int pcStrings) EnumUserStrings(ref HCorEnum phEnum)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumUserStrings(ref phEnum, out var rStrings, 1u, out var pcStrings));
			return (rStrings: rStrings, pcStrings: checked((int)pcStrings));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryFindField(ICorDebugSharp.mdTypeDef,System.String,System.Byte*,System.UInt32,ICorDebugSharp.mdFieldDef@)" />
		public unsafe mdFieldDef FindField(mdTypeDef td, string szName, nint pvSigBlob, int cbSigBlob)
		{
			Marshal.ThrowExceptionForHR(instance.TryFindField(td, szName, (byte*)pvSigBlob, checked((uint)cbSigBlob), out var pmb));
			return pmb;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryFindMember(ICorDebugSharp.mdTypeDef,System.String,System.Byte*,System.UInt32,ICorDebugSharp.mdToken@)" />
		public unsafe mdToken FindMember(mdTypeDef td, string szName, nint pvSigBlob, int cbSigBlob)
		{
			Marshal.ThrowExceptionForHR(instance.TryFindMember(td, szName, (byte*)pvSigBlob, checked((uint)cbSigBlob), out var pmb));
			return pmb;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryFindMemberRef(ICorDebugSharp.mdTypeRef,System.String,System.Byte*,System.UInt32,ICorDebugSharp.mdMemberRef@)" />
		public unsafe mdMemberRef FindMemberRef(mdTypeRef td, string szName, nint pvSigBlob, int cbSigBlob)
		{
			Marshal.ThrowExceptionForHR(instance.TryFindMemberRef(td, szName, (byte*)pvSigBlob, checked((uint)cbSigBlob), out var pmr));
			return pmr;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryFindMethod(ICorDebugSharp.mdTypeDef,System.String,System.Byte*,System.UInt32,ICorDebugSharp.mdMethodDef@)" />
		public unsafe mdMethodDef FindMethod(mdTypeDef td, string szName, nint pvSigBlob, int cbSigBlob)
		{
			Marshal.ThrowExceptionForHR(instance.TryFindMethod(td, szName, (byte*)pvSigBlob, checked((uint)cbSigBlob), out var pmb));
			return pmb;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryFindTypeDefByName(System.String,ICorDebugSharp.mdToken,ICorDebugSharp.mdTypeDef@)" />
		public mdTypeDef FindTypeDefByName(string szTypeDef, mdToken tkEnclosingClass)
		{
			Marshal.ThrowExceptionForHR(instance.TryFindTypeDefByName(szTypeDef, tkEnclosingClass, out var ptd));
			return ptd;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryFindTypeRef(ICorDebugSharp.mdToken,System.String,ICorDebugSharp.mdTypeRef@)" />
		public mdTypeRef FindTypeRef(mdToken tkResolutionScope, string szName)
		{
			Marshal.ThrowExceptionForHR(instance.TryFindTypeRef(tkResolutionScope, szName, out var ptr));
			return ptr;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetClassLayout(ICorDebugSharp.mdTypeDef,System.UInt32@,ICorDebugSharp.COR_FIELD_OFFSET@,System.UInt32,System.UInt32@,System.UInt32@)" />
		public (int pdwPackSize, COR_FIELD_OFFSET rFieldOffset, int pcFieldOffset, int pulClassSize) GetClassLayout(mdTypeDef td)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetClassLayout(td, out var pdwPackSize, out var rFieldOffset, 1u, out var pcFieldOffset, out var pulClassSize));
			return checked((pdwPackSize: (int)pdwPackSize, rFieldOffset: rFieldOffset, pcFieldOffset: (int)pcFieldOffset, pulClassSize: (int)pulClassSize));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetCustomAttributeByName(ICorDebugSharp.mdToken,System.String,System.IntPtr@,System.UInt32@)" />
		public (nint ppData, int pcbData) GetCustomAttributeByName(mdToken tkObj, string szName)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetCustomAttributeByName(tkObj, szName, out var ppData, out var pcbData));
			return (ppData: ppData, pcbData: checked((int)pcbData));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetCustomAttributeProps(ICorDebugSharp.mdCustomAttribute,ICorDebugSharp.mdToken@,ICorDebugSharp.mdToken@,System.IntPtr@,System.UInt32@)" />
		public (mdToken ptkObj, mdToken ptkType, nint ppBlob, int pcbSize) GetCustomAttributeProps(mdCustomAttribute cv)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetCustomAttributeProps(cv, out var ptkObj, out var ptkType, out var ppBlob, out var pcbSize));
			return (ptkObj: ptkObj, ptkType: ptkType, ppBlob: ppBlob, pcbSize: checked((int)pcbSize));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetEventProps(ICorDebugSharp.mdEvent,ICorDebugSharp.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.CorEventAttr@,ICorDebugSharp.mdToken@,ICorDebugSharp.mdMethodDef@,ICorDebugSharp.mdMethodDef@,ICorDebugSharp.mdMethodDef@,ICorDebugSharp.mdMethodDef@,System.UInt32,System.UInt32@)" />
		public (mdTypeDef pClass, int pchEvent, CorEventAttr pdwEventFlags, mdToken ptkEventType, mdMethodDef pmdAddOn, mdMethodDef pmdRemoveOn, mdMethodDef pmdFire, mdMethodDef rmdOtherMethod, int pcOtherMethod) GetEventProps(mdEvent ev, char[]? szEvent, int cchEvent)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetEventProps(ev, out var pClass, szEvent, (uint)cchEvent, out var pchEvent, out var pdwEventFlags, out var ptkEventType, out var pmdAddOn, out var pmdRemoveOn, out var pmdFire, out var rmdOtherMethod, 1u, out var pcOtherMethod));
				return (pClass: pClass, pchEvent: (int)pchEvent, pdwEventFlags: pdwEventFlags, ptkEventType: ptkEventType, pmdAddOn: pmdAddOn, pmdRemoveOn: pmdRemoveOn, pmdFire: pmdFire, rmdOtherMethod: rmdOtherMethod, pcOtherMethod: (int)pcOtherMethod);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetFieldMarshal(ICorDebugSharp.mdToken,System.Byte*@,System.UInt32@)" />
		public unsafe (nint ppvNativeType, int pcbNativeType) GetFieldMarshal(mdToken tk)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetFieldMarshal(tk, out var ppvNativeType, out var pcbNativeType));
			return (ppvNativeType: (nint)ppvNativeType, pcbNativeType: checked((int)pcbNativeType));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetFieldProps(ICorDebugSharp.mdFieldDef,ICorDebugSharp.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.CorFieldAttr@,System.Byte*@,System.UInt32@,ICorDebugSharp.CorElementType@,System.IntPtr@,System.UInt32@)" />
		public unsafe (mdTypeDef pClass, int pchField, CorFieldAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) GetFieldProps(mdFieldDef mb, char[]? szField, int cchField)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetFieldProps(mb, out var pClass, szField, (uint)cchField, out var pchField, out var pdwAttr, out var ppvSigBlob, out var pcbSigBlob, out var pdwCPlusTypeFlag, out var ppValue, out var pcchValue));
				return (pClass: pClass, pchField: (int)pchField, pdwAttr: pdwAttr, ppvSigBlob: unchecked((nint)ppvSigBlob), pcbSigBlob: (int)pcbSigBlob, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppValue: ppValue, pcchValue: (int)pcchValue);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetInterfaceImplProps(ICorDebugSharp.mdInterfaceImpl,ICorDebugSharp.mdTypeDef@,ICorDebugSharp.mdToken@)" />
		public (mdTypeDef pClass, mdToken ptkIface) GetInterfaceImplProps(mdInterfaceImpl iiImpl)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetInterfaceImplProps(iiImpl, out var pClass, out var ptkIface));
			return (pClass: pClass, ptkIface: ptkIface);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetMemberProps(ICorDebugSharp.mdToken,ICorDebugSharp.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,System.UInt32@,System.Byte*@,System.UInt32@,System.UInt32@,System.UInt32@,ICorDebugSharp.CorElementType@,System.IntPtr@,System.UInt32@)" />
		public unsafe (mdTypeDef pClass, int pchMember, int pdwAttr, nint ppvSigBlob, int pcbSigBlob, int pulCodeRVA, int pdwImplFlags, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) GetMemberProps(mdToken mb, char[]? szMember, int cchMember)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetMemberProps(mb, out var pClass, szMember, (uint)cchMember, out var pchMember, out var pdwAttr, out var ppvSigBlob, out var pcbSigBlob, out var pulCodeRVA, out var pdwImplFlags, out var pdwCPlusTypeFlag, out var ppValue, out var pcchValue));
				return (pClass: pClass, pchMember: (int)pchMember, pdwAttr: (int)pdwAttr, ppvSigBlob: unchecked((nint)ppvSigBlob), pcbSigBlob: (int)pcbSigBlob, pulCodeRVA: (int)pulCodeRVA, pdwImplFlags: (int)pdwImplFlags, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppValue: ppValue, pcchValue: (int)pcchValue);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetMemberRefProps(ICorDebugSharp.mdMemberRef,ICorDebugSharp.mdToken@,System.Char[],System.UInt32,System.UInt32@,System.Byte*@,System.UInt32@)" />
		public unsafe (mdToken ptk, int pchMember, nint ppvSigBlob, int pbSig) GetMemberRefProps(mdMemberRef mr, char[]? szMember, int cchMember)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetMemberRefProps(mr, out var ptk, szMember, (uint)cchMember, out var pchMember, out var ppvSigBlob, out var pbSig));
				return (ptk: ptk, pchMember: (int)pchMember, ppvSigBlob: unchecked((nint)ppvSigBlob), pbSig: (int)pbSig);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetMethodProps(ICorDebugSharp.mdMethodDef,ICorDebugSharp.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.CorMethodAttr@,System.Byte*@,System.UInt32@,System.UInt32@,System.UInt32@)" />
		public unsafe (mdTypeDef pClass, int pchMethod, CorMethodAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, int pulCodeRVA, int pdwImplFlags) GetMethodProps(mdMethodDef mb, char[]? szMethod, int cchMethod)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetMethodProps(mb, out var pClass, szMethod, (uint)cchMethod, out var pchMethod, out var pdwAttr, out var ppvSigBlob, out var pcbSigBlob, out var pulCodeRVA, out var pdwImplFlags));
				return (pClass: pClass, pchMethod: (int)pchMethod, pdwAttr: pdwAttr, ppvSigBlob: unchecked((nint)ppvSigBlob), pcbSigBlob: (int)pcbSigBlob, pulCodeRVA: (int)pulCodeRVA, pdwImplFlags: (int)pdwImplFlags);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetMethodSemantics(ICorDebugSharp.mdMethodDef,ICorDebugSharp.mdToken,System.UInt32@)" />
		public int GetMethodSemantics(mdMethodDef mb, mdToken tkEventProp)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetMethodSemantics(mb, tkEventProp, out var pdwSemanticsFlags));
			return checked((int)pdwSemanticsFlags);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetModuleFromScope(ICorDebugSharp.mdModule@)" />
		public mdModule ModuleFromScope
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetModuleFromScope(out var pmd));
				return pmd;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetModuleRefProps(ICorDebugSharp.mdModuleRef,System.Char[],System.UInt32,System.UInt32@)" />
		public int GetModuleRefProps(mdModuleRef mur, char[]? szName, int cchName)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetModuleRefProps(mur, szName, (uint)cchName, out var pchName));
				return (int)pchName;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetNameFromToken(ICorDebugSharp.mdToken,System.SByte*@)" />
		public unsafe sbyte* GetNameFromToken(mdToken tk)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetNameFromToken(tk, out var pszUtf8NamePtr));
			return pszUtf8NamePtr;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetNativeCallConvFromSig(System.IntPtr,System.UInt32,System.UInt32@)" />
		public int GetNativeCallConvFromSig(nint pvSig, int cbSig)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetNativeCallConvFromSig(pvSig, (uint)cbSig, out var pCallConv));
				return (int)pCallConv;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetNestedClassProps(ICorDebugSharp.mdTypeDef,ICorDebugSharp.mdTypeDef@)" />
		public mdTypeDef GetNestedClassProps(mdTypeDef tdNestedClass)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetNestedClassProps(tdNestedClass, out var ptdEnclosingClass));
			return ptdEnclosingClass;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetParamForMethodIndex(ICorDebugSharp.mdMethodDef,System.UInt32,ICorDebugSharp.mdParamDef@)" />
		public mdParamDef GetParamForMethodIndex(mdMethodDef md, int ulParamSeq)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetParamForMethodIndex(md, checked((uint)ulParamSeq), out var ppd));
			return ppd;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetParamProps(ICorDebugSharp.mdParamDef,ICorDebugSharp.mdMethodDef@,System.UInt32@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.CorParamAttr@,ICorDebugSharp.CorElementType@,System.IntPtr@,System.UInt32@)" />
		public (mdMethodDef pmd, int pulSequence, int pchName, CorParamAttr pdwAttr, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) GetParamProps(mdParamDef tk, char[]? szName, int cchName)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetParamProps(tk, out var pmd, out var pulSequence, szName, (uint)cchName, out var pchName, out var pdwAttr, out var pdwCPlusTypeFlag, out var ppValue, out var pcchValue));
				return (pmd: pmd, pulSequence: (int)pulSequence, pchName: (int)pchName, pdwAttr: pdwAttr, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppValue: ppValue, pcchValue: (int)pcchValue);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetPermissionSetProps(ICorDebugSharp.mdPermission,System.UInt32@,System.IntPtr@,System.UInt32@)" />
		public (int pdwAction, nint ppvPermission, int pcbPermission) GetPermissionSetProps(mdPermission pm)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetPermissionSetProps(pm, out var pdwAction, out var ppvPermission, out var pcbPermission));
			return checked((pdwAction: (int)pdwAction, ppvPermission: ppvPermission, pcbPermission: (int)pcbPermission));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetPinvokeMap(ICorDebugSharp.mdToken,System.UInt32@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.mdModuleRef@)" />
		public (int pdwMappingFlags, int pchImportName, mdModuleRef pmrImportDLL) GetPinvokeMap(mdToken tk, char[]? szImportName, int cchImportName)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetPinvokeMap(tk, out var pdwMappingFlags, szImportName, (uint)cchImportName, out var pchImportName, out var pmrImportDLL));
				return (pdwMappingFlags: (int)pdwMappingFlags, pchImportName: (int)pchImportName, pmrImportDLL: pmrImportDLL);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetPropertyProps(ICorDebugSharp.mdProperty,ICorDebugSharp.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.CorPropertyAttr@,System.Byte*@,System.UInt32@,ICorDebugSharp.CorElementType@,System.IntPtr@,System.UInt32@,ICorDebugSharp.mdMethodDef@,ICorDebugSharp.mdMethodDef@,ICorDebugSharp.mdMethodDef@,System.UInt32,System.UInt32@)" />
		public unsafe (mdTypeDef pClass, int pchProperty, CorPropertyAttr pdwPropFlags, nint ppvSig, int pbSig, CorElementType pdwCPlusTypeFlag, nint ppDefaultValue, int pcchDefaultValue, mdMethodDef pmdSetter, mdMethodDef pmdGetter, mdMethodDef rmdOtherMethod, int pcOtherMethod) GetPropertyProps(mdProperty prop, char[]? szProperty, int cchProperty)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetPropertyProps(prop, out var pClass, szProperty, (uint)cchProperty, out var pchProperty, out var pdwPropFlags, out var ppvSig, out var pbSig, out var pdwCPlusTypeFlag, out var ppDefaultValue, out var pcchDefaultValue, out var pmdSetter, out var pmdGetter, out var rmdOtherMethod, 1u, out var pcOtherMethod));
				return (pClass: pClass, pchProperty: (int)pchProperty, pdwPropFlags: pdwPropFlags, ppvSig: unchecked((nint)ppvSig), pbSig: (int)pbSig, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppDefaultValue: ppDefaultValue, pcchDefaultValue: (int)pcchDefaultValue, pmdSetter: pmdSetter, pmdGetter: pmdGetter, rmdOtherMethod: rmdOtherMethod, pcOtherMethod: (int)pcOtherMethod);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetRVA(ICorDebugSharp.mdToken,System.UInt32@,System.UInt32@)" />
		public (int pulCodeRVA, int pdwImplFlags) GetRVA(mdToken tk)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetRVA(tk, out var pulCodeRVA, out var pdwImplFlags));
			return checked((pulCodeRVA: (int)pulCodeRVA, pdwImplFlags: (int)pdwImplFlags));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetScopeProps(System.Char[],System.UInt32,System.UInt32@,System.Guid@)" />
		public (int pchName, Guid pmvid) GetScopeProps(char[]? szName, int cchName)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetScopeProps(szName, (uint)cchName, out var pchName, out var pmvid));
				return (pchName: (int)pchName, pmvid: pmvid);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetSigFromToken(ICorDebugSharp.mdSignature,System.Byte*@,System.UInt32@)" />
		public unsafe (nint ppvSig, int pcbSig) GetSigFromToken(mdSignature mdSig)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetSigFromToken(mdSig, out var ppvSig, out var pcbSig));
			return (ppvSig: (nint)ppvSig, pcbSig: checked((int)pcbSig));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetTypeDefProps(ICorDebugSharp.mdTypeDef,System.Char[],System.UInt32,System.UInt32@,ICorDebugSharp.CorTypeAttr@,ICorDebugSharp.mdToken@)" />
		public (int pchTypeDef, CorTypeAttr pdwTypeDefFlags, mdToken ptkExtends) GetTypeDefProps(mdTypeDef td, char[]? szTypeDef, int cchTypeDef)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetTypeDefProps(td, szTypeDef, (uint)cchTypeDef, out var pchTypeDef, out var pdwTypeDefFlags, out var ptkExtends));
				return (pchTypeDef: (int)pchTypeDef, pdwTypeDefFlags: pdwTypeDefFlags, ptkExtends: ptkExtends);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetTypeRefProps(ICorDebugSharp.mdTypeRef,ICorDebugSharp.mdToken@,System.Char[],System.UInt32,System.UInt32@)" />
		public (mdToken ptkResolutionScope, int pchName) GetTypeRefProps(mdTypeRef tr, char[]? szName, int cchName)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetTypeRefProps(tr, out var ptkResolutionScope, szName, (uint)cchName, out var pchName));
				return (ptkResolutionScope: ptkResolutionScope, pchName: (int)pchName);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetTypeSpecFromToken(ICorDebugSharp.mdTypeSpec,System.Byte*@,System.UInt32@)" />
		public unsafe (nint ppvSig, int pcbSig) GetTypeSpecFromToken(mdTypeSpec typespec)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetTypeSpecFromToken(typespec, out var ppvSig, out var pcbSig));
			return (ppvSig: (nint)ppvSig, pcbSig: checked((int)pcbSig));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryGetUserString(ICorDebugSharp.mdString,System.Char[],System.UInt32,System.UInt32@)" />
		public int GetUserString(mdString stk, char[]? szString, int cchString)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetUserString(stk, szString, (uint)cchString, out var pchString));
				return (int)pchString;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryIsGlobal(ICorDebugSharp.mdToken,System.Int32@)" />
		public int IsGlobal(mdToken pd)
		{
			Marshal.ThrowExceptionForHR(instance.TryIsGlobal(pd, out var pbGlobal));
			return pbGlobal;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryResolveTypeRef(ICorDebugSharp.mdTypeRef,System.Guid@,System.IntPtr@,ICorDebugSharp.mdTypeDef@)" />
		public (nint ppIScope, mdTypeDef ptd) ResolveTypeRef(mdTypeRef tr, ref Guid riid)
		{
			Marshal.ThrowExceptionForHR(instance.TryResolveTypeRef(tr, ref riid, out var ppIScope, out var ptd));
			return (ppIScope: ppIScope, ptd: ptd);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataImport.TryResetEnum(ICorDebugSharp.HCorEnum,System.UInt32)" />
		public void ResetEnum(HCorEnum hEnum, int ulPos)
		{
			Marshal.ThrowExceptionForHR(instance.TryResetEnum(hEnum, checked((uint)ulPos)));
		}
	}

	private static IEnumerable<mdTypeDef> EnumerateTypeDefsCore(IMetaDataImport instance)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumTypeDefs(ref handle, out var rTypeDefs, 1u, out var pcTypeDefs));
				switch (pcTypeDefs)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rTypeDefs;
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

	private static IEnumerable<mdInterfaceImpl> EnumerateInterfaceImplsCore(IMetaDataImport instance, mdTypeDef td)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumInterfaceImpls(ref handle, td, out var rImpls, 1u, out var pcImpls));
				switch (pcImpls)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rImpls;
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

	private static IEnumerable<mdTypeRef> EnumerateTypeRefsCore(IMetaDataImport instance)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumTypeRefs(ref handle, out var rTypeRefs, 1u, out var pcTypeRefs));
				switch (pcTypeRefs)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rTypeRefs;
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

	private static IEnumerable<mdToken> EnumerateMembersCore(IMetaDataImport instance, mdTypeDef cl)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumMembers(ref handle, cl, out var rMembers, 1u, out var pcTokens));
				switch (pcTokens)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rMembers;
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

	private static IEnumerable<mdToken> EnumerateMembersWithNameCore(IMetaDataImport instance, mdTypeDef cl, string szName)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumMembersWithName(ref handle, cl, szName, out var rMembers, 1u, out var pcTokens));
				switch (pcTokens)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rMembers;
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

	private static IEnumerable<mdMethodDef> EnumerateMethodsCore(IMetaDataImport instance, mdTypeDef cl)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumMethods(ref handle, cl, out var rMethods, 1u, out var pcTokens));
				switch (pcTokens)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rMethods;
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

	private static IEnumerable<mdMethodDef> EnumerateMethodsWithNameCore(IMetaDataImport instance, mdTypeDef cl, string szName)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumMethodsWithName(ref handle, cl, szName, out var rMethods, 1u, out var pcTokens));
				switch (pcTokens)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rMethods;
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

	private static IEnumerable<mdFieldDef> EnumerateFieldsCore(IMetaDataImport instance, mdTypeDef cl)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumFields(ref handle, cl, out var rFields, 1u, out var pcTokens));
				switch (pcTokens)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rFields;
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

	private static IEnumerable<mdFieldDef> EnumerateFieldsWithNameCore(IMetaDataImport instance, mdTypeDef cl, string szName)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumFieldsWithName(ref handle, cl, szName, out var rFields, 1u, out var pcTokens));
				switch (pcTokens)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rFields;
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

	private static IEnumerable<mdParamDef> EnumerateParamsCore(IMetaDataImport instance, mdMethodDef mb)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumParams(ref handle, mb, out var rParams, 1u, out var pcTokens));
				switch (pcTokens)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rParams;
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

	private static IEnumerable<mdMemberRef> EnumerateMemberRefsCore(IMetaDataImport instance, mdToken tkParent)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumMemberRefs(ref handle, tkParent, out var rMemberRefs, 1u, out var pcTokens));
				switch (pcTokens)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rMemberRefs;
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

	private static IEnumerable<(mdToken MethodBody, mdToken MethodDeclaration)> EnumerateMethodImplsCore(IMetaDataImport instance, mdTypeDef td)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumMethodImpls(ref handle, td, out var rMethodBody, out var rMethodDecl, 1u, out var pcTokens));
				switch (pcTokens)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return (MethodBody: rMethodBody, MethodDeclaration: rMethodDecl);
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

	private static IEnumerable<mdPermission> EnumeratePermissionSetsCore(IMetaDataImport instance, mdToken tk, int dwActions)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumPermissionSets(ref handle, tk, checked((uint)dwActions), out var rPermission, 1u, out var pcTokens));
				switch (pcTokens)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rPermission;
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

	private static IEnumerable<mdProperty> EnumeratePropertiesCore(IMetaDataImport instance, mdTypeDef td)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumProperties(ref handle, td, out var rProperties, 1u, out var pcProperties));
				switch (pcProperties)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rProperties;
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

	private static IEnumerable<mdEvent> EnumerateEventsCore(IMetaDataImport instance, mdTypeDef td)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumEvents(ref handle, td, out var rEvents, 1u, out var pcEvents));
				switch (pcEvents)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rEvents;
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

	private static IEnumerable<mdToken> EnumerateMethodSemanticsCore(IMetaDataImport instance, mdMethodDef mb)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumMethodSemantics(ref handle, mb, out var rEventProp, 1u, out var pcEventProp));
				switch (pcEventProp)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rEventProp;
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

	private static IEnumerable<mdModuleRef> EnumerateModuleRefsCore(IMetaDataImport instance)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumModuleRefs(ref handle, out var rModuleRefs, 1u, out var pcModuleRefs));
				switch (pcModuleRefs)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rModuleRefs;
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

	private static IEnumerable<mdToken> EnumerateUnresolvedMethodsCore(IMetaDataImport instance)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumUnresolvedMethods(ref handle, out var rMethods, 1u, out var pcTokens));
				switch (pcTokens)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rMethods;
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

	private static IEnumerable<mdSignature> EnumerateSignaturesCore(IMetaDataImport instance)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumSignatures(ref handle, out var rSignatures, 1u, out var pcSignatures));
				switch (pcSignatures)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rSignatures;
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

	private static IEnumerable<mdTypeSpec> EnumerateTypeSpecsCore(IMetaDataImport instance)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumTypeSpecs(ref handle, out var rTypeSpecs, 1u, out var pcTypeSpecs));
				switch (pcTypeSpecs)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rTypeSpecs;
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

	private static IEnumerable<mdString> EnumerateUserStringsCore(IMetaDataImport instance)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumUserStrings(ref handle, out var rStrings, 1u, out var pcStrings));
				switch (pcStrings)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rStrings;
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

	private static IEnumerable<mdCustomAttribute> EnumerateCustomAttributesCore(IMetaDataImport instance, mdToken tk, mdToken tkType)
	{
		HCorEnum handle = default;
		try
		{
			while (true)
			{
				Marshal.ThrowExceptionForHR(instance.TryEnumCustomAttributes(ref handle, tk, tkType, out var rCustomAttributes, 1u, out var pcCustomAttributes));
				switch (pcCustomAttributes)
				{
				default:
					throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
				case 1u:
					yield return rCustomAttributes;
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

	private static (string szName, Guid pmvid) QueryGetScopeProps(IMetaDataImport instance)
	{
		Marshal.ThrowExceptionForHR(instance.TryGetScopeProps(null, 0u, out var pchName, out var _));
		if (pchName == 0)
		{
			return (szName: string.Empty, pmvid: default);
		}
		for (int i = 0; i < 3; i++)
		{
			uint num = pchName;
			char[] array = new char[checked((int)num)];
			Marshal.ThrowExceptionForHR(instance.TryGetScopeProps(array, num, out var pchName2, out var pmvid2));
			if (pchName2 > num)
			{
				pchName = pchName2;
				continue;
			}
			return (szName: CreateString(array, pchName2, trimTerminator: true), pmvid: pmvid2);
		}
		throw new InvalidOperationException("Native buffer size did not stabilize.");
	}

	private static (string szTypeDef, CorTypeAttr pdwTypeDefFlags, mdToken ptkExtends) QueryGetTypeDefProps(IMetaDataImport instance, mdTypeDef typeDefinition)
	{
		Marshal.ThrowExceptionForHR(instance.TryGetTypeDefProps(typeDefinition, null, 0u, out var pchTypeDef, out var _, out var _));
		if (pchTypeDef == 0)
		{
			return (szTypeDef: string.Empty, pdwTypeDefFlags: CorTypeAttr.tdNotPublic, ptkExtends: default);
		}
		for (int i = 0; i < 3; i++)
		{
			uint num = pchTypeDef;
			char[] array = new char[checked((int)num)];
			Marshal.ThrowExceptionForHR(instance.TryGetTypeDefProps(typeDefinition, array, num, out var pchTypeDef2, out var pdwTypeDefFlags2, out var ptkExtends2));
			if (pchTypeDef2 > num)
			{
				pchTypeDef = pchTypeDef2;
				continue;
			}
			return (szTypeDef: CreateString(array, pchTypeDef2, trimTerminator: true), pdwTypeDefFlags: pdwTypeDefFlags2, ptkExtends: ptkExtends2);
		}
		throw new InvalidOperationException("Native buffer size did not stabilize.");
	}

	private static (mdToken ptkResolutionScope, string szName) QueryGetTypeRefProps(IMetaDataImport instance, mdTypeRef typeReference)
	{
		Marshal.ThrowExceptionForHR(instance.TryGetTypeRefProps(typeReference, out var _, null, 0u, out var pchName));
		if (pchName == 0)
		{
			return (ptkResolutionScope: default, szName: string.Empty);
		}
		for (int i = 0; i < 3; i++)
		{
			uint num = pchName;
			char[] array = new char[checked((int)num)];
			Marshal.ThrowExceptionForHR(instance.TryGetTypeRefProps(typeReference, out var ptkResolutionScope2, array, num, out var pchName2));
			if (pchName2 > num)
			{
				pchName = pchName2;
				continue;
			}
			return (ptkResolutionScope: ptkResolutionScope2, szName: CreateString(array, pchName2, trimTerminator: true));
		}
		throw new InvalidOperationException("Native buffer size did not stabilize.");
	}

	private static string QueryGetModuleRefProps(IMetaDataImport instance, mdModuleRef moduleReference)
	{
		Marshal.ThrowExceptionForHR(instance.TryGetModuleRefProps(moduleReference, null, 0u, out var pchName));
		if (pchName == 0)
		{
			return string.Empty;
		}
		for (int i = 0; i < 3; i++)
		{
			uint num = pchName;
			char[] array = new char[checked((int)num)];
			Marshal.ThrowExceptionForHR(instance.TryGetModuleRefProps(moduleReference, array, num, out var pchName2));
			if (pchName2 > num)
			{
				pchName = pchName2;
				continue;
			}
			return CreateString(array, pchName2, trimTerminator: true);
		}
		throw new InvalidOperationException("Native buffer size did not stabilize.");
	}

	private static string QueryGetUserString(IMetaDataImport instance, mdString userString)
	{
		Marshal.ThrowExceptionForHR(instance.TryGetUserString(userString, null, 0u, out var pchString));
		if (pchString == 0)
		{
			return string.Empty;
		}
		for (int i = 0; i < 3; i++)
		{
			uint num = pchString;
			char[] array = new char[checked((int)num)];
			Marshal.ThrowExceptionForHR(instance.TryGetUserString(userString, array, num, out var pchString2));
			if (pchString2 > num)
			{
				pchString = pchString2;
				continue;
			}
			return CreateString(array, pchString2, trimTerminator: false);
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
