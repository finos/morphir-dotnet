// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BoundedGeneration

open System
open System.Globalization
open System.IO
open System.Text.Json
open Morphir.IR
open Morphir.IR.Trees

/// Native reader facts only. No expected data, resource verdict, or runner policy.
/// Kept internal until an explicit --adapter host owns admission and serialization.
[<RequireQualifiedAccess>]
module internal ReaderObservation =
    let private refuse message =
        Error [ { Code = "observation.unsupported_selection"; File = None; Message = message } ]

    let private traverse action items =
        let rec loop reversed = function
            | [] -> Ok(List.rev reversed)
            | item :: tail -> action item |> Result.bind (fun value -> loop (value :: reversed) tail)
        loop [] items

    // Feed each opaque production continuation back unchanged, on the same snapshot.
    let private pages selection size snapshot =
        let rec next cursor reversed =
            DocumentTreeStore.pageInventory selection size cursor snapshot
            |> Result.bind (fun page ->
                let collected = page.Entries :: reversed
                match page.Continuation with
                | None -> Ok(List.rev collected)
                | Some token -> next (Some token) collected)
        next None []

    let private role = function
        | BoundedTreeRole.Definition -> "definition"
        | BoundedTreeRole.Specification -> "specification"

    let private addressParts own = function
        | Own address -> own, address
        | Dependency(package, address) -> package, address
        | Manifest -> invalidArg "address" "A manifest is not an inventory member."

    let private packageText (PackageName path) = CanonicalNames.pathToString path
    let private moduleText (ModulePath path) = CanonicalNames.pathToString path

    let private targetAddress (snapshot: BoundedTreeSnapshot) (target: FQName) =
        let memberAddress = ValueDef(target.ModulePath, target.LocalName)
        if target.PackagePath = snapshot.Package then Own memberAddress
        else Dependency(target.PackagePath, memberAddress)

    let private reference = function
        | BoundedTreeBody.ClassicValue(ClassicValueDocument.DefinedValue value) ->
            let definition = Classic.Documented.value value.Value
            match definition.OutputType, definition.Body with
            | Classic.Type.Reference(_, output, []), Classic.Reference(_, target) -> Ok(output, target)
            | _ -> refuse "Select a value with a reference output type and a value-reference body."
        | BoundedTreeBody.V4Value(DefinedValue value) ->
            match value.Value.Value.Body with
            | V4.ExpressionBody(V4.Type.Reference(_, output, []), V4.Value.Reference(_, target)) -> Ok(output, target)
            | _ -> refuse "Select a value with a reference output type and a value-reference body."
        | _ -> refuse "The final selected member must be a value definition."

    let private integer = function
        | BoundedTreeBody.ClassicValue(ClassicValueDocument.DefinedValue value) ->
            match (Classic.Documented.value value.Value).Body with
            | Classic.Literal(_, Classic.WholeNumberLiteral literal) -> Ok(bigint literal)
            | _ -> refuse "The referenced value is not an integer literal."
        | BoundedTreeBody.V4Value(DefinedValue value) ->
            match value.Value.Value.Body with
            | V4.ExpressionBody(_, V4.Value.Literal(_, V4.IntegerLiteral literal)) -> Ok literal
            | _ -> refuse "The referenced value is not an integer literal."
        | _ -> refuse "The reference does not identify a value definition."

    let private document limit snapshot id =
        DocumentTreeStore.readExpandedDocument id limit snapshot
        |> Result.map (fun file ->
            use parsed = JsonDocument.Parse file.Bytes
            parsed.RootElement.Clone())

    /// Observe inventory, selected-module pages, the last value and its typed value target.
    /// The caller owns source admission, encoding, limits, selection and snapshot lifetime.
    /// All I/O goes through production paging and selected-document/member APIs.
    let observe selection pageSize documentLimit (snapshot: BoundedTreeSnapshot) =
        pages "root" pageSize snapshot
        |> Result.bind (fun rootPages ->
            let modules =
                rootPages |> List.collect id |> List.choose (function
                    | BoundedTreeInventoryEntry.ModuleEntry item -> Some item
                    | BoundedTreeInventoryEntry.MemberEntry _ -> None)
            traverse (fun (item: BoundedTreeModule) ->
                pages item.DocumentId pageSize snapshot
                |> Result.map (fun found -> item, List.collect id found)) modules)
        |> Result.bind (fun inventory ->
            pages selection pageSize snapshot
            |> Result.bind (fun selectedPages ->
                match selectedPages |> List.tryLast |> Option.bind List.tryLast with
                | Some(BoundedTreeInventoryEntry.MemberEntry(final, _)) ->
                    DocumentTreeStore.readMember final.Address snapshot
                    |> Result.bind reference
                    |> Result.bind (fun (output, target) ->
                        DocumentTreeStore.readMember (targetAddress snapshot target) snapshot
                        |> Result.bind integer
                        |> Result.map (fun literal -> inventory, selectedPages, final, output, target, literal))
                | _ -> refuse "Select a nonempty module inventory."))
        |> Result.bind (fun (inventory, selectedPages, final, output, target, literal) ->
            document documentLimit snapshot "manifest"
            |> Result.bind (fun manifest ->
                let classicIds =
                    if snapshot.IrVersion <> "3.1.0" then []
                    else
                        inventory |> List.collect (fun (_, entries) -> entries) |> List.choose (function
                            | BoundedTreeInventoryEntry.MemberEntry(item, _) -> Some item.DocumentId
                            | BoundedTreeInventoryEntry.ModuleEntry _ -> None)
                traverse (fun id -> document documentLimit snapshot id |> Result.map (fun value -> id, value)) classicIds
                |> Result.map (fun classicDocuments ->
                    use stream = new MemoryStream()
                    use writer = new Utf8JsonWriter(stream)
                    let text (key: string) (value: string) = writer.WriteString(key, value)
                    let strings name values =
                        writer.WriteStartArray(name: string)
                        for value in values do writer.WriteStringValue(value: string)
                        writer.WriteEndArray()
                    writer.WriteStartObject()
                    text "scope" "native-reader"
                    text "revision" snapshot.RevisionId
                    text "irVersion" snapshot.IrVersion
                    for field in [ "distribution"; "pathBudget" ] do
                        writer.WritePropertyName field
                        manifest.GetProperty(field).WriteTo writer
                    text "package" (packageText snapshot.Package)
                    strings "dependencies" (snapshot.Dependencies |> List.map packageText)
                    writer.WriteStartArray "modules"
                    for item, entries in inventory do
                        let package, address = addressParts snapshot.Package item.Address
                        writer.WriteStartObject()
                        text "package" (packageText package)
                        match address with
                        | Module path -> text "path" (moduleText path)
                        | TypeDef _ | ValueDef _ -> invalidOp "The reader returned a member in module inventory."
                        text "role" (role item.Role)
                        match item.Access with
                        | Some V4.Public -> text "access" "Public"
                        | Some V4.Private -> text "access" "Private"
                        | None -> ()
                        text "document" item.DocumentId
                        writer.WriteStartArray "members"
                        for entry in entries do
                            match entry with
                            | BoundedTreeInventoryEntry.ModuleEntry _ -> invalidOp "The reader returned a module in member inventory."
                            | BoundedTreeInventoryEntry.MemberEntry(memberItem, memberRole) ->
                                let _, memberAddress = addressParts snapshot.Package memberItem.Address
                                writer.WriteStartObject()
                                match memberAddress with
                                | TypeDef(_, name) -> text "name" (CanonicalNames.nameToString name); text "kind" "type"
                                | ValueDef(_, name) -> text "name" (CanonicalNames.nameToString name); text "kind" "value"
                                | Module _ -> invalidOp "Expected a type or value."
                                text "role" (role memberRole)
                                text "document" memberItem.DocumentId
                                writer.WriteEndObject()
                        writer.WriteEndArray()
                        writer.WriteEndObject()
                    writer.WriteEndArray()
                    writer.WriteStartArray "pages"
                    for page in selectedPages do
                        writer.WriteStartArray()
                        for entry in page do
                            match entry with
                            | BoundedTreeInventoryEntry.MemberEntry(item, _) -> writer.WriteStringValue(item.DocumentId)
                            | BoundedTreeInventoryEntry.ModuleEntry item -> writer.WriteStringValue(item.DocumentId)
                        writer.WriteEndArray()
                    writer.WriteEndArray()
                    writer.WriteStartObject "finalPageBody"
                    text "document" final.DocumentId
                    text "outputType" (CanonicalNames.fqNameToString output)
                    text "reference" (CanonicalNames.fqNameToString target)
                    text "referenceKind" "value"
                    writer.WritePropertyName "referencedIntegerLiteral"
                    writer.WriteRawValue(literal.ToString(CultureInfo.InvariantCulture), false)
                    writer.WriteEndObject()
                    writer.WriteStartObject "classicDocuments"
                    for id, value in classicDocuments do
                        writer.WritePropertyName id
                        value.WriteTo writer
                    writer.WriteEndObject()
                    writer.WriteEndObject()
                    writer.Flush()
                    use parsed = JsonDocument.Parse(ReadOnlyMemory(stream.ToArray()))
                    parsed.RootElement.Clone())))
