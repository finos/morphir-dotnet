[<AutoOpen>]
module Morphir.IR.Builders

open Morphir.IR.Module
open Morphir.IR.Package
open Morphir.IR.Distribution
open Morphir.IR.Type
open Morphir.SDK.Dict

open type Morphir.IR.Distribution.Distribution

type TypeSpecBuilder () =
    member inline _.Yield(()) = ()

let typeSpec = TypeSpecBuilder()

[<RequireQualifiedAccess>]
type LibraryProperty =
    | PackageName of packageName:PackageName
    | Dependencies of byPackageName:Dict<PackageName, Package.Specification<unit>>
    | Definition of def:Package.Definition<unit, Type<unit>>
    | Dependency of packageName:PackageName * spec:Package.Specification<unit>

type LibraryBuilder() =

    member inline _.Combine(newProp:LibraryProperty, props:LibraryProperty list) =
        newProp :: props

    member inline _.Delay(f:unit -> LibraryProperty list) = f ()
    member inline _.Delay(f: unit -> LibraryProperty) = [f ()]
    member inline _.Yield(()) = ()

    [<CustomOperation("packageName")>]
    member inline _.PackageName((), packageName:PackageName) = [LibraryProperty.PackageName packageName]
    [<CustomOperation("packageName")>]
    member inline _.PackageName((), packageName:string) = [ LibraryProperty.PackageName <| PackageName.fromString packageName]

    member inline x.Run(props: LibraryProperty list) =
        props
        |> List.fold
            (fun dist prop ->
                match prop with
                | LibraryProperty.PackageName packageName -> Distribution.changePackageName packageName dist
                | LibraryProperty.Dependencies dependencies -> Distribution.updateDependencies dependencies dist
                | LibraryProperty.Definition definition -> Distribution.updateDefinition definition dist
                | LibraryProperty.Dependency(packageName, spec) -> Distribution.insertDependency packageName spec dist
            )
            (emptyLibrary Package.PackageName.root)

let library = LibraryBuilder()

type ModuleProperty =
    | ModuleName of moduleName:ModuleName

type Module<'ta,'va> =
    | Module of spec:Module.Specification<'ta> * def:Module.Definition<'ta,'va>
    | ModuleSpec of spec:Module.Specification<'ta>
    | ModuleDef of spec:Module.Definition<'ta,'va>

type ModuleBuilder() =
    member _.Yield(()) = ()

    [<CustomOperation("moduleName")>]
    member _.ModuleName((), moduleName:ModuleName) = [ModuleProperty.ModuleName moduleName]
    [<CustomOperation("moduleName")>]
    member _.ModuleName((), moduleName:string) = [ModuleProperty.ModuleName <| moduleNameFromString moduleName]

    member _.Run(props) = ()


let module' = ModuleBuilder()

module Demo =
    let result = library {
        packageName "MyPackage"
    }
