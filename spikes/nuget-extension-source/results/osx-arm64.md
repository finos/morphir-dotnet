# NuGet extension source spike: osx-arm64

SDK 11.0.100, NuGet client packages 7.9.0, 2026-11-14T06:15Z.

## baseline

- Binary: 3.3 MiB (3500488 bytes)
- Distinct trim/AOT warnings: 0

```
PASS baseline.descriptor-json: 3 artifacts read with System.IO.Compression and System.Text.Json
exit 0
```

## core

- Binary: 9.8 MiB (10287928 bytes)
- Distinct trim/AOT warnings: 0

```
PASS baseline.descriptor-json: 3 artifacts read with System.IO.Compression and System.Text.Json
PASS versioning.parse: normalized 1.2.3-Beta.1, full 1.2.3-Beta.1+build.5, in [1,2): True
PASS configuration.sources: sources spike,private,nuget.org; private credential spike-user (clear text True, length 12)
PASS configuration.source-mapping: enabled True; Acme.Morphir.Python -> spike; Newtonsoft.Json -> nuget.org
PASS packaging.main-package: Acme.Morphir.Python 1.2.3 types DotnetTool,MorphirExtension; artifacts process:acme.morphir.python.osx-arm64.1.2.3.nupkg process:acme.morphir.python.linux-x64.1.2.3.nupkg wasm:acme-python.wasm; tools/any/any/DotnetToolSettings.xml osx-arm64=Acme.Morphir.Python.osx-arm64,linux-x64=Acme.Morphir.Python.linux-x64
PASS packaging.rid-package-subtree: types DotnetToolRidPackage; 6 files under root, outside root: _rels/.rels,Acme.Morphir.Python.osx-arm64.nuspec,morphir/acme-python.wasm,morphir/release.json,[Content_Types].xml,package/services/metadata/core-properties/nuget.psmdcp; tree bef7556afa6f; file f1caa97c2ed0
PASS packaging.signature-read: signed True; primary Author; repository countersignature True; content hash matches signature
PASS packaging.signature-verify: valid True; issues [Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Information,Undefined Information,Undefined Verbose,Undefined Information,Undefined Information,Undefined Verbose,Undefined Minimal,Undefined Information,Undefined Information,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Information,Undefined Information,Undefined Verbose,Undefined Information,Undefined Information,Undefined Verbose]
exit 0
```

## full

- Binary: 18.3 MiB (19225096 bytes)
- Distinct trim/AOT warnings: 119

<details><summary>Warnings by code</summary>

```
  33 IL3050
  27 IL2070
  14 IL2026
  13 IL2075
   7 IL2046
   6 IL2080
   6 IL2067
   4 IL2055
   3 IL2060
   3 IL2057
   2 IL2072
   1 IL2077
```

