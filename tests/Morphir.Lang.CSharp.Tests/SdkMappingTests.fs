// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.SdkMappingTests

open System.IO
open System.Text.Json
open Expecto
open Microsoft.CodeAnalysis.Operations
open Morphir.IR
open Morphir.Lang.CSharp

/// The reviewed SDK inventory: the operation ids, such as Morphir.SDK.Basics.add.
let private inventory =
    let path = Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "MorphirSDK", "inventory.json")
    use document = JsonDocument.Parse(File.ReadAllText path)

    document.RootElement.GetProperty("operations").EnumerateArray()
    |> Seq.map (fun operation -> operation.GetProperty("operation").GetString())
    |> Set.ofSeq

let private operationId (name: FQName) =
    let (ModulePath(Path modulePath)) = name.ModulePath
    let moduleName = modulePath |> List.map Name.toTitleCase |> String.concat "."
    "Morphir.SDK." + moduleName + "." + Name.toCamelCase name.LocalName

/// Entries that are mapped but are not in the reviewed contract, each with the reason.
let private outsideContract =
    Map.ofList [
        "Morphir.SDK.String.append", "String concatenation has the same UTF-16 meaning; the reviewed contract has no String operations yet."
        "Morphir.SDK.Basics.not", "Boolean negation; the reviewed contract has no Boolean operations yet."
    ]

let private everyBinary =
    [ for kind in
          [ BinaryOperatorKind.Add; BinaryOperatorKind.Subtract; BinaryOperatorKind.Multiply; BinaryOperatorKind.Divide
            BinaryOperatorKind.Remainder; BinaryOperatorKind.LessThan; BinaryOperatorKind.LessThanOrEqual
            BinaryOperatorKind.GreaterThan; BinaryOperatorKind.GreaterThanOrEqual; BinaryOperatorKind.Equals
            BinaryOperatorKind.NotEquals ] do
          for operand in [ Operand.Int; Operand.Float; Operand.Decimal; Operand.String; Operand.Char; Operand.Bool; Operand.Other ] do
              match SdkMapping.tryBinary kind operand with
              | Some name -> yield $"{kind} on {operand}", name
              | None -> () ]

let private everyUnary =
    [ for kind in [ UnaryOperatorKind.Minus; UnaryOperatorKind.Not ] do
          for operand in [ Operand.Int; Operand.Float; Operand.Bool ] do
              match SdkMapping.tryUnary kind operand with
              | Some name -> yield $"{kind} on {operand}", name
              | None -> () ]

[<Tests>]
let tests =
    testList
        "C# SDK mapping"
        [ test "every operator entry is in the reviewed inventory, or names why it is outside" {
              for label, name in everyBinary @ everyUnary do
                  let id = operationId name

                  Expect.isTrue
                      (inventory.Contains id || outsideContract.ContainsKey id)
                      $"{label} maps to {id}, which is neither in the inventory nor in the outside list"
          }

          test "every entry outside the contract has a reason and is used" {
              let used = (everyBinary @ everyUnary) |> List.map (snd >> operationId) |> Set.ofList

              for KeyValue(id, reason) in outsideContract do
                  Expect.isNotEmpty reason id
                  Expect.isTrue (used.Contains id) $"{id} is listed as outside but no operator maps to it"
          }

          test "every public static method of Morphir.SDK.CSharp.Basics maps to an inventory operation" {
              let methods =
                  typeof<Morphir.SDK.CSharp.Basics>.GetMethods(System.Reflection.BindingFlags.Public ||| System.Reflection.BindingFlags.Static)
                  |> Array.map (fun m -> m.Name)
                  |> Array.filter (fun name -> not (name.StartsWith "get_"))
                  |> Array.distinct

              Expect.isNonEmpty methods "the SDK has methods"

              for name in methods do
                  let id = operationId (SdkMapping.sdk "Basics" name)
                  Expect.isTrue (inventory.Contains id) $"Basics.{name} maps to {id}, which is not in the inventory"
          }

          test "decimal division and arithmetic use the Decimal module" {
              Expect.equal (SdkMapping.tryBinary BinaryOperatorKind.Divide Operand.Decimal |> Option.map operationId) (Some "Morphir.SDK.Decimal.div") "div"
              Expect.equal (SdkMapping.tryBinary BinaryOperatorKind.Add Operand.Decimal |> Option.map operationId) (Some "Morphir.SDK.Decimal.add") "add"
          }

          test "decimal comparison has no mapping" {
              Expect.isNone (SdkMapping.tryBinary BinaryOperatorKind.LessThan Operand.Decimal) "not in the contract"
          } ]
