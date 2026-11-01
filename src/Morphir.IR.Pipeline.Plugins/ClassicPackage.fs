namespace Morphir.IR.Pipeline.Plugins

open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.Documented

/// <summary>Helpers shared by plugins that work on classic IR packages.</summary>
module internal ClassicPackage =
    let private words (Name value) = value
    let private namesOf (Path names) = names

    let private joined (name: Name) = String.concat "" (words name)

    let private capitalize (value: string) =
        value.Substring(0, 1).ToUpperInvariant() + value.Substring(1)

    /// <summary>True for a name in the Morphir SDK package.</summary>
    let isSdk (name: FQName) : bool =
        let (PackageName path) = name.PackagePath
        namesOf path |> List.map joined = [ "morphir"; "sdk" ]

    /// <summary>A case-insensitive SDK name such as <c>basics.lessthan</c>.</summary>
    let sdkName (name: FQName) : string =
        let (ModulePath path) = name.ModulePath
        (namesOf path |> List.map joined |> String.concat ".") + "." + joined name.LocalName

    /// <summary>A readable location such as <c>Domain.Rules.dailyRate</c>.</summary>
    let private location (modulePath: ModulePath) (value: Name) : string =
        let (ModulePath path) = modulePath

        let moduleName =
            namesOf path
            |> List.map (fun name -> words name |> List.map capitalize |> String.concat "")
            |> String.concat "."

        let valueName =
            match words value with
            | head :: tail -> head + (tail |> List.map capitalize |> String.concat "")
            | [] -> ""

        moduleName + "." + valueName

    /// <summary>
    /// Applies a function to every value definition in a package, threading a state through them
    /// in a stable order and keeping access control and documentation.
    /// </summary>
    let mapDefinitions
        (f: string -> ValueDefinition<'ta, 'va> -> 'state -> ValueDefinition<'ta, 'va> * 'state)
        (package: Package.PackageDefinition<'ta, 'va>)
        (state: 'state)
        : Package.PackageDefinition<'ta, 'va> * 'state =
        let mapDocumented where documented current =
            match documented with
            | WithDocumentation(doc, definition) ->
                let updated, next = f where definition current
                WithDocumentation(doc, updated), next
            | WithoutDocumentation definition ->
                let updated, next = f where definition current
                WithoutDocumentation updated, next

        let modules, finalState =
            package.Modules
            |> Map.toList
            |> List.mapFold
                (fun current (modulePath, controlled: AccessControlled<Module.ModuleDefinition<'ta, 'va>>) ->
                    let values, afterModule =
                        controlled.Value.Values
                        |> Map.toList
                        |> List.mapFold
                            (fun inner (name, access: AccessControlled<Documented<ValueDefinition<'ta, 'va>>>) ->
                                let documented, next = mapDocumented (location modulePath name) access.Value inner
                                (name, { Access = access.Access; Value = documented }), next)
                            current

                    (modulePath,
                     { Access = controlled.Access
                       Value = { controlled.Value with Values = Map.ofList values } }),
                    afterModule)
                state

        ({ Modules = Map.ofList modules }: Package.PackageDefinition<'ta, 'va>), finalState