```
warning IL2026: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetProperties(): Using member 'System.ComponentModel.ICustomTypeDescriptor.GetProperties(Attribute
warning IL2026: Newtonsoft.Json.Serialization.DefaultSerializationBinder.GetGenericTypeFromTypeName(String,Assembly): Using member 'System.Reflection.Assembly.GetType(String)' which has 'RequiresUnreferencedCodeAttribute' can break function
warning IL2026: Newtonsoft.Json.Serialization.DefaultSerializationBinder.GetTypeFromTypeNameKey(StructMultiKey`2<String,String>): Using member 'System.Reflection.Assembly.GetType(String)' which has 'RequiresUnreferencedCodeAttribute' can br
warning IL2026: Newtonsoft.Json.Serialization.JsonTypeReflector.CanTypeDescriptorConvertString(Type,TypeConverter&): Using member 'System.ComponentModel.TypeDescriptor.GetConverter(Type)' which has 'RequiresUnreferencedCodeAttribute' can br
warning IL2026: Newtonsoft.Json.Utilities.ConvertUtils.TryConvertInternal(Object,CultureInfo,Type,Object&): Using member 'System.ComponentModel.TypeDescriptor.GetConverter(Type)' which has 'RequiresUnreferencedCodeAttribute' can break funct
warning IL2026: Newtonsoft.Json.Utilities.DynamicUtils.BinderWrapper.CreateMemberCalls(): Using member 'Microsoft.CSharp.RuntimeBinder.Binder.GetMember(CSharpBinderFlags,String,Type,IEnumerable`1<CSharpArgumentInfo>)' which has 'RequiresUnr
warning IL2026: Newtonsoft.Json.Utilities.DynamicUtils.BinderWrapper.CreateMemberCalls(): Using member 'Microsoft.CSharp.RuntimeBinder.Binder.SetMember(CSharpBinderFlags,String,Type,IEnumerable`1<CSharpArgumentInfo>)' which has 'RequiresUnr
warning IL2026: Newtonsoft.Json.Utilities.FSharpUtils.FSharpUtils(Assembly): Using member 'System.Reflection.Assembly.GetType(String)' which has 'RequiresUnreferencedCodeAttribute' can break functionality when trimming application code. Typ
warning IL2026: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForArrayContract(Type,Type,Type&,ObjectConstructor`1<Object>&): Using member 'System.Reflection.Assembly.GetType(String)' which has 'RequiresUnreferencedCo
warning IL2026: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForDictionaryContract(Type,Type,Type,Type&,ObjectConstructor`1<Object>&): Using member 'System.Reflection.Assembly.GetType(String)' which has 'RequiresUnre
warning IL2026: NuGet.Protocol.Plugins.Sender.SendAsync(Message,CancellationToken): Using member 'NuGet.Protocol.Plugins.JsonSerializationUtilities.Serialize(JsonWriter,Object)' which has 'RequiresUnreferencedCodeAttribute' can break functi
warning IL2026: NuGet.Protocol.Plugins.StandardOutputReceiver.OnLineRead(Object,LineReadEventArgs): Using member 'NuGet.Protocol.Plugins.JsonSerializationUtilities.Deserialize<Message>(String)' which has 'RequiresUnreferencedCodeAttribute' 
warning IL2026: NuGet.Protocol.RepositorySignatureResourceProvider.<>c__DisplayClass4_0.<<GetRepositorySignatureResourceAsync>b__2>d.MoveNext(): Using member 'NuGet.Protocol.RepositorySignatureResource.RepositorySignatureResource(JObject,So
warning IL2026: NuGet.Protocol.ServiceIndexResourceV3Provider.<ConsumeServiceIndexStreamNsjAsync>d__15.MoveNext(): Using member 'NuGet.Protocol.ServiceIndexResourceV3.ServiceIndexResourceV3(JObject,DateTime,PackageSource)' which has 'Requir
warning IL2046: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetConverter(): Interface member 'System.ComponentModel.ICustomTypeDescriptor.GetConverter()' with 'RequiresUnreferencedCodeAttribute' has an implement
warning IL2046: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetDefaultEvent(): Interface member 'System.ComponentModel.ICustomTypeDescriptor.GetDefaultEvent()' with 'RequiresUnreferencedCodeAttribute' has an imp
warning IL2046: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetDefaultProperty(): Interface member 'System.ComponentModel.ICustomTypeDescriptor.GetDefaultProperty()' with 'RequiresUnreferencedCodeAttribute' has 
warning IL2046: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetEditor(Type): Interface member 'System.ComponentModel.ICustomTypeDescriptor.GetEditor(Type)' with 'RequiresUnreferencedCodeAttribute' has an impleme
warning IL2046: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetEvents(Attribute
warning IL2046: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetProperties(): Interface member 'System.ComponentModel.ICustomTypeDescriptor.GetProperties()' with 'RequiresUnreferencedCodeAttribute' has an impleme
warning IL2046: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetProperties(Attribute
warning IL2055: Newtonsoft.Json.Serialization.DefaultSerializationBinder.GetGenericTypeFromTypeName(String,Assembly): Call to 'System.Type.MakeGenericType(Type
warning IL2055: Newtonsoft.Json.Utilities.FSharpUtils.BuildMapCreator<TKey,TValue>(): Call to 'System.Type.MakeGenericType(Type
warning IL2055: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForArrayContract(Type,Type,Type&,ObjectConstructor`1<Object>&): Call to 'System.Type.MakeGenericType(Type
warning IL2055: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForDictionaryContract(Type,Type,Type,Type&,ObjectConstructor`1<Object>&): Call to 'System.Type.MakeGenericType(Type
warning IL2057: Newtonsoft.Json.Converters.EntityKeyMemberConverter.ReadJson(JsonReader,Type,Object,JsonSerializer): Unrecognized value passed to the parameter 'typeName' of method 'System.Type.GetType(String)'. It's not possible to guarant
warning IL2057: Newtonsoft.Json.Serialization.DefaultSerializationBinder.GetTypeFromTypeNameKey(StructMultiKey`2<String,String>): Unrecognized value passed to the parameter 'typeName' of method 'System.Type.GetType(String)'. It's not possib
warning IL2057: Newtonsoft.Json.Utilities.ConvertUtils.TryConvertInternal(Object,CultureInfo,Type,Object&): Unrecognized value passed to the parameter 'typeName' of method 'System.Type.GetType(String,Boolean)'. It's not possible to guarante
warning IL2060: Newtonsoft.Json.Utilities.FSharpUtils.CreateSeq(Type): Call to 'System.Reflection.MethodInfo.MakeGenericMethod(Type
warning IL2060: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForArrayContract(Type,Type,Type&,ObjectConstructor`1<Object>&): Call to 'System.Reflection.MethodInfo.MakeGenericMethod(Type
warning IL2060: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForDictionaryContract(Type,Type,Type,Type&,ObjectConstructor`1<Object>&): Call to 'System.Reflection.MethodInfo.MakeGenericMethod(Type
warning IL2067: Newtonsoft.Json.Converters.DataSetConverter.ReadJson(JsonReader,Type,Object,JsonSerializer): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicParameterlessConstructor' in call to 'System.Activator.Creat
warning IL2067: Newtonsoft.Json.Converters.DataTableConverter.ReadJson(JsonReader,Type,Object,JsonSerializer): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicParameterlessConstructor' in call to 'System.Activator.Cre
warning IL2067: Newtonsoft.Json.Serialization.JsonTypeReflector.CanTypeDescriptorConvertString(Type,TypeConverter&): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPu
warning IL2067: Newtonsoft.Json.Utilities.ConvertUtils.TryConvertInternal(Object,CultureInfo,Type,Object&): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicConst
warning IL2067: Newtonsoft.Json.Utilities.ExpressionReflectionDelegateFactory.CreateDefaultConstructor<T>(Type): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicParameterlessConstructor' in call to 'System.Linq.Expres
warning IL2067: Newtonsoft.Json.Utilities.ReflectionUtils.GetDefaultValue(Type): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicParameterlessConstructor' in call to 'System.Activator.CreateInstance(Type)'. The parame
warning IL2070: Newtonsoft.Json.Converters.BinaryConverter.EnsureReflectionObject(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructor(Type
warning IL2070: Newtonsoft.Json.Converters.KeyValuePairConverter.InitializeReflectionObject(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructor(Type
warning IL2070: Newtonsoft.Json.Serialization.DefaultContractResolver.<>c.<GetExtensionDataMemberForType>b__44_0(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicFields', 'DynamicallyAccessedMemberTypes.NonPubli
warning IL2070: Newtonsoft.Json.Serialization.DefaultContractResolver.<>c.<GetExtensionDataMemberForType>b__44_0(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties', 'DynamicallyAccessedMemberTypes.NonP
warning IL2070: Newtonsoft.Json.Serialization.DefaultContractResolver.GetAttributeConstructor(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructor(Type
warning IL2070: Newtonsoft.Json.Serialization.DefaultContractResolver.GetAttributeConstructor(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicConstructors
warning IL2070: Newtonsoft.Json.Serialization.DefaultContractResolver.GetImmutableConstructor(Type,JsonPropertyCollection): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetCons
warning IL2070: Newtonsoft.Json.Serialization.DefaultContractResolver.GetParameterizedConstructor(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructors(BindingFla
warning IL2070: Newtonsoft.Json.Serialization.DefaultContractResolver.SetExtensionDataDelegates(JsonObjectContract,MemberInfo): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties' in call to 'System.Type.GetPr
warning IL2070: Newtonsoft.Json.Serialization.JsonTypeReflector.<>c__DisplayClass22_0.<GetCreator>b__0(Object
warning IL2070: Newtonsoft.Json.Serialization.JsonTypeReflector.GetAttribute<T>(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.Interfaces' in call to 'System.Type.GetInterfaces()'. The parameter 'type' of method 'Ne
warning IL2070: Newtonsoft.Json.Utilities.CollectionUtils.ResolveEnumerableCollectionConstructor(Type,Type,Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructors(B
warning IL2070: Newtonsoft.Json.Utilities.DynamicReflectionDelegateFactory.GenerateCreateDefaultConstructorIL(Type,ILGenerator,Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessed
warning IL2070: Newtonsoft.Json.Utilities.FSharpUtils.GetMethodWithNonPublicFallback(Type,String,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods', 'DynamicallyAccessedMemberTypes.NonPublicMethod
warning IL2070: Newtonsoft.Json.Utilities.ReflectionObject.Create(Type,MethodBase,String
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetChildPrivateFields(IList`1<MemberInfo>,Type,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicFields', 'DynamicallyAccessedMemberTypes.NonPubli
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetChildPrivateProperties(IList`1<PropertyInfo>,Type,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties', 'DynamicallyAccessedMemberType
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetDefaultConstructor(Type,Boolean): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicConstructors' in c
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetFields(Type,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicFields', 'DynamicallyAccessedMemberTypes.NonPublicFields' in call to 'System.Type
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetMemberInfoFromType(Type,MemberInfo): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicConstructors', 
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetMemberInfoFromType(Type,MemberInfo): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties', 'DynamicallyAccessedMemberTypes.NonPublicProperties' in ca
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetProperties(Type,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.Interfaces' in call to 'System.Type.GetInterfaces()'. The parameter 'targetType' of
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetProperties(Type,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties', 'DynamicallyAccessedMemberTypes.NonPublicProperties' in call to 
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.ImplementsGenericDefinition(Type,Type,Type&): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.Interfaces' in call to 'System.Type.GetInterfaces()'. The parameter 'ty
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.IsMethodOverridden(Type,Type,String): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods', 'DynamicallyAccessedMemberTypes.NonPublicMethods' in call to 'S
warning IL2070: Newtonsoft.Json.Utilities.TypeExtensions.AssignableToTypeName(Type,String,Boolean,Type&): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.Interfaces' in call to 'System.Type.GetInterfaces()'. The parameter '
warning IL2070: Newtonsoft.Json.Utilities.TypeExtensions.ImplementInterface(Type,Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.Interfaces' in call to 'System.Type.GetInterfaces()'. The parameter 'type' of method 'N
warning IL2072: Newtonsoft.Json.Converters.DataTableConverter.CreateRow(JsonReader,DataTable,JsonSerializer): 'dataType' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicFields', 'DynamicallyAccessedMemberTypes.PublicProperti
warning IL2072: Newtonsoft.Json.Utilities.ConvertUtils.TryConvertInternal(Object,CultureInfo,Type,Object&): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicConst
warning IL2075: Newtonsoft.Json.Serialization.DefaultContractResolver.CreateShouldSerializeTest(MemberInfo): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods' in call to 'System.Type.GetMethod(String,Type
warning IL2075: Newtonsoft.Json.Serialization.DefaultContractResolver.GetCallbackMethodsForType(Type,List`1<SerializationCallback>&,List`1<SerializationCallback>&,List`1<SerializationCallback>&,List`1<SerializationCallback>&,List`1<Serializ
warning IL2075: Newtonsoft.Json.Serialization.DefaultContractResolver.SetExtensionDataDelegates(JsonObjectContract,MemberInfo): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties' in call to 'System.Type.GetPr
warning IL2075: Newtonsoft.Json.Serialization.DefaultContractResolver.SetIsSpecifiedActions(JsonProperty,MemberInfo,Boolean): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicFields', 'DynamicallyAccessedMemberTypes.No
warning IL2075: Newtonsoft.Json.Serialization.DefaultContractResolver.SetIsSpecifiedActions(JsonProperty,MemberInfo,Boolean): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties', 'DynamicallyAccessedMemberType
warning IL2075: Newtonsoft.Json.Serialization.JsonTypeReflector.GetAttribute<T>(MemberInfo): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.Interfaces' in call to 'System.Type.GetInterfaces()'. The return value of method '
warning IL2075: Newtonsoft.Json.Utilities.FSharpUtils.CreateFSharpFuncCall(Type,String): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods' in call to 'System.Type.GetMethod(String,BindingFlags)'. The return val
warning IL2075: Newtonsoft.Json.Utilities.FSharpUtils.FSharpUtils(Assembly): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods' in call to 'System.Type.GetMethod(String)'. The return value of method 'System.Refl
warning IL2075: Newtonsoft.Json.Utilities.FSharpUtils.FSharpUtils(Assembly): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties' in call to 'System.Type.GetProperty(String)'. The return value of method 'System
warning IL2075: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForArrayContract(Type,Type,Type&,ObjectConstructor`1<Object>&): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods' in call to 
warning IL2075: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForDictionaryContract(Type,Type,Type,Type&,ObjectConstructor`1<Object>&): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods' i
warning IL2075: Newtonsoft.Json.Utilities.ReflectionUtils.GetProperties(Type,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties', 'DynamicallyAccessedMemberTypes.NonPublicProperties' in call to 
warning IL2075: Newtonsoft.Json.Utilities.ReflectionUtils.IsOverridenGenericMember(MemberInfo,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicCons
warning IL2077: Newtonsoft.Json.Serialization.JsonObjectContract.GetUninitializedObject(): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicConstructors' in call 
warning IL2080: Newtonsoft.Json.Serialization.DefaultContractResolver.CreateISerializableContract(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicConstruc
warning IL2080: Newtonsoft.Json.Serialization.JsonArrayContract.CreateWrapper(Object): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructor(Type
warning IL2080: Newtonsoft.Json.Serialization.JsonDictionaryContract.CreateWrapper(Object): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructor(Type
warning IL2080: Newtonsoft.Json.Utilities.ConvertUtils.CreateCastConverter(StructMultiKey`2<Type,Type>): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods' in call to 'System.Type.GetMethod(String,Type
warning IL2080: Newtonsoft.Json.Utilities.EnumUtils.InitializeValuesAndNames(StructMultiKey`2<Type,NamingStrategy>): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicFields', 'DynamicallyAccessedMemberTypes.NonPublicFi
warning IL2080: Newtonsoft.Json.Utilities.FSharpUtils.BuildMapCreator<TKey,TValue>(): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructor(Type
warning IL3050: Newtonsoft.Json.Converters.DataTableConverter.GetColumnDataType(JsonReader): Using member 'System.Type.MakeArrayType()' which has 'RequiresDynamicCodeAttribute' can break functionality when AOT compiling. The code for an arr
warning IL3050: Newtonsoft.Json.Serialization.DefaultContractResolver.CreateArrayContract(Type): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.DefaultContractResolver.CreateDictionaryContract(Type): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.DefaultContractResolver.SetExtensionDataDelegates(JsonObjectContract,MemberInfo): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.DefaultSerializationBinder.GetGenericTypeFromTypeName(String,Assembly): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.JsonArrayContract.CreateTemporaryCollection(): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.JsonArrayContract.CreateWrapper(Object): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.JsonArrayContract.JsonArrayContract(Type): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.JsonDictionaryContract.CreateTemporaryDictionary(): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.JsonDictionaryContract.CreateWrapper(Object): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.JsonDictionaryContract.JsonDictionaryContract(Type): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.JsonDynamicContract.CreateCallSiteGetter(String): Using member 'System.Runtime.CompilerServices.CallSite`1<Func`3<CallSite,Object,Object>>.Create(CallSiteBinder)' which has 'RequiresDynamicCodeA
warning IL3050: Newtonsoft.Json.Serialization.JsonDynamicContract.CreateCallSiteSetter(String): Using member 'System.Runtime.CompilerServices.CallSite`1<Func`4<CallSite,Object,Object,Object>>.Create(CallSiteBinder)' which has 'RequiresDynam
warning IL3050: Newtonsoft.Json.Utilities.CollectionUtils.ResolveEnumerableCollectionConstructor(Type,Type): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Utilities.CollectionUtils.ResolveEnumerableCollectionConstructor(Type,Type,Type): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Utilities.CollectionUtils.ToMultidimensionalArray(IList,Type,Int32): Using member 'System.Array.CreateInstance(Type,Int32
warning IL3050: Newtonsoft.Json.Utilities.ConvertUtils.GetTypeCode(Type,Boolean&): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Utilities.DynamicReflectionDelegateFactory.CreateDynamicMethod(String,Type,Type
warning IL3050: Newtonsoft.Json.Utilities.DynamicUtils.BinderWrapper.CreateMemberCalls(): Using member 'Microsoft.CSharp.RuntimeBinder.Binder.GetMember(CSharpBinderFlags,String,Type,IEnumerable`1<CSharpArgumentInfo>)' which has 'RequiresDyn
warning IL3050: Newtonsoft.Json.Utilities.DynamicUtils.BinderWrapper.CreateMemberCalls(): Using member 'Microsoft.CSharp.RuntimeBinder.Binder.SetMember(CSharpBinderFlags,String,Type,IEnumerable`1<CSharpArgumentInfo>)' which has 'RequiresDyn
warning IL3050: Newtonsoft.Json.Utilities.FSharpUtils.BuildMapCreator<TKey,TValue>(): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Utilities.FSharpUtils.CreateMap(Type,Type): Using member 'System.Reflection.MethodInfo.MakeGenericMethod(Type
warning IL3050: Newtonsoft.Json.Utilities.FSharpUtils.CreateSeq(Type): Using member 'System.Reflection.MethodInfo.MakeGenericMethod(Type
warning IL3050: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForArrayContract(Type,Type,Type&,ObjectConstructor`1<Object>&): Using member 'System.Reflection.MethodInfo.MakeGenericMethod(Type
warning IL3050: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForArrayContract(Type,Type,Type&,ObjectConstructor`1<Object>&): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForDictionaryContract(Type,Type,Type,Type&,ObjectConstructor`1<Object>&): Using member 'System.Reflection.MethodInfo.MakeGenericMethod(Type
warning IL3050: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForDictionaryContract(Type,Type,Type,Type&,ObjectConstructor`1<Object>&): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Utilities.NoThrowGetBinderMember.NoThrowGetBinderMember(GetMemberBinder): Using member 'System.Dynamic.GetMemberBinder.GetMemberBinder(String,Boolean)' which has 'RequiresDynamicCodeAttribute' can break funct
warning IL3050: Newtonsoft.Json.Utilities.NoThrowSetBinderMember.NoThrowSetBinderMember(SetMemberBinder): Using member 'System.Dynamic.SetMemberBinder.SetMemberBinder(String,Boolean)' which has 'RequiresDynamicCodeAttribute' can break funct
warning IL3050: NuGet.Protocol.Plugins.Sender.SendAsync(Message,CancellationToken): Using member 'NuGet.Protocol.Plugins.JsonSerializationUtilities.Serialize(JsonWriter,Object)' which has 'RequiresDynamicCodeAttribute' can break functionali
warning IL3050: NuGet.Protocol.Plugins.StandardOutputReceiver.OnLineRead(Object,LineReadEventArgs): Using member 'NuGet.Protocol.Plugins.JsonSerializationUtilities.Deserialize<Message>(String)' which has 'RequiresDynamicCodeAttribute' can b
warning IL3050: NuGet.Protocol.RepositorySignatureResourceProvider.<>c__DisplayClass4_0.<<GetRepositorySignatureResourceAsync>b__2>d.MoveNext(): Using member 'NuGet.Protocol.RepositorySignatureResource.RepositorySignatureResource(JObject,So
warning IL3050: NuGet.Protocol.ServiceIndexResourceV3Provider.<ConsumeServiceIndexStreamNsjAsync>d__15.MoveNext(): Using member 'NuGet.Protocol.ServiceIndexResourceV3.ServiceIndexResourceV3(JObject,DateTime,PackageSource)' which has 'Requir
```

</details>

```
PASS baseline.descriptor-json: 3 artifacts read with System.IO.Compression and System.Text.Json
PASS versioning.parse: normalized 1.2.3-Beta.1, full 1.2.3-Beta.1+build.5, in [1,2): True
PASS configuration.sources: sources spike,private,nuget.org; private credential spike-user (clear text True, length 12)
PASS configuration.source-mapping: enabled True; Acme.Morphir.Python -> spike; Newtonsoft.Json -> nuget.org
PASS packaging.main-package: Acme.Morphir.Python 1.2.3 types DotnetTool,MorphirExtension; artifacts process:acme.morphir.python.osx-arm64.1.2.3.nupkg process:acme.morphir.python.linux-x64.1.2.3.nupkg wasm:acme-python.wasm; tools/any/any/DotnetToolSettings.xml osx-arm64=Acme.Morphir.Python.osx-arm64,linux-x64=Acme.Morphir.Python.linux-x64
PASS packaging.rid-package-subtree: types DotnetToolRidPackage; 6 files under root, outside root: _rels/.rels,Acme.Morphir.Python.osx-arm64.nuspec,morphir/acme-python.wasm,morphir/release.json,[Content_Types].xml,package/services/metadata/core-properties/nuget.psmdcp; tree bef7556afa6f; file f1caa97c2ed0
PASS packaging.signature-read: signed True; primary Author; repository countersignature True; content hash matches signature
PASS packaging.signature-verify: valid True; issues [Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Information,Undefined Information,Undefined Verbose,Undefined Information,Undefined Information,Undefined Verbose,Undefined Minimal,Undefined Information,Undefined Information,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Information,Undefined Information,Undefined Verbose,Undefined Information,Undefined Information,Undefined Verbose]
PASS protocol.local-feed: LocalV2FindPackageByIdResource; versions 1.2.3; copied True, 2727 bytes, id Acme.Morphir.Python
PASS credentials.plugin-service: 0 plugin providers; credentials none; 0 ms
PASS protocol.http-service-index: 40 entries; PackageBaseAddress https://api.nuget.org/v3-flatcontainer/
FAIL protocol.http-flat-container: NuGet.Protocol.Core.Types.FatalProtocolException: Unable to get repository signature information for source https://api.nuget.org/v3-index/repository-signatures/5.0.0/index.json.
     caused by Newtonsoft.Json.JsonSerializationException: Unable to find a constructor to use for type NuGet.Protocol.RepositoryCertificateInfo. A class should either have a default constructor, one constructor with arguments or a constructor marked with the JsonConstructor attribute. Path 'signingCertificates[0].fingerprints'.
FAIL protocol.http-registration-metadata: Newtonsoft.Json.JsonSerializationException: Unable to find a constructor to use for type NuGet.Protocol.Model.RegistrationIndex. A class should either have a default constructor, one constructor with arguments or a constructor marked with the JsonConstructor attribute. Path '@id', line 1, position 7.
exit 2
```

## rooted

- Binary: 22.0 MiB (23075912 bytes)
- Distinct trim/AOT warnings: 124

<details><summary>Warnings by code</summary>

```
  36 IL3050
  27 IL2070
  16 IL2026
  13 IL2075
   7 IL2046
   6 IL2080
   6 IL2067
   4 IL2055
   3 IL2060
   3 IL2057
   2 IL2072
   1 IL2077
```

```
warning IL2026: Newtonsoft.Json.Linq.JContainer.System.ComponentModel.ITypedList.GetItemProperties(PropertyDescriptor
warning IL2026: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetProperties(): Using member 'System.ComponentModel.ICustomTypeDescriptor.GetProperties(Attribute
warning IL2026: Newtonsoft.Json.Serialization.DefaultSerializationBinder.GetGenericTypeFromTypeName(String,Assembly): Using member 'System.Reflection.Assembly.GetType(String)' which has 'RequiresUnreferencedCodeAttribute' can break function
warning IL2026: Newtonsoft.Json.Serialization.DefaultSerializationBinder.GetTypeFromTypeNameKey(StructMultiKey`2<String,String>): Using member 'System.Reflection.Assembly.GetType(String)' which has 'RequiresUnreferencedCodeAttribute' can br
warning IL2026: Newtonsoft.Json.Serialization.JsonTypeReflector.CanTypeDescriptorConvertString(Type,TypeConverter&): Using member 'System.ComponentModel.TypeDescriptor.GetConverter(Type)' which has 'RequiresUnreferencedCodeAttribute' can br
warning IL2026: Newtonsoft.Json.Utilities.ConvertUtils.TryConvertInternal(Object,CultureInfo,Type,Object&): Using member 'System.ComponentModel.TypeDescriptor.GetConverter(Type)' which has 'RequiresUnreferencedCodeAttribute' can break funct
warning IL2026: Newtonsoft.Json.Utilities.DynamicUtils.BinderWrapper.CreateMemberCalls(): Using member 'Microsoft.CSharp.RuntimeBinder.Binder.GetMember(CSharpBinderFlags,String,Type,IEnumerable`1<CSharpArgumentInfo>)' which has 'RequiresUnr
warning IL2026: Newtonsoft.Json.Utilities.DynamicUtils.BinderWrapper.CreateMemberCalls(): Using member 'Microsoft.CSharp.RuntimeBinder.Binder.SetMember(CSharpBinderFlags,String,Type,IEnumerable`1<CSharpArgumentInfo>)' which has 'RequiresUnr
warning IL2026: Newtonsoft.Json.Utilities.FSharpUtils.FSharpUtils(Assembly): Using member 'System.Reflection.Assembly.GetType(String)' which has 'RequiresUnreferencedCodeAttribute' can break functionality when trimming application code. Typ
warning IL2026: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForArrayContract(Type,Type,Type&,ObjectConstructor`1<Object>&): Using member 'System.Reflection.Assembly.GetType(String)' which has 'RequiresUnreferencedCo
warning IL2026: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForDictionaryContract(Type,Type,Type,Type&,ObjectConstructor`1<Object>&): Using member 'System.Reflection.Assembly.GetType(String)' which has 'RequiresUnre
warning IL2026: NuGet.Protocol.Plugins.Sender.SendAsync(Message,CancellationToken): Using member 'NuGet.Protocol.Plugins.JsonSerializationUtilities.Serialize(JsonWriter,Object)' which has 'RequiresUnreferencedCodeAttribute' can break functi
warning IL2026: NuGet.Protocol.Plugins.StandardInputReceiver.Receive(Object): Using member 'NuGet.Protocol.Plugins.JsonSerializationUtilities.Deserialize<Message>(String)' which has 'RequiresUnreferencedCodeAttribute' can break functionalit
warning IL2026: NuGet.Protocol.Plugins.StandardOutputReceiver.OnLineRead(Object,LineReadEventArgs): Using member 'NuGet.Protocol.Plugins.JsonSerializationUtilities.Deserialize<Message>(String)' which has 'RequiresUnreferencedCodeAttribute' 
warning IL2026: NuGet.Protocol.RepositorySignatureResourceProvider.<>c__DisplayClass4_0.<<GetRepositorySignatureResourceAsync>b__2>d.MoveNext(): Using member 'NuGet.Protocol.RepositorySignatureResource.RepositorySignatureResource(JObject,So
warning IL2026: NuGet.Protocol.ServiceIndexResourceV3Provider.<ConsumeServiceIndexStreamNsjAsync>d__15.MoveNext(): Using member 'NuGet.Protocol.ServiceIndexResourceV3.ServiceIndexResourceV3(JObject,DateTime,PackageSource)' which has 'Requir
warning IL2046: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetConverter(): Interface member 'System.ComponentModel.ICustomTypeDescriptor.GetConverter()' with 'RequiresUnreferencedCodeAttribute' has an implement
warning IL2046: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetDefaultEvent(): Interface member 'System.ComponentModel.ICustomTypeDescriptor.GetDefaultEvent()' with 'RequiresUnreferencedCodeAttribute' has an imp
warning IL2046: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetDefaultProperty(): Interface member 'System.ComponentModel.ICustomTypeDescriptor.GetDefaultProperty()' with 'RequiresUnreferencedCodeAttribute' has 
warning IL2046: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetEditor(Type): Interface member 'System.ComponentModel.ICustomTypeDescriptor.GetEditor(Type)' with 'RequiresUnreferencedCodeAttribute' has an impleme
warning IL2046: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetEvents(Attribute
warning IL2046: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetProperties(): Interface member 'System.ComponentModel.ICustomTypeDescriptor.GetProperties()' with 'RequiresUnreferencedCodeAttribute' has an impleme
warning IL2046: Newtonsoft.Json.Linq.JObject.System.ComponentModel.ICustomTypeDescriptor.GetProperties(Attribute
warning IL2055: Newtonsoft.Json.Serialization.DefaultSerializationBinder.GetGenericTypeFromTypeName(String,Assembly): Call to 'System.Type.MakeGenericType(Type
warning IL2055: Newtonsoft.Json.Utilities.FSharpUtils.BuildMapCreator<TKey,TValue>(): Call to 'System.Type.MakeGenericType(Type
warning IL2055: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForArrayContract(Type,Type,Type&,ObjectConstructor`1<Object>&): Call to 'System.Type.MakeGenericType(Type
warning IL2055: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForDictionaryContract(Type,Type,Type,Type&,ObjectConstructor`1<Object>&): Call to 'System.Type.MakeGenericType(Type
warning IL2057: Newtonsoft.Json.Converters.EntityKeyMemberConverter.ReadJson(JsonReader,Type,Object,JsonSerializer): Unrecognized value passed to the parameter 'typeName' of method 'System.Type.GetType(String)'. It's not possible to guarant
warning IL2057: Newtonsoft.Json.Serialization.DefaultSerializationBinder.GetTypeFromTypeNameKey(StructMultiKey`2<String,String>): Unrecognized value passed to the parameter 'typeName' of method 'System.Type.GetType(String)'. It's not possib
warning IL2057: Newtonsoft.Json.Utilities.ConvertUtils.TryConvertInternal(Object,CultureInfo,Type,Object&): Unrecognized value passed to the parameter 'typeName' of method 'System.Type.GetType(String,Boolean)'. It's not possible to guarante
warning IL2060: Newtonsoft.Json.Utilities.FSharpUtils.CreateSeq(Type): Call to 'System.Reflection.MethodInfo.MakeGenericMethod(Type
warning IL2060: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForArrayContract(Type,Type,Type&,ObjectConstructor`1<Object>&): Call to 'System.Reflection.MethodInfo.MakeGenericMethod(Type
warning IL2060: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForDictionaryContract(Type,Type,Type,Type&,ObjectConstructor`1<Object>&): Call to 'System.Reflection.MethodInfo.MakeGenericMethod(Type
warning IL2067: Newtonsoft.Json.Converters.DataSetConverter.ReadJson(JsonReader,Type,Object,JsonSerializer): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicParameterlessConstructor' in call to 'System.Activator.Creat
warning IL2067: Newtonsoft.Json.Converters.DataTableConverter.ReadJson(JsonReader,Type,Object,JsonSerializer): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicParameterlessConstructor' in call to 'System.Activator.Cre
warning IL2067: Newtonsoft.Json.Serialization.JsonTypeReflector.CanTypeDescriptorConvertString(Type,TypeConverter&): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPu
warning IL2067: Newtonsoft.Json.Utilities.ConvertUtils.TryConvertInternal(Object,CultureInfo,Type,Object&): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicConst
warning IL2067: Newtonsoft.Json.Utilities.ExpressionReflectionDelegateFactory.CreateDefaultConstructor<T>(Type): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicParameterlessConstructor' in call to 'System.Linq.Expres
warning IL2067: Newtonsoft.Json.Utilities.ReflectionUtils.GetDefaultValue(Type): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicParameterlessConstructor' in call to 'System.Activator.CreateInstance(Type)'. The parame
warning IL2070: Newtonsoft.Json.Converters.BinaryConverter.EnsureReflectionObject(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructor(Type
warning IL2070: Newtonsoft.Json.Converters.KeyValuePairConverter.InitializeReflectionObject(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructor(Type
warning IL2070: Newtonsoft.Json.Serialization.DefaultContractResolver.<>c.<GetExtensionDataMemberForType>b__44_0(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicFields', 'DynamicallyAccessedMemberTypes.NonPubli
warning IL2070: Newtonsoft.Json.Serialization.DefaultContractResolver.<>c.<GetExtensionDataMemberForType>b__44_0(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties', 'DynamicallyAccessedMemberTypes.NonP
warning IL2070: Newtonsoft.Json.Serialization.DefaultContractResolver.GetAttributeConstructor(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructor(Type
warning IL2070: Newtonsoft.Json.Serialization.DefaultContractResolver.GetAttributeConstructor(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicConstructors
warning IL2070: Newtonsoft.Json.Serialization.DefaultContractResolver.GetImmutableConstructor(Type,JsonPropertyCollection): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetCons
warning IL2070: Newtonsoft.Json.Serialization.DefaultContractResolver.GetParameterizedConstructor(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructors(BindingFla
warning IL2070: Newtonsoft.Json.Serialization.DefaultContractResolver.SetExtensionDataDelegates(JsonObjectContract,MemberInfo): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties' in call to 'System.Type.GetPr
warning IL2070: Newtonsoft.Json.Serialization.JsonTypeReflector.<>c__DisplayClass22_0.<GetCreator>b__0(Object
warning IL2070: Newtonsoft.Json.Serialization.JsonTypeReflector.GetAttribute<T>(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.Interfaces' in call to 'System.Type.GetInterfaces()'. The parameter 'type' of method 'Ne
warning IL2070: Newtonsoft.Json.Utilities.CollectionUtils.ResolveEnumerableCollectionConstructor(Type,Type,Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructors(B
warning IL2070: Newtonsoft.Json.Utilities.DynamicReflectionDelegateFactory.GenerateCreateDefaultConstructorIL(Type,ILGenerator,Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessed
warning IL2070: Newtonsoft.Json.Utilities.FSharpUtils.GetMethodWithNonPublicFallback(Type,String,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods', 'DynamicallyAccessedMemberTypes.NonPublicMethod
warning IL2070: Newtonsoft.Json.Utilities.ReflectionObject.Create(Type,MethodBase,String
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetChildPrivateFields(IList`1<MemberInfo>,Type,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicFields', 'DynamicallyAccessedMemberTypes.NonPubli
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetChildPrivateProperties(IList`1<PropertyInfo>,Type,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties', 'DynamicallyAccessedMemberType
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetDefaultConstructor(Type,Boolean): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicConstructors' in c
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetFields(Type,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicFields', 'DynamicallyAccessedMemberTypes.NonPublicFields' in call to 'System.Type
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetMemberInfoFromType(Type,MemberInfo): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicConstructors', 
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetMemberInfoFromType(Type,MemberInfo): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties', 'DynamicallyAccessedMemberTypes.NonPublicProperties' in ca
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetProperties(Type,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.Interfaces' in call to 'System.Type.GetInterfaces()'. The parameter 'targetType' of
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.GetProperties(Type,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties', 'DynamicallyAccessedMemberTypes.NonPublicProperties' in call to 
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.ImplementsGenericDefinition(Type,Type,Type&): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.Interfaces' in call to 'System.Type.GetInterfaces()'. The parameter 'ty
warning IL2070: Newtonsoft.Json.Utilities.ReflectionUtils.IsMethodOverridden(Type,Type,String): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods', 'DynamicallyAccessedMemberTypes.NonPublicMethods' in call to 'S
warning IL2070: Newtonsoft.Json.Utilities.TypeExtensions.AssignableToTypeName(Type,String,Boolean,Type&): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.Interfaces' in call to 'System.Type.GetInterfaces()'. The parameter '
warning IL2070: Newtonsoft.Json.Utilities.TypeExtensions.ImplementInterface(Type,Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.Interfaces' in call to 'System.Type.GetInterfaces()'. The parameter 'type' of method 'N
warning IL2072: Newtonsoft.Json.Converters.DataTableConverter.CreateRow(JsonReader,DataTable,JsonSerializer): 'dataType' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicFields', 'DynamicallyAccessedMemberTypes.PublicProperti
warning IL2072: Newtonsoft.Json.Utilities.ConvertUtils.TryConvertInternal(Object,CultureInfo,Type,Object&): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicConst
warning IL2075: Newtonsoft.Json.Serialization.DefaultContractResolver.CreateShouldSerializeTest(MemberInfo): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods' in call to 'System.Type.GetMethod(String,Type
warning IL2075: Newtonsoft.Json.Serialization.DefaultContractResolver.GetCallbackMethodsForType(Type,List`1<SerializationCallback>&,List`1<SerializationCallback>&,List`1<SerializationCallback>&,List`1<SerializationCallback>&,List`1<Serializ
warning IL2075: Newtonsoft.Json.Serialization.DefaultContractResolver.SetExtensionDataDelegates(JsonObjectContract,MemberInfo): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties' in call to 'System.Type.GetPr
warning IL2075: Newtonsoft.Json.Serialization.DefaultContractResolver.SetIsSpecifiedActions(JsonProperty,MemberInfo,Boolean): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicFields', 'DynamicallyAccessedMemberTypes.No
warning IL2075: Newtonsoft.Json.Serialization.DefaultContractResolver.SetIsSpecifiedActions(JsonProperty,MemberInfo,Boolean): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties', 'DynamicallyAccessedMemberType
warning IL2075: Newtonsoft.Json.Serialization.JsonTypeReflector.GetAttribute<T>(MemberInfo): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.Interfaces' in call to 'System.Type.GetInterfaces()'. The return value of method '
warning IL2075: Newtonsoft.Json.Utilities.FSharpUtils.CreateFSharpFuncCall(Type,String): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods' in call to 'System.Type.GetMethod(String,BindingFlags)'. The return val
warning IL2075: Newtonsoft.Json.Utilities.FSharpUtils.FSharpUtils(Assembly): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods' in call to 'System.Type.GetMethod(String)'. The return value of method 'System.Refl
warning IL2075: Newtonsoft.Json.Utilities.FSharpUtils.FSharpUtils(Assembly): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties' in call to 'System.Type.GetProperty(String)'. The return value of method 'System
warning IL2075: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForArrayContract(Type,Type,Type&,ObjectConstructor`1<Object>&): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods' in call to 
warning IL2075: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForDictionaryContract(Type,Type,Type,Type&,ObjectConstructor`1<Object>&): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods' i
warning IL2075: Newtonsoft.Json.Utilities.ReflectionUtils.GetProperties(Type,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties', 'DynamicallyAccessedMemberTypes.NonPublicProperties' in call to 
warning IL2075: Newtonsoft.Json.Utilities.ReflectionUtils.IsOverridenGenericMember(MemberInfo,BindingFlags): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicCons
warning IL2077: Newtonsoft.Json.Serialization.JsonObjectContract.GetUninitializedObject(): 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicConstructors' in call 
warning IL2080: Newtonsoft.Json.Serialization.DefaultContractResolver.CreateISerializableContract(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors', 'DynamicallyAccessedMemberTypes.NonPublicConstruc
warning IL2080: Newtonsoft.Json.Serialization.JsonArrayContract.CreateWrapper(Object): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructor(Type
warning IL2080: Newtonsoft.Json.Serialization.JsonDictionaryContract.CreateWrapper(Object): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructor(Type
warning IL2080: Newtonsoft.Json.Utilities.ConvertUtils.CreateCastConverter(StructMultiKey`2<Type,Type>): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods' in call to 'System.Type.GetMethod(String,Type
warning IL2080: Newtonsoft.Json.Utilities.EnumUtils.InitializeValuesAndNames(StructMultiKey`2<Type,NamingStrategy>): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicFields', 'DynamicallyAccessedMemberTypes.NonPublicFi
warning IL2080: Newtonsoft.Json.Utilities.FSharpUtils.BuildMapCreator<TKey,TValue>(): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicConstructors' in call to 'System.Type.GetConstructor(Type
warning IL3050: Newtonsoft.Json.Converters.DataTableConverter.GetColumnDataType(JsonReader): Using member 'System.Type.MakeArrayType()' which has 'RequiresDynamicCodeAttribute' can break functionality when AOT compiling. The code for an arr
warning IL3050: Newtonsoft.Json.Serialization.DefaultContractResolver.CreateArrayContract(Type): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.DefaultContractResolver.CreateDictionaryContract(Type): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.DefaultContractResolver.SetExtensionDataDelegates(JsonObjectContract,MemberInfo): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.DefaultSerializationBinder.GetGenericTypeFromTypeName(String,Assembly): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.JsonArrayContract.CreateTemporaryCollection(): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.JsonArrayContract.CreateWrapper(Object): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.JsonArrayContract.JsonArrayContract(Type): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.JsonDictionaryContract.CreateTemporaryDictionary(): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.JsonDictionaryContract.CreateWrapper(Object): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.JsonDictionaryContract.JsonDictionaryContract(Type): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Serialization.JsonDynamicContract.CreateCallSiteGetter(String): Using member 'System.Runtime.CompilerServices.CallSite`1<Func`3<CallSite,Object,Object>>.Create(CallSiteBinder)' which has 'RequiresDynamicCodeA
warning IL3050: Newtonsoft.Json.Serialization.JsonDynamicContract.CreateCallSiteSetter(String): Using member 'System.Runtime.CompilerServices.CallSite`1<Func`4<CallSite,Object,Object,Object>>.Create(CallSiteBinder)' which has 'RequiresDynam
warning IL3050: Newtonsoft.Json.Utilities.CollectionUtils.ResolveEnumerableCollectionConstructor(Type,Type): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Utilities.CollectionUtils.ResolveEnumerableCollectionConstructor(Type,Type,Type): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Utilities.CollectionUtils.ToMultidimensionalArray(IList,Type,Int32): Using member 'System.Array.CreateInstance(Type,Int32
warning IL3050: Newtonsoft.Json.Utilities.ConvertUtils.GetTypeCode(Type,Boolean&): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Utilities.DynamicProxyMetaObject`1.GetArgArray(DynamicMetaObject
warning IL3050: Newtonsoft.Json.Utilities.DynamicProxyMetaObject`1.GetBinderAdapter.GetBinderAdapter(InvokeMemberBinder): Using member 'System.Dynamic.GetMemberBinder.GetMemberBinder(String,Boolean)' which has 'RequiresDynamicCodeAttribute'
warning IL3050: Newtonsoft.Json.Utilities.DynamicReflectionDelegateFactory.CreateDynamicMethod(String,Type,Type
warning IL3050: Newtonsoft.Json.Utilities.DynamicUtils.BinderWrapper.CreateMemberCalls(): Using member 'Microsoft.CSharp.RuntimeBinder.Binder.GetMember(CSharpBinderFlags,String,Type,IEnumerable`1<CSharpArgumentInfo>)' which has 'RequiresDyn
warning IL3050: Newtonsoft.Json.Utilities.DynamicUtils.BinderWrapper.CreateMemberCalls(): Using member 'Microsoft.CSharp.RuntimeBinder.Binder.SetMember(CSharpBinderFlags,String,Type,IEnumerable`1<CSharpArgumentInfo>)' which has 'RequiresDyn
warning IL3050: Newtonsoft.Json.Utilities.FSharpUtils.BuildMapCreator<TKey,TValue>(): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Utilities.FSharpUtils.CreateMap(Type,Type): Using member 'System.Reflection.MethodInfo.MakeGenericMethod(Type
warning IL3050: Newtonsoft.Json.Utilities.FSharpUtils.CreateSeq(Type): Using member 'System.Reflection.MethodInfo.MakeGenericMethod(Type
warning IL3050: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForArrayContract(Type,Type,Type&,ObjectConstructor`1<Object>&): Using member 'System.Reflection.MethodInfo.MakeGenericMethod(Type
warning IL3050: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForArrayContract(Type,Type,Type&,ObjectConstructor`1<Object>&): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForDictionaryContract(Type,Type,Type,Type&,ObjectConstructor`1<Object>&): Using member 'System.Reflection.MethodInfo.MakeGenericMethod(Type
warning IL3050: Newtonsoft.Json.Utilities.ImmutableCollectionsUtils.TryBuildImmutableForDictionaryContract(Type,Type,Type,Type&,ObjectConstructor`1<Object>&): Using member 'System.Type.MakeGenericType(Type
warning IL3050: Newtonsoft.Json.Utilities.NoThrowGetBinderMember.NoThrowGetBinderMember(GetMemberBinder): Using member 'System.Dynamic.GetMemberBinder.GetMemberBinder(String,Boolean)' which has 'RequiresDynamicCodeAttribute' can break funct
warning IL3050: Newtonsoft.Json.Utilities.NoThrowSetBinderMember.NoThrowSetBinderMember(SetMemberBinder): Using member 'System.Dynamic.SetMemberBinder.SetMemberBinder(String,Boolean)' which has 'RequiresDynamicCodeAttribute' can break funct
warning IL3050: NuGet.Protocol.Plugins.Sender.SendAsync(Message,CancellationToken): Using member 'NuGet.Protocol.Plugins.JsonSerializationUtilities.Serialize(JsonWriter,Object)' which has 'RequiresDynamicCodeAttribute' can break functionali
warning IL3050: NuGet.Protocol.Plugins.StandardInputReceiver.Receive(Object): Using member 'NuGet.Protocol.Plugins.JsonSerializationUtilities.Deserialize<Message>(String)' which has 'RequiresDynamicCodeAttribute' can break functionality whe
warning IL3050: NuGet.Protocol.Plugins.StandardOutputReceiver.OnLineRead(Object,LineReadEventArgs): Using member 'NuGet.Protocol.Plugins.JsonSerializationUtilities.Deserialize<Message>(String)' which has 'RequiresDynamicCodeAttribute' can b
warning IL3050: NuGet.Protocol.RepositorySignatureResourceProvider.<>c__DisplayClass4_0.<<GetRepositorySignatureResourceAsync>b__2>d.MoveNext(): Using member 'NuGet.Protocol.RepositorySignatureResource.RepositorySignatureResource(JObject,So
warning IL3050: NuGet.Protocol.ServiceIndexResourceV3Provider.<ConsumeServiceIndexStreamNsjAsync>d__15.MoveNext(): Using member 'NuGet.Protocol.ServiceIndexResourceV3.ServiceIndexResourceV3(JObject,DateTime,PackageSource)' which has 'Requir
```

</details>

```
PASS baseline.descriptor-json: 3 artifacts read with System.IO.Compression and System.Text.Json
PASS versioning.parse: normalized 1.2.3-Beta.1, full 1.2.3-Beta.1+build.5, in [1,2): True
PASS configuration.sources: sources spike,private,nuget.org; private credential spike-user (clear text True, length 12)
PASS configuration.source-mapping: enabled True; Acme.Morphir.Python -> spike; Newtonsoft.Json -> nuget.org
PASS packaging.main-package: Acme.Morphir.Python 1.2.3 types DotnetTool,MorphirExtension; artifacts process:acme.morphir.python.osx-arm64.1.2.3.nupkg process:acme.morphir.python.linux-x64.1.2.3.nupkg wasm:acme-python.wasm; tools/any/any/DotnetToolSettings.xml osx-arm64=Acme.Morphir.Python.osx-arm64,linux-x64=Acme.Morphir.Python.linux-x64
PASS packaging.rid-package-subtree: types DotnetToolRidPackage; 6 files under root, outside root: _rels/.rels,Acme.Morphir.Python.osx-arm64.nuspec,morphir/acme-python.wasm,morphir/release.json,[Content_Types].xml,package/services/metadata/core-properties/nuget.psmdcp; tree bef7556afa6f; file f1caa97c2ed0
PASS packaging.signature-read: signed True; primary Author; repository countersignature True; content hash matches signature
PASS packaging.signature-verify: valid True; issues [Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Information,Undefined Information,Undefined Verbose,Undefined Information,Undefined Information,Undefined Verbose,Undefined Minimal,Undefined Information,Undefined Information,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Information,Undefined Information,Undefined Verbose,Undefined Information,Undefined Information,Undefined Verbose]
PASS protocol.local-feed: LocalV2FindPackageByIdResource; versions 1.2.3; copied True, 2727 bytes, id Acme.Morphir.Python
PASS credentials.plugin-service: 0 plugin providers; credentials none; 0 ms
PASS protocol.http-service-index: 40 entries; PackageBaseAddress https://api.nuget.org/v3-flatcontainer/
PASS protocol.http-flat-container: HttpFileSystemBasedFindPackageByIdResource; 177 versions; copied True; same bytes as the local cache True
FAIL protocol.http-registration-metadata: Newtonsoft.Json.JsonSerializationException: Unable to find a constructor to use for type NuGet.Packaging.PackageDependencyGroup. A class should either have a default constructor, one constructor with arguments or a constructor marked with the JsonConstructor attribute. Path 'items[4].catalogEntry.dependencyGroups[0].@id', line 1, position 6419.
exit 1
```

## stj

- Binary: 14.2 MiB (14963416 bytes)
- Distinct trim/AOT warnings: 0

```
PASS baseline.descriptor-json: 3 artifacts read with System.IO.Compression and System.Text.Json
PASS versioning.parse: normalized 1.2.3-Beta.1, full 1.2.3-Beta.1+build.5, in [1,2): True
PASS configuration.sources: sources spike,private,nuget.org; private credential spike-user (clear text True, length 12)
PASS configuration.source-mapping: enabled True; Acme.Morphir.Python -> spike; Newtonsoft.Json -> nuget.org
PASS packaging.main-package: Acme.Morphir.Python 1.2.3 types DotnetTool,MorphirExtension; artifacts process:acme.morphir.python.osx-arm64.1.2.3.nupkg process:acme.morphir.python.linux-x64.1.2.3.nupkg wasm:acme-python.wasm; tools/any/any/DotnetToolSettings.xml osx-arm64=Acme.Morphir.Python.osx-arm64,linux-x64=Acme.Morphir.Python.linux-x64
PASS packaging.rid-package-subtree: types DotnetToolRidPackage; 6 files under root, outside root: _rels/.rels,Acme.Morphir.Python.osx-arm64.nuspec,morphir/acme-python.wasm,morphir/release.json,[Content_Types].xml,package/services/metadata/core-properties/nuget.psmdcp; tree bef7556afa6f; file f1caa97c2ed0
PASS packaging.signature-read: signed True; primary Author; repository countersignature True; content hash matches signature
PASS packaging.signature-verify: valid True; issues [Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Information,Undefined Information,Undefined Verbose,Undefined Information,Undefined Information,Undefined Verbose,Undefined Minimal,Undefined Information,Undefined Information,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Minimal,Undefined Information,Undefined Information,Undefined Information,Undefined Verbose,Undefined Information,Undefined Information,Undefined Verbose]
PASS protocol.local-feed: LocalV2FindPackageByIdResource; versions 1.2.3; copied True, 2727 bytes, id Acme.Morphir.Python
PASS credentials.plugin-service: 0 plugin providers; credentials none; 0 ms
PASS protocol.http-service-index: 40 entries; PackageBaseAddress https://api.nuget.org/v3-flatcontainer/
PASS protocol.http-flat-container: HttpFileSystemBasedFindPackageByIdResource; 177 versions; copied True; same bytes as the local cache True
PASS protocol.http-registration-metadata: 76 stable versions; latest 8.0.0; description length 46
exit 0
```

## stj with the Azure Artifacts Credential Provider

As a NUGET_PLUGIN_PATHS .dll (NuGet starts it with dotnet), then as a nuget-plugin-* executable on PATH.

```
PASS credentials.plugin-service: 1 plugin providers; credentials spike-user, password length 11; 230 ms
PASS credentials.plugin-service: 1 plugin providers; credentials spike-user, password length 11; 110 ms
```

