module Morphir.Bogus.ProgrammingLibDataLoader

open FSharp.Data

type LibraryData =
    JsonProvider<"../../Data/ProgrammingLibData.json", EmbeddedResource="Morphir.Bogus, Morphir.Bogus.ProgrammingLibData.json", RootName="Record">

type TypeIdentity = {
    LanguageEcosystem: string
    Namespace: string
    Name: string
}

type TypeData = {
    Identity: TypeIdentity
    Members: string list
}

let private assembly = typeof<TypeIdentity>.Assembly

let private data =
    lazy
        (LibraryData.GetSamples()
         |> Seq.toArray)

let ProgrammingLibData = data.Value

let (|ModuleName|_|) (record: LibraryData.Record) =
    match record.Module with
    | Some module_ -> Some module_
    | _ -> None

let (|TypeName|_|) (record: LibraryData.Record) =
    match record.Type with
    | Some type_ -> Some type_
    | _ -> None

let (|TypeOrModuleName|_|) (record: LibraryData.Record) =
    match record.Type, record.Module with
    | Some type_, _ -> Some type_
    | _, Some module_ -> Some module_
    | _ -> None

let hasTypeOrModuleName =
    function
    | TypeOrModuleName _ -> true
    | _ -> false

let getTypeOrModuleName =
    function
    | TypeOrModuleName name -> Some name
    | _ -> None

type LibraryData.Record with

    member self.HasTypeOrModuleName: bool = hasTypeOrModuleName self
    member self.GetTypeOrModuleName: string option = getTypeOrModuleName self

    member self.TypeIdentity: TypeIdentity option =
        match self with
        | TypeOrModuleName name ->
            Some
                {
                    LanguageEcosystem = self.LanguageEcosystem
                    Namespace = self.Namespace
                    Name = name
                }
        | _ -> None

let TypeIdentities: seq<TypeIdentity> = query {
    for record in ProgrammingLibData do
        where record.HasTypeOrModuleName

        select
            {
                LanguageEcosystem = record.LanguageEcosystem
                Namespace = record.Namespace
                Name = record.GetTypeOrModuleName.Value
            }
}

let TypeData = query {
    for record in ProgrammingLibData do
        where record.HasTypeOrModuleName
        groupBy record.TypeIdentity.Value into g

        select
            {
                Identity = g.Key
                Members =
                    g
                    |> Seq.map (fun x -> x.Member)
                    |> Seq.toList
            }
}

type TypeIdentity with

    member self.FullName =
        self.Namespace
        + "."
        + self.Name

type TypeData with

    member self.FullName = self.Identity.FullName
