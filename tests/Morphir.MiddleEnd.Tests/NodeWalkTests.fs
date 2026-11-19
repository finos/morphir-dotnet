// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.MiddleEnd.Tests.NodeWalkTests

open Expecto
open Morphir.IR
open Morphir.IR.V4
open Morphir.MiddleEnd
open Morphir.MiddleEnd.Tests.ModelSupport

let private variable (text: string) = Value.Variable(va, name text)

let private model =
    let order =
        TypeAliasDefinition(
            [],
            Type.Record(ta, [ name "customer-id", stringType; name "lines", Type.Tuple(ta, [ intType; intType ]) ])
        )

    let calculate = expression [ "f", intType; "x", intType ] intType (Value.Apply(va, variable "f", variable "x"))

    let evaluate =
        expression
            [ "s", intType ]
            intType
            (Value.PatternMatch(
                va,
                variable "s",
                [ ConstructorPattern(va, fq "acme/orders:domain#market", [ AsPattern(va, WildcardPattern va, name "q") ]), variable "q"
                  WildcardPattern va, variable "s" ]
            ))

    library [ "domain", moduleOf [ "order", order ] [ "calculate-total", calculate; "evaluate", evaluate ] ]

let private has (fragment: string) = Expect.contains (fragments model) fragment $"the walk has {fragment}"

[<Tests>]
let tests =
    testList
        "node walk"
        [ test "the roots are the distribution, the package, each module, each type and each value" {
              has "/distribution"
              has "/package"
              has "/module/domain"
              has "/module/domain/type/order"
              has "/module/domain/value/calculate-total"
              has "/module/domain/value/evaluate"
          }

          test "a record type has the address of each of its fields" {
              has "/module/domain/type/order/type-exp"
              has "/module/domain/type/order/type-exp/record/field/customer-id"
              has "/module/domain/type/order/type-exp/record/field/lines"
              has "/module/domain/type/order/type-exp/record/field/lines/tuple/element/0"
              has "/module/domain/type/order/type-exp/record/field/lines/tuple/element/1"
          }

          test "a value has input types, an output type and a body with its roles" {
              has "/module/domain/value/calculate-total/input/f"
              has "/module/domain/value/calculate-total/input/x"
              has "/module/domain/value/calculate-total/output"
              has "/module/domain/value/calculate-total/body"
              has "/module/domain/value/calculate-total/body/apply/function"
              has "/module/domain/value/calculate-total/body/apply/argument"
          }

          test "a pattern match has a subject, and a pattern and a body for each case" {
              has "/module/domain/value/evaluate/body/pattern-match/subject"
              has "/module/domain/value/evaluate/body/pattern-match/case/0/pattern"
              has "/module/domain/value/evaluate/body/pattern-match/case/0/pattern/constructor-pattern/argument/0"
              has "/module/domain/value/evaluate/body/pattern-match/case/0/pattern/constructor-pattern/argument/0/as-pattern/pattern"
              has "/module/domain/value/evaluate/body/pattern-match/case/0/body"
              has "/module/domain/value/evaluate/body/pattern-match/case/1/pattern"
              has "/module/domain/value/evaluate/body/pattern-match/case/1/body"
          }

          test "an inferred type has an address below its node" {
              let typed =
                  Value.Variable({ va with InferredType = Some intType }, name "x")

              let distribution = library [ "domain", moduleOf [] [ "show", expression [ "x", intType ] intType typed ] ]
              Expect.contains (fragments distribution) "/module/domain/value/show/body/inferred-type" "inferred type"
          }

          test "a let and a let recursion of one name give the same address for the definition" {
              let definition = { Doc = None; Value = expression [] intType (Value.Literal(va, Literal.IntegerLiteral(System.Numerics.BigInteger 1))) }

              let single = Value.LetDefinition(va, name "loop", definition, variable "loop")
              let recursion = Value.LetRecursion(va, ordered [ name "loop", definition ], variable "loop")

              let ofBody body = fragments (library [ "domain", moduleOf [] [ "total", expression [] intType body ] ])

              let a = ofBody single |> List.filter (fun f -> f.Contains "let/definition/loop")
              let b = ofBody recursion |> List.filter (fun f -> f.Contains "let/definition/loop")
              Expect.isNonEmpty a "the let has a definition address"
              Expect.equal a b "the same addresses"
              Expect.contains a "/module/domain/value/total/body/let/definition/loop" "the definition"
              Expect.contains a "/module/domain/value/total/body/let/definition/loop/body" "the body of the definition"
          }

          test "every address is pinned to the snapshot, so an ordered step needs no guard" {
              for address, _ in NodeWalk.nodes artifact format snapshot model do
                  Expect.equal address.Revision (ArtifactRevision.Pinned snapshot) "pinned"
                  Expect.isNone address.Guard "no guard"
          }

          test "every address of the walk parses back to itself" {
              for address, _ in NodeWalk.nodes artifact format snapshot model do
                  Expect.equal (NodeAddress.parse (NodeAddress.toUri address) |> Result.toOption) (Some address) (NodeAddress.toUri address)
          }

          test "the walk is deterministic and each address is used once in this model" {
              let first = fragments model
              Expect.equal first (fragments model) "same order"
              Expect.equal (List.distinct first) first "no address twice"
          } ]
