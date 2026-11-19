// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp

open Morphir.IR
open Morphir.IR.V4

/// <summary>
/// A value class of a type variable (EQL-2): the operations that the values of the type variable take. The frontend
/// finds it and the backend reads it.
/// </summary>
type internal ValueClass =
    /// <summary>Values that compare with <c>==</c>: every type that holds no function.</summary>
    | Equality
    /// <summary>Values with an order: numbers, Char, String, and lists and 2- and 3-tuples of comparable values.</summary>
    | Comparable
    /// <summary>Int or Float.</summary>
    | Number
    /// <summary>String or List.</summary>
    | Appendable
    /// <summary>String, or a list of comparable values.</summary>
    | CompAppend

/// <summary>
/// The words of the value classes, the name rule, and the carrier of a class in the IR: the <c>extensions</c> of the type
/// attributes of a type variable, as <c>{"morphir-dotnet": {"classes": ["comparable"]}}</c>.
/// </summary>
module internal ValueClasses =
    /// <summary>The key of the carrier in the extensions of type attributes.</summary>
    [<Literal>]
    let ExtensionKey = "morphir-dotnet"

    /// <summary>The member of the carrier that holds the class words.</summary>
    [<Literal>]
    let ClassesKey = "classes"

    /// <summary>The word of a class in the carrier.</summary>
    let word (valueClass: ValueClass) =
        match valueClass with
        | Equality -> "equality"
        | Comparable -> "comparable"
        | Number -> "number"
        | Appendable -> "appendable"
        | CompAppend -> "compappend"

    /// <summary>The class of a word of the carrier, or None for a word that is not a class word.</summary>
    let tryParse (text: string) =
        match text with
        | "equality" -> Some Equality
        | "comparable" -> Some Comparable
        | "number" -> Some Number
        | "appendable" -> Some Appendable
        | "compappend" -> Some CompAppend
        | _ -> None

    /// <summary>
    /// The class of a type variable by its name: the first word of the Morphir name, when that word is
    /// <c>number</c>, <c>comparable</c>, <c>appendable</c> or <c>compappend</c>. The word <c>equality</c> is not a name rule.
    /// </summary>
    let ofName (Name words) =
        match words with
        | "number" :: _ -> Some Number
        | "comparable" :: _ -> Some Comparable
        | "appendable" :: _ -> Some Appendable
        | "compappend" :: _ -> Some CompAppend
        | _ -> None

    /// <summary>The classes in the extensions of type attributes, in their order. An extension with no carrier gives none.</summary>
    let ofExtensions (extensions: DocumentValue option) : ValueClass list =
        match extensions with
        | Some(DocumentObject members) ->
            match OrderedMap.tryFind ExtensionKey members with
            | Some(DocumentObject carrier) ->
                match OrderedMap.tryFind ClassesKey carrier with
                | Some(DocumentArray items) ->
                    items
                    |> List.choose (fun item ->
                        match item with
                        | DocumentString text -> tryParse text
                        | _ -> None
                    )
                    |> List.distinct
                | _ -> []
            | _ -> []
        | _ -> []

    /// <summary>The carrier of the classes, as the extensions of type attributes.</summary>
    let carrier (classes: ValueClass list) : DocumentValue =
        let single key value =
            match OrderedMap.ofList [ key, value ] with
            | Ok map -> DocumentObject map
            | Error _ -> DocumentNull

        single ExtensionKey (single ClassesKey (DocumentArray [ for c in classes -> DocumentString(word c) ]))

    /// <summary>
    /// The classes of the carrier on each type variable of a type, merged for each name in the order in which they occur.
    /// </summary>
    let rec private collect (found: Map<Name, ValueClass list>) (typ: Type) =
        let add (found: Map<Name, ValueClass list>) name classes =
            let before = Map.tryFind name found |> Option.defaultValue []
            Map.add name (List.distinct (before @ classes)) found

        match typ with
        | Type.Variable(attributes, name) ->
            match ofExtensions attributes.Extensions with
            | [] -> found
            | classes -> add found name classes
        | Type.Reference(_, _, items)
        | Type.Tuple(_, items) -> List.fold collect found items
        | Type.Record(_, fields)
        | Type.ExtensibleRecord(_, _, fields) -> fields |> List.map snd |> List.fold collect found
        | Type.Function(_, input, output) -> collect (collect found input) output
        | Type.Unit _ -> found

    /// <summary>The classes that the carriers give for the type variables of a value definition: its inputs and its output.</summary>
    let ofDefinition (definition: ValueDefinition) : Map<Name, ValueClass list> =
        let inputs = definition.InputTypes |> List.map snd

        let output =
            match definition.Body with
            | ExpressionBody(output, _) -> [ output ]
            | _ -> []

        List.fold collect Map.empty (inputs @ output)
