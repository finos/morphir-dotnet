// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.MiddleEnd.Tests.DocumentedRule

// <doc-example>
open Morphir.IR
open Morphir.IR.V4
open Morphir.MiddleEnd

let private empty = ValueAttributes.empty
let private x = Name.fromString "x"
let private price = Value.Variable(empty, Name.fromString "price")

// The function x -> x.
let private identity =
    Value.Lambda(empty, AsPattern(empty, WildcardPattern empty, x), Value.Variable(empty, x))

/// The rule "an identity lambda becomes its argument".
let identityLambda: RewriteRule =
    { Id = RuleId.Create "https://morphir.finos.org/rules/identity-lambda"
      Title = "An identity lambda becomes its argument"
      // The example: the value before and the value after.
      Example = Some { Before = Value.Apply(empty, identity, price); After = price }
      Removes = Set.ofList [ IrFeature.Value ValueKind.Lambda; IrFeature.Value ValueKind.Apply ]
      Adds = Set.empty
      Apply =
        fun _ value ->
            match value with
            | Value.Apply(_, Value.Lambda(_, AsPattern(_, WildcardPattern _, bound), Value.Variable(_, used)), argument) when
                bound = used
                ->
                Some argument
            | _ -> None }

/// The pass that runs the rule on each module, right after a frontend.
let pass: IModelTransform =
    RewritePass.create
        "acme.identity-lambda"
        "Identity lambda"
        "Replaces an identity lambda that is applied to an argument by the argument."
        PassStage.AfterFrontend
        Strategy.BottomUp
        [ identityLambda ]
// </doc-example>
