#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class MetaDataImportExtensions
{
    extension(IMetaDataImport instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryEnumTypeDefs(Passenger.Debugger.Interop.HCorEnum@,Passenger.Debugger.Interop.mdTypeDef@,System.UInt32,System.UInt32@)" />
        public IEnumerable<mdTypeDef> EnumTypeDefs()
        {
            return EnumerateTypeDefsCore(instance);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryEnumInterfaceImpls(Passenger.Debugger.Interop.HCorEnum@,Passenger.Debugger.Interop.mdTypeDef,Passenger.Debugger.Interop.mdInterfaceImpl@,System.UInt32,System.UInt32@)" />
        public IEnumerable<mdInterfaceImpl> EnumInterfaceImpls(mdTypeDef td)
        {
            return EnumerateInterfaceImplsCore(instance, td);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryEnumMethods(Passenger.Debugger.Interop.HCorEnum@,Passenger.Debugger.Interop.mdTypeDef,Passenger.Debugger.Interop.mdMethodDef@,System.UInt32,System.UInt32@)" />
        public IEnumerable<mdMethodDef> EnumMethods(mdTypeDef cl)
        {
            return EnumerateMethodsCore(instance, cl);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryEnumMethodsWithName(Passenger.Debugger.Interop.HCorEnum@,Passenger.Debugger.Interop.mdTypeDef,System.String,Passenger.Debugger.Interop.mdMethodDef@,System.UInt32,System.UInt32@)" />
        public IEnumerable<mdMethodDef> EnumMethodsWithName(mdTypeDef cl, string szName)
        {
            return EnumerateMethodsWithNameCore(instance, cl, szName);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryEnumFields(Passenger.Debugger.Interop.HCorEnum@,Passenger.Debugger.Interop.mdTypeDef,Passenger.Debugger.Interop.mdFieldDef@,System.UInt32,System.UInt32@)" />
        public IEnumerable<mdFieldDef> EnumFields(mdTypeDef cl)
        {
            return EnumerateFieldsCore(instance, cl);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryEnumFieldsWithName(Passenger.Debugger.Interop.HCorEnum@,Passenger.Debugger.Interop.mdTypeDef,System.String,Passenger.Debugger.Interop.mdFieldDef@,System.UInt32,System.UInt32@)" />
        public IEnumerable<mdFieldDef> EnumFieldsWithName(mdTypeDef cl, string szName)
        {
            return EnumerateFieldsWithNameCore(instance, cl, szName);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryEnumProperties(Passenger.Debugger.Interop.HCorEnum@,Passenger.Debugger.Interop.mdTypeDef,Passenger.Debugger.Interop.mdProperty@,System.UInt32,System.UInt32@)" />
        public IEnumerable<mdProperty> EnumProperties(mdTypeDef td)
        {
            return EnumeratePropertiesCore(instance, td);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetScopeProps(System.Char[],System.UInt32,System.UInt32@,System.Guid@)" />
        public (string szName, Guid pmvid) ScopeProps
        {
            get
            {
                return QueryGetScopeProps(instance);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetTypeDefProps(Passenger.Debugger.Interop.mdTypeDef,System.Char[],System.UInt32,System.UInt32@,Passenger.Debugger.Interop.CorTypeAttr@,Passenger.Debugger.Interop.mdToken@)" />
        public (string szTypeDef, CorTypeAttr pdwTypeDefFlags, mdToken ptkExtends) GetTypeDefProps(mdTypeDef typeDefinition)
        {
            return QueryGetTypeDefProps(instance, typeDefinition);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetTypeRefProps(Passenger.Debugger.Interop.mdTypeRef,Passenger.Debugger.Interop.mdToken@,System.Char[],System.UInt32,System.UInt32@)" />
        public (mdToken ptkResolutionScope, string szName) GetTypeRefProps(mdTypeRef typeReference)
        {
            return QueryGetTypeRefProps(instance, typeReference);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetFieldProps(Passenger.Debugger.Interop.mdFieldDef,Passenger.Debugger.Interop.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,Passenger.Debugger.Interop.CorFieldAttr@,System.Byte*@,System.UInt32@,Passenger.Debugger.Interop.CorElementType@,System.IntPtr@,System.UInt32@)" />
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

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetMethodProps(Passenger.Debugger.Interop.mdMethodDef,Passenger.Debugger.Interop.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,Passenger.Debugger.Interop.CorMethodAttr@,System.Byte*@,System.UInt32@,System.UInt32@,System.UInt32@)" />
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

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetParamProps(Passenger.Debugger.Interop.mdParamDef,Passenger.Debugger.Interop.mdMethodDef@,System.UInt32@,System.Char[],System.UInt32,System.UInt32@,Passenger.Debugger.Interop.CorParamAttr@,Passenger.Debugger.Interop.CorElementType@,System.IntPtr@,System.UInt32@)" />
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

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetPropertyProps(Passenger.Debugger.Interop.mdProperty,Passenger.Debugger.Interop.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,Passenger.Debugger.Interop.CorPropertyAttr@,System.Byte*@,System.UInt32@,Passenger.Debugger.Interop.CorElementType@,System.IntPtr@,System.UInt32@,Passenger.Debugger.Interop.mdMethodDef@,Passenger.Debugger.Interop.mdMethodDef@,Passenger.Debugger.Interop.mdMethodDef@,System.UInt32,System.UInt32@)" />
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

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryEnumFields(Passenger.Debugger.Interop.HCorEnum@,Passenger.Debugger.Interop.mdTypeDef,Passenger.Debugger.Interop.mdFieldDef@,System.UInt32,System.UInt32@)" />
        public (mdFieldDef rFields, int pcTokens) EnumFields(ref HCorEnum phEnum, mdTypeDef cl)
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumFields(ref phEnum, cl, out var rFields, 1u, out var pcTokens));
            return (rFields: rFields, pcTokens: checked((int)pcTokens));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryEnumFieldsWithName(Passenger.Debugger.Interop.HCorEnum@,Passenger.Debugger.Interop.mdTypeDef,System.String,Passenger.Debugger.Interop.mdFieldDef@,System.UInt32,System.UInt32@)" />
        public (mdFieldDef rFields, int pcTokens) EnumFieldsWithName(ref HCorEnum phEnum, mdTypeDef cl, string szName)
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumFieldsWithName(ref phEnum, cl, szName, out var rFields, 1u, out var pcTokens));
            return (rFields: rFields, pcTokens: checked((int)pcTokens));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryEnumInterfaceImpls(Passenger.Debugger.Interop.HCorEnum@,Passenger.Debugger.Interop.mdTypeDef,Passenger.Debugger.Interop.mdInterfaceImpl@,System.UInt32,System.UInt32@)" />
        public (mdInterfaceImpl rImpls, int pcImpls) EnumInterfaceImpls(ref HCorEnum phEnum, mdTypeDef td)
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumInterfaceImpls(ref phEnum, td, out var rImpls, 1u, out var pcImpls));
            return (rImpls: rImpls, pcImpls: checked((int)pcImpls));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryEnumMethods(Passenger.Debugger.Interop.HCorEnum@,Passenger.Debugger.Interop.mdTypeDef,Passenger.Debugger.Interop.mdMethodDef@,System.UInt32,System.UInt32@)" />
        public (mdMethodDef rMethods, int pcTokens) EnumMethods(ref HCorEnum phEnum, mdTypeDef cl)
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumMethods(ref phEnum, cl, out var rMethods, 1u, out var pcTokens));
            return (rMethods: rMethods, pcTokens: checked((int)pcTokens));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryEnumMethodsWithName(Passenger.Debugger.Interop.HCorEnum@,Passenger.Debugger.Interop.mdTypeDef,System.String,Passenger.Debugger.Interop.mdMethodDef@,System.UInt32,System.UInt32@)" />
        public (mdMethodDef rMethods, int pcTokens) EnumMethodsWithName(ref HCorEnum phEnum, mdTypeDef cl, string szName)
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumMethodsWithName(ref phEnum, cl, szName, out var rMethods, 1u, out var pcTokens));
            return (rMethods: rMethods, pcTokens: checked((int)pcTokens));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryEnumProperties(Passenger.Debugger.Interop.HCorEnum@,Passenger.Debugger.Interop.mdTypeDef,Passenger.Debugger.Interop.mdProperty@,System.UInt32,System.UInt32@)" />
        public (mdProperty rProperties, int pcProperties) EnumProperties(ref HCorEnum phEnum, mdTypeDef td)
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumProperties(ref phEnum, td, out var rProperties, 1u, out var pcProperties));
            return (rProperties: rProperties, pcProperties: checked((int)pcProperties));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryEnumTypeDefs(Passenger.Debugger.Interop.HCorEnum@,Passenger.Debugger.Interop.mdTypeDef@,System.UInt32,System.UInt32@)" />
        public (mdTypeDef rTypeDefs, int pcTypeDefs) EnumTypeDefs(ref HCorEnum phEnum)
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumTypeDefs(ref phEnum, out var rTypeDefs, 1u, out var pcTypeDefs));
            return (rTypeDefs: rTypeDefs, pcTypeDefs: checked((int)pcTypeDefs));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryFindField(Passenger.Debugger.Interop.mdTypeDef,System.String,System.Byte*,System.UInt32,Passenger.Debugger.Interop.mdFieldDef@)" />
        public unsafe mdFieldDef FindField(mdTypeDef td, string szName, nint pvSigBlob, int cbSigBlob)
        {
            Marshal.ThrowExceptionForHR(instance.TryFindField(td, szName, (byte*)pvSigBlob, checked((uint)cbSigBlob), out var pmb));
            return pmb;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryFindMethod(Passenger.Debugger.Interop.mdTypeDef,System.String,System.Byte*,System.UInt32,Passenger.Debugger.Interop.mdMethodDef@)" />
        public unsafe mdMethodDef FindMethod(mdTypeDef td, string szName, nint pvSigBlob, int cbSigBlob)
        {
            Marshal.ThrowExceptionForHR(instance.TryFindMethod(td, szName, (byte*)pvSigBlob, checked((uint)cbSigBlob), out var pmb));
            return pmb;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetFieldProps(Passenger.Debugger.Interop.mdFieldDef,Passenger.Debugger.Interop.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,Passenger.Debugger.Interop.CorFieldAttr@,System.Byte*@,System.UInt32@,Passenger.Debugger.Interop.CorElementType@,System.IntPtr@,System.UInt32@)" />
        public unsafe (mdTypeDef pClass, int pchField, CorFieldAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) GetFieldProps(mdFieldDef mb, char[]? szField, int cchField)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetFieldProps(mb, out var pClass, szField, (uint)cchField, out var pchField, out var pdwAttr, out var ppvSigBlob, out var pcbSigBlob, out var pdwCPlusTypeFlag, out var ppValue, out var pcchValue));
                return (pClass: pClass, pchField: (int)pchField, pdwAttr: pdwAttr, ppvSigBlob: unchecked((nint)ppvSigBlob), pcbSigBlob: (int)pcbSigBlob, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppValue: ppValue, pcchValue: (int)pcchValue);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetInterfaceImplProps(Passenger.Debugger.Interop.mdInterfaceImpl,Passenger.Debugger.Interop.mdTypeDef@,Passenger.Debugger.Interop.mdToken@)" />
        public (mdTypeDef pClass, mdToken ptkIface) GetInterfaceImplProps(mdInterfaceImpl iiImpl)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetInterfaceImplProps(iiImpl, out var pClass, out var ptkIface));
            return (pClass: pClass, ptkIface: ptkIface);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetMethodProps(Passenger.Debugger.Interop.mdMethodDef,Passenger.Debugger.Interop.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,Passenger.Debugger.Interop.CorMethodAttr@,System.Byte*@,System.UInt32@,System.UInt32@,System.UInt32@)" />
        public unsafe (mdTypeDef pClass, int pchMethod, CorMethodAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, int pulCodeRVA, int pdwImplFlags) GetMethodProps(mdMethodDef mb, char[]? szMethod, int cchMethod)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetMethodProps(mb, out var pClass, szMethod, (uint)cchMethod, out var pchMethod, out var pdwAttr, out var ppvSigBlob, out var pcbSigBlob, out var pulCodeRVA, out var pdwImplFlags));
                return (pClass: pClass, pchMethod: (int)pchMethod, pdwAttr: pdwAttr, ppvSigBlob: unchecked((nint)ppvSigBlob), pcbSigBlob: (int)pcbSigBlob, pulCodeRVA: (int)pulCodeRVA, pdwImplFlags: (int)pdwImplFlags);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetNestedClassProps(Passenger.Debugger.Interop.mdTypeDef,Passenger.Debugger.Interop.mdTypeDef@)" />
        public mdTypeDef GetNestedClassProps(mdTypeDef tdNestedClass)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetNestedClassProps(tdNestedClass, out var ptdEnclosingClass));
            return ptdEnclosingClass;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetParamProps(Passenger.Debugger.Interop.mdParamDef,Passenger.Debugger.Interop.mdMethodDef@,System.UInt32@,System.Char[],System.UInt32,System.UInt32@,Passenger.Debugger.Interop.CorParamAttr@,Passenger.Debugger.Interop.CorElementType@,System.IntPtr@,System.UInt32@)" />
        public (mdMethodDef pmd, int pulSequence, int pchName, CorParamAttr pdwAttr, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) GetParamProps(mdParamDef tk, char[]? szName, int cchName)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetParamProps(tk, out var pmd, out var pulSequence, szName, (uint)cchName, out var pchName, out var pdwAttr, out var pdwCPlusTypeFlag, out var ppValue, out var pcchValue));
                return (pmd: pmd, pulSequence: (int)pulSequence, pchName: (int)pchName, pdwAttr: pdwAttr, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppValue: ppValue, pcchValue: (int)pcchValue);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetPropertyProps(Passenger.Debugger.Interop.mdProperty,Passenger.Debugger.Interop.mdTypeDef@,System.Char[],System.UInt32,System.UInt32@,Passenger.Debugger.Interop.CorPropertyAttr@,System.Byte*@,System.UInt32@,Passenger.Debugger.Interop.CorElementType@,System.IntPtr@,System.UInt32@,Passenger.Debugger.Interop.mdMethodDef@,Passenger.Debugger.Interop.mdMethodDef@,Passenger.Debugger.Interop.mdMethodDef@,System.UInt32,System.UInt32@)" />
        public unsafe (mdTypeDef pClass, int pchProperty, CorPropertyAttr pdwPropFlags, nint ppvSig, int pbSig, CorElementType pdwCPlusTypeFlag, nint ppDefaultValue, int pcchDefaultValue, mdMethodDef pmdSetter, mdMethodDef pmdGetter, mdMethodDef rmdOtherMethod, int pcOtherMethod) GetPropertyProps(mdProperty prop, char[]? szProperty, int cchProperty)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetPropertyProps(prop, out var pClass, szProperty, (uint)cchProperty, out var pchProperty, out var pdwPropFlags, out var ppvSig, out var pbSig, out var pdwCPlusTypeFlag, out var ppDefaultValue, out var pcchDefaultValue, out var pmdSetter, out var pmdGetter, out var rmdOtherMethod, 1u, out var pcOtherMethod));
                return (pClass: pClass, pchProperty: (int)pchProperty, pdwPropFlags: pdwPropFlags, ppvSig: unchecked((nint)ppvSig), pbSig: (int)pbSig, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppDefaultValue: ppDefaultValue, pcchDefaultValue: (int)pcchDefaultValue, pmdSetter: pmdSetter, pmdGetter: pmdGetter, rmdOtherMethod: rmdOtherMethod, pcOtherMethod: (int)pcOtherMethod);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetTypeDefProps(Passenger.Debugger.Interop.mdTypeDef,System.Char[],System.UInt32,System.UInt32@,Passenger.Debugger.Interop.CorTypeAttr@,Passenger.Debugger.Interop.mdToken@)" />
        public (int pchTypeDef, CorTypeAttr pdwTypeDefFlags, mdToken ptkExtends) GetTypeDefProps(mdTypeDef td, char[]? szTypeDef, int cchTypeDef)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetTypeDefProps(td, szTypeDef, (uint)cchTypeDef, out var pchTypeDef, out var pdwTypeDefFlags, out var ptkExtends));
                return (pchTypeDef: (int)pchTypeDef, pdwTypeDefFlags: pdwTypeDefFlags, ptkExtends: ptkExtends);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetTypeRefProps(Passenger.Debugger.Interop.mdTypeRef,Passenger.Debugger.Interop.mdToken@,System.Char[],System.UInt32,System.UInt32@)" />
        public (mdToken ptkResolutionScope, int pchName) GetTypeRefProps(mdTypeRef tr, char[]? szName, int cchName)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetTypeRefProps(tr, out var ptkResolutionScope, szName, (uint)cchName, out var pchName));
                return (ptkResolutionScope: ptkResolutionScope, pchName: (int)pchName);
            }
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
