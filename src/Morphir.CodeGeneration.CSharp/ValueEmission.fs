// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.CodeGeneration.CSharp

open System.Threading
open Morphir.IR
open TypeEmission
open ValueLowering

module internal ValueEmission =
    let private signature (ct: CancellationToken) definition =
        ct.ThrowIfCancellationRequested()

        List.foldBack
            (fun (_, input) output ->
                ct.ThrowIfCancellationRequested()
                Arrow(input, output)
            )
            definition.Inputs
            definition.Output

    let emit catalog (ct: CancellationToken) (checkedValues: Checked list) =
        checkedValues
        |> List.map (fun checkedValue ->
            let top = checkedValue.Top
            let loc = top.Location
            let parameters = checkedValue.Parameters
            let body = checkedValue.Body

            let typ shape =
                render catalog ct loc parameters Set.empty (checkedValue.CheckedType shape)

            let mutable nextId = 0

            let fresh () =
                ct.ThrowIfCancellationRequested()
                nextId <- nextId + 1

                "__m"
                + string nextId

            let rec curry inputs output call =
                match inputs with
                | [] -> call []
                | _ ->
                    let names =
                        inputs
                        |> List.map (fun _ -> fresh ())

                    let args = List.zip inputs names

                    List.foldBack
                        (fun (input, name) (text, output) ->
                            "new global::System.Func<"
                            + typ input
                            + ", "
                            + typ output
                            + ">("
                            + name
                            + " => "
                            + text
                            + ")",
                            Arrow(input, output)
                        )
                        args
                        (call names, output)
                    |> fst

            let block output statements result =
                "((global::System.Func<"
                + typ output
                + ">)(() => {\n"
                + statements
                + "return "
                + result
                + ";\n})).Invoke()"

            let constructorParts (candidate: Constructor) args =
                let substitutions =
                    List.zip candidate.Owner.Parameters args
                    |> Map.ofList

                let fields =
                    candidate.Fields
                    |> List.map (fun (name, shape) -> name, substitute ct substitutions shape)

                let output =
                    if candidate.IsRecord then
                        Fields fields
                    else
                        Named(candidate.Owner.Name, args)

                let target =
                    if candidate.IsRecord then
                        typ output
                    elif candidate.Runtime.IsSome then
                        typ output
                        + "."
                        + candidate.Runtime.Value
                    else
                        qualified candidate.Owner.Name
                        + arguments typ args
                        + "."
                        + identifier candidate.Case

                fields, output, target

            let recordExpression shape values =
                ct.ThrowIfCancellationRequested()
                let owner, _ = recordTarget ct catalog loc (checkedValue.CheckedType shape)

                let fields =
                    match owner.Definition with
                    | Alias(Fields fields) -> fields
                    | _ ->
                        Admission.fail
                            "type-mismatch"
                            loc
                            "The selected record target is not a record."

                "new "
                + typ shape
                + "("
                + (fields
                   |> List.map (fun (name, _) -> Map.find name values)
                   |> String.concat ", ")
                + ")"

            let combine conditions =
                match List.choose id conditions with
                | [] -> None
                | conditions ->
                    Some(
                        "("
                        + String.concat " && " conditions
                        + ")"
                    )

            let rec tupleItem target index =
                if index < 7 then
                    target
                    + ".Item"
                    + string (index + 1)
                else
                    tupleItem
                        (target
                         + ".Rest")
                        (index - 7)

            let rec pattern env target (pattern: TypedPattern) =
                ct.ThrowIfCancellationRequested()

                let many parts =
                    parts
                    |> List.fold
                        (fun (conditions, text, env) (p, target) ->
                            let condition, bindings, env = patternCode env target p

                            conditions
                            @ [ condition ],
                            text
                            + bindings,
                            env
                        )
                        ([], "", env)

                match pattern.Pattern with
                | TWild
                | TUnitPattern -> None, "", env
                | TAs(inner, name) ->
                    let condition, bindings, env = patternCode env target inner
                    let variable = fresh ()

                    condition,
                    bindings
                    + typ pattern.Type
                    + " "
                    + variable
                    + " = "
                    + target
                    + "; _ = "
                    + variable
                    + ";\n",
                    Map.add name variable env
                | TLiteralPattern literal ->
                    Some(
                        "global::Morphir.SDK.CSharp.Basics.Equal("
                        + target
                        + ", "
                        + LiteralEmission.text ct literal
                        + ")"
                    ),
                    "",
                    env
                | TTuplePattern items ->
                    let conditions, bindings, env =
                        many (
                            items
                            |> List.mapi (fun index item -> item, tupleItem target index)
                        )

                    combine conditions, bindings, env
                | TEmptyList ->
                    Some(
                        target
                        + ".Length == 0"
                    ),
                    "",
                    env
                | THeadTail(head, tail) ->
                    let tailValue =
                        typ pattern.Type
                        + ".FromEnumerable(global::System.Linq.Enumerable.Skip("
                        + target
                        + ", 1))"

                    let conditions, bindings, env =
                        many[head,
                             target
                             + "[0]"

                             tail, tailValue]

                    combine (
                        Some(
                            target
                            + ".Length > 0"
                        )
                        :: conditions
                    ),
                    bindings,
                    env
                | TConstructorPattern(candidate, _, items) when candidate.Runtime.IsSome ->
                    let methodName = candidate.Runtime.Value
                    let variable = fresh ()

                    let failure =
                        "throw new global::System.InvalidOperationException(\"Morphir constructor pattern failed.\")"

                    let extraction, condition =
                        match methodName with
                        | "LT"
                        | "EQ"
                        | "GT" ->
                            "",
                            "global::Morphir.SDK.CSharp.Basics.Equal("
                            + target
                            + ",global::Morphir.SDK.CSharp.Order."
                            + methodName
                            + ")"
                        | "Just" ->
                            target
                            + ".Match("
                            + variable
                            + " => "
                            + variable
                            + ", () => "
                            + failure
                            + ")",
                            target
                            + ".IsJust"
                        | "Nothing" ->
                            "",
                            "!"
                            + target
                            + ".IsJust"
                        | "Ok" ->
                            target
                            + ".Match("
                            + variable
                            + " => "
                            + failure
                            + ", "
                            + variable
                            + " => "
                            + variable
                            + ")",
                            target
                            + ".IsOk"
                        | "Err" ->
                            target
                            + ".Match("
                            + variable
                            + " => "
                            + variable
                            + ", "
                            + variable
                            + " => "
                            + failure
                            + ")",
                            "!"
                            + target
                            + ".IsOk"
                        | _ ->
                            Admission.fail
                                "unmapped-sdk"
                                loc
                                "The SDK constructor pattern has no binding."

                    let conditions, bindings, env =
                        many (
                            items
                            |> List.map (fun item -> item, extraction)
                        )

                    combine (
                        Some condition
                        :: conditions
                    ),
                    bindings,
                    env
                | TConstructorPattern(candidate, args, items) ->
                    let fields, _, caseType = constructorParts candidate args

                    let targetValue =
                        if candidate.IsRecord then
                            target
                        else
                            "(("
                            + caseType
                            + ")("
                            + target
                            + "))"

                    let conditions, bindings, env =
                        many (
                            List.map2
                                (fun (name, _) item ->
                                    item,
                                    targetValue
                                    + "."
                                    + identifier name
                                )
                                fields
                                items
                        )

                    let tag =
                        if candidate.IsRecord then
                            None
                        else
                            Some(
                                target
                                + " is "
                                + caseType
                            )

                    combine (
                        tag
                        :: conditions
                    ),
                    bindings,
                    env

            and patternCode env target p = pattern env target p

            let rec expression env (value: Typed) =
                ct.ThrowIfCancellationRequested()

                match value.Node with
                | TLiteral literal -> LiteralEmission.text ct literal
                | TVariable name -> Map.find name env
                | TUnit -> "global::Morphir.SDK.CSharp.Unit.Value"
                | TTuple items ->
                    "("
                    + String.concat ", " (List.map (expression env) items)
                    + ")"
                | TList items ->
                    match checkedValue.Resolve value.Type with
                    | Named(_, [ item ]) ->
                        let target =
                            "global::Morphir.SDK.CSharp.List<"
                            + typ item
                            + ">"

                        if List.isEmpty items then
                            target
                            + ".Empty"
                        else
                            target
                            + ".FromEnumerable(new "
                            + typ item
                            + "[] { "
                            + String.concat ", " (List.map (expression env) items)
                            + " })"
                    | _ ->
                        Admission.fail
                            "type-mismatch"
                            loc
                            "A list expression has an inconsistent type."
                | TConstructor(candidate, args) ->
                    let fields, output, target = constructorParts candidate args

                    curry
                        (List.map snd fields)
                        output
                        (fun values ->
                            if candidate.IsRecord then
                                recordExpression
                                    output
                                    (List.zip (List.map fst fields) values
                                     |> Map.ofList)
                            elif candidate.Runtime.IsSome then
                                if List.isEmpty values then
                                    target
                                else
                                    target
                                    + "("
                                    + String.concat ", " values
                                    + ")"
                            else
                                "new "
                                + target
                                + "("
                                + String.concat ", " values
                                + ")"
                        )
                | TRecord fields ->
                    let bindings, values =
                        fields
                        |> List.fold
                            (fun (bindings, values) (name, value) ->
                                let target = fresh ()

                                bindings
                                + typ value.Type
                                + " "
                                + target
                                + " = "
                                + expression env value
                                + ";\n",
                                Map.add name target values
                            )
                            ("", Map.empty)

                    block value.Type bindings (recordExpression value.Type values)
                | TField(target, name) ->
                    "("
                    + expression env target
                    + ")."
                    + identifier name
                | TFieldFunction name ->
                    match checkedValue.Resolve value.Type with
                    | Arrow(input, output) ->
                        let target = fresh () in

                        "new global::System.Func<"
                        + typ input
                        + ", "
                        + typ output
                        + ">("
                        + target
                        + " => "
                        + target
                        + "."
                        + identifier name
                        + ")"
                    | _ ->
                        Admission.fail
                            "type-mismatch"
                            loc
                            "A field function has an inconsistent type."
                | TUpdate(target, fields) ->
                    let targetName = fresh ()

                    let initial =
                        typ target.Type
                        + " "
                        + targetName
                        + " = "
                        + expression env target
                        + "; _ = "
                        + targetName
                        + ";\n"

                    let bindings, updates =
                        fields
                        |> List.fold
                            (fun (bindings, updates) (name, value) ->
                                let variable = fresh ()

                                bindings
                                + typ value.Type
                                + " "
                                + variable
                                + " = "
                                + expression env value
                                + ";\n",
                                Map.add name variable updates
                            )
                            (initial, Map.empty)

                    let shape = checkedValue.Resolve target.Type

                    let recordFields =
                        match shape with
                        | Fields fields -> fields
                        | _ ->
                            Admission.fail
                                "type-mismatch"
                                loc
                                "An update requires a closed record."

                    let values =
                        recordFields
                        |> List.map (fun (name, _) ->
                            name,
                            Map.tryFind name updates
                            |> Option.defaultValue (
                                targetName
                                + "."
                                + identifier name
                            )
                        )
                        |> Map.ofList

                    block value.Type bindings (recordExpression shape values)
                | TIf(condition, yes, no) ->
                    "("
                    + expression env condition
                    + " ? "
                    + expression env yes
                    + " : "
                    + expression env no
                    + ")"
                | TMatch(target, cases) ->
                    let subject = fresh ()

                    let rec branches cases =
                        match cases with
                        | [] ->
                            "throw new global::System.InvalidOperationException(\"Non-exhaustive Morphir pattern.\");\n"
                        | (p, body) :: rest ->
                            let condition, bindings, scope = pattern env subject p

                            let body =
                                bindings
                                + "return "
                                + expression scope body
                                + ";\n"

                            match condition with
                            | None -> body
                            | Some condition ->
                                "if ("
                                + condition
                                + ") {\n"
                                + body
                                + "}\n"
                                + branches rest

                    "((global::System.Func<"
                    + typ value.Type
                    + ">)(() => {\n"
                    + typ target.Type
                    + " "
                    + subject
                    + " = "
                    + expression env target
                    + "; _ = "
                    + subject
                    + ";\n"
                    + branches cases
                    + "})).Invoke()"
                | TDestructure(p, target, body) ->
                    let subject = fresh ()
                    let condition, bindings, scope = pattern env subject p

                    let guard =
                        condition
                        |> Option.map (fun condition ->
                            "if (!("
                            + condition
                            + ")) throw new global::System.InvalidOperationException(\"Morphir destructuring failed.\");\n"
                        )
                        |> Option.defaultValue ""

                    block
                        value.Type
                        (typ target.Type
                         + " "
                         + subject
                         + " = "
                         + expression env target
                         + "; _ = "
                         + subject
                         + ";\n"
                         + guard
                         + bindings)
                        (expression scope body)
                | TPatternLambda(p, input, body) ->
                    let subject = fresh ()
                    let condition, bindings, scope = pattern env subject p

                    let guard =
                        condition
                        |> Option.map (fun condition ->
                            "if (!("
                            + condition
                            + ")) throw new global::System.InvalidOperationException(\"Morphir argument pattern failed.\");\n"
                        )
                        |> Option.defaultValue ""

                    "new global::System.Func<"
                    + typ input
                    + ", "
                    + typ body.Type
                    + ">("
                    + subject
                    + " => {\n"
                    + guard
                    + bindings
                    + "return "
                    + expression scope body
                    + ";\n})"
                | TApply(f, x) ->
                    "("
                    + expression env f
                    + ").Invoke("
                    + expression env x
                    + ")"
                | TLambda(name, input, body) ->
                    let target = fresh ()

                    "new global::System.Func<"
                    + typ input
                    + ", "
                    + typ body.Type
                    + ">("
                    + target
                    + " => "
                    + expression (Map.add name target env) body
                    + ")"
                | TLet(name, binding, rest) ->
                    let target = fresh ()
                    let initial = expression env binding
                    let body = expression (Map.add name target env) rest

                    "((global::System.Func<"
                    + typ rest.Type
                    + ">)(() => { "
                    + typ binding.Type
                    + " "
                    + target
                    + " = "
                    + initial
                    + "; _ = "
                    + target
                    + "; return "
                    + body
                    + "; }))()"
                | TSdk operation ->
                    curry
                        operation.Inputs
                        operation.Output
                        (SdkMapping.emit ct typ checkedValue.CheckedType loc operation)
                | TReference(target, args) ->
                    let targetParameters =
                        signature ct target.Definition
                        |> freeParameters ct
                        |> Set.toList

                    let substitutions =
                        List.zip targetParameters args
                        |> Map.ofList

                    let inputTypes =
                        target.Definition.Inputs
                        |> List.map (
                            snd
                            >> substitute ct substitutions
                        )

                    let output = substitute ct substitutions target.Definition.Output

                    let methodName =
                        qualified target.Name
                        + arguments typ args

                    curry
                        inputTypes
                        output
                        (fun args ->
                            methodName
                            + "("
                            + String.concat ", " args
                            + ")"
                        )

            let inputTargets =
                top.Definition.Inputs
                |> List.map (fun (name, t) -> name, (t, fresh ()))

            let env =
                inputTargets
                |> List.map (fun (name, (_, target)) -> name, target)
                |> Map.ofList

            let inputs =
                inputTargets
                |> List.map (fun (_, (t, target)) ->
                    typ t
                    + " "
                    + target
                )
                |> String.concat ", "

            let access = if top.Public then "public" else "internal"

            let text =
                access
                + " static "
                + typ top.Definition.Output
                + " "
                + identifier top.Name.LocalName
                + TypeEmission.parameters parameters
                + "("
                + inputs
                + ")"
                + constraints parameters
                + " => "
                + expression env body
                + ";\n"

            top.Name.ModulePath, text
        )
        |> List.groupBy fst
        |> List.map (fun (path, items) ->
            path,
            items
            |> List.map snd
            |> String.concat ""
        )
        |> Map.ofList
