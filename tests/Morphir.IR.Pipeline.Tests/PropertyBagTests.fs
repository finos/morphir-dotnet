module Morphir.IR.Pipeline.Tests.PropertyBagTests

open Expecto
open Morphir.IR.Pipeline

let private irVersion = Property.create<int> "test.ir.version"
let private sourcePath = Property.create<string> "test.source.path"

[<Tests>]
let propertyBagTests =
    testList "PropertyBag" [
        test "tryGet should return a stored value with its own type" {
            let version: int option =
                PropertyBag.empty
                |> PropertyBag.set irVersion 3
                |> PropertyBag.tryGet irVersion

            Expect.equal version (Some 3) "should read back the integer"
        }

        test "tryGet should return None when no value is stored" {
            Expect.isNone (PropertyBag.tryGet irVersion PropertyBag.empty) "nothing was stored"
        }

        test "set should replace an earlier value" {
            let bag =
                PropertyBag.empty
                |> PropertyBag.set irVersion 3
                |> PropertyBag.set irVersion 4

            Expect.equal (PropertyBag.tryGet irVersion bag) (Some 4) "last set should win"
        }

        test "a property with the same name and a different type should not see the value" {
            let sameNameAsString = Property.create<string> "test.shared"
            let sameNameAsInt = Property.create<int> "test.shared"

            let bag = PropertyBag.empty |> PropertyBag.set sameNameAsString "text"

            Expect.isNone (PropertyBag.tryGet sameNameAsInt bag) "types differ so the value is not visible"
            Expect.equal (PropertyBag.tryGet sameNameAsString bag) (Some "text") "original is still readable"
        }

        test "same-name different-type properties should hold separate values" {
            let sameNameAsString = Property.create<string> "test.shared"
            let sameNameAsInt = Property.create<int> "test.shared"

            let bag =
                PropertyBag.empty
                |> PropertyBag.set sameNameAsString "text"
                |> PropertyBag.set sameNameAsInt 7

            Expect.equal (PropertyBag.tryGet sameNameAsString bag) (Some "text") "string value"
            Expect.equal (PropertyBag.tryGet sameNameAsInt bag) (Some 7) "integer value"
        }

        test "separately created properties with the same name and type should share a value" {
            let first = Property.create<int> "test.same"
            let second = Property.create<int> "test.same"

            let bag = PropertyBag.empty |> PropertyBag.set first 9

            Expect.equal (PropertyBag.tryGet second bag) (Some 9) "identity is name plus type"
        }

        test "ofSeq should hold values of mixed types" {
            let bag =
                PropertyBag.ofSeq [
                    irVersion.With 3
                    sourcePath.With "orders.json"
                ]

            Expect.equal (PropertyBag.tryGet irVersion bag) (Some 3) "integer value"
            Expect.equal (PropertyBag.tryGet sourcePath bag) (Some "orders.json") "string value"
        }

        test "ofSeq should accept any sequence" {
            let bag = PropertyBag.ofSeq [| Property.valueOf irVersion 3 |]

            Expect.equal (PropertyBag.tryGet irVersion bag) (Some 3) "array input"
        }

        test "ofSeq should keep the last value for a repeated property" {
            let bag = PropertyBag.ofSeq [ irVersion.With 3; irVersion.With 4 ]

            Expect.equal (PropertyBag.tryGet irVersion bag) (Some 4) "last value should win"
        }

        test "add should store a property value" {
            let bag = PropertyBag.empty |> PropertyBag.add (irVersion.With 3)

            Expect.equal (PropertyBag.tryGet irVersion bag) (Some 3) "value should be stored"
        }

        test "update should receive the current value" {
            let bag =
                PropertyBag.empty
                |> PropertyBag.set irVersion 3
                |> PropertyBag.update irVersion (fun current -> Option.defaultValue 0 current + 1)

            Expect.equal (PropertyBag.tryGet irVersion bag) (Some 4) "should increment"
        }

        test "update should receive None when there is no value" {
            let bag =
                PropertyBag.empty
                |> PropertyBag.update irVersion (fun current -> Option.defaultValue 10 current)

            Expect.equal (PropertyBag.tryGet irVersion bag) (Some 10) "should use the default"
        }

        test "remove should delete only the given property" {
            let bag =
                PropertyBag.ofSeq [ irVersion.With 3; sourcePath.With "a" ]
                |> PropertyBag.remove irVersion

            Expect.isNone (PropertyBag.tryGet irVersion bag) "removed"
            Expect.equal (PropertyBag.tryGet sourcePath bag) (Some "a") "kept"
        }

        test "getOr should fall back only when no value is stored" {
            let bag = PropertyBag.empty |> PropertyBag.set irVersion 3

            Expect.equal (PropertyBag.getOr irVersion 9 bag) 3 "stored value"
            Expect.equal (PropertyBag.getOr irVersion 9 PropertyBag.empty) 9 "fallback"
        }

        test "contains should report whether a value is stored" {
            let bag = PropertyBag.empty |> PropertyBag.set irVersion 3

            Expect.isTrue (PropertyBag.contains irVersion bag) "stored"
            Expect.isFalse (PropertyBag.contains sourcePath bag) "not stored"
        }

        test "a stored None should be readable as a value" {
            let optional = Property.create<int option> "test.optional"
            let bag = PropertyBag.empty |> PropertyBag.set optional None

            Expect.equal (PropertyBag.tryGet optional bag) (Some None) "None is a value, not a missing entry"
            Expect.isTrue (PropertyBag.contains optional bag) "entry exists"
        }

        test "functions should not change the bag they are given" {
            let original = PropertyBag.empty |> PropertyBag.set irVersion 3

            original |> PropertyBag.set irVersion 4 |> ignore
            original |> PropertyBag.remove irVersion |> ignore

            Expect.equal (PropertyBag.tryGet irVersion original) (Some 3) "original should be unchanged"
        }

        test "functions should compose with the backward pipe" {
            let bag = PropertyBag.set irVersion 3 <| PropertyBag.empty
            let version = PropertyBag.tryGet irVersion <| bag

            Expect.equal version (Some 3) "backward pipe should work"
        }

        test "functions should compose with function composition" {
            let stamp = PropertyBag.set irVersion 3 >> PropertyBag.set sourcePath "a"
            let bag = stamp PropertyBag.empty

            Expect.equal (PropertyBag.tryGet irVersion bag) (Some 3) "first step"
            Expect.equal (PropertyBag.tryGet sourcePath bag) (Some "a") "second step"
        }

        test "valueOf should accept the value through a backward pipe" {
            let value = Property.valueOf irVersion <| 1 + 2

            Expect.equal (PropertyBag.tryGet irVersion (PropertyBag.ofSeq [ value ])) (Some 3) "computed value"
        }

        test "bags with equal contents should be equal in any order" {
            let first = PropertyBag.ofSeq [ irVersion.With 3; sourcePath.With "a" ]
            let second = PropertyBag.ofSeq [ sourcePath.With "a"; irVersion.With 3 ]

            Expect.equal first second "same content"
            Expect.equal (first.GetHashCode()) (second.GetHashCode()) "equal bags should hash equally"
        }

        test "bags with different values or keys should not be equal" {
            let bag = PropertyBag.ofSeq [ irVersion.With 3 ]

            Expect.notEqual bag (PropertyBag.ofSeq [ irVersion.With 4 ]) "different value"
            Expect.notEqual bag (PropertyBag.ofSeq [ sourcePath.With "3" ]) "different key"
            Expect.notEqual bag PropertyBag.empty "different size"
        }

        test "merge should copy overlay values and let them win" {
            let bag = PropertyBag.ofSeq [ irVersion.With 3; sourcePath.With "a" ]
            let overlay = PropertyBag.ofSeq [ irVersion.With 4 ]

            let merged = PropertyBag.merge overlay bag

            Expect.equal (PropertyBag.tryGet irVersion merged) (Some 4) "overlay wins"
            Expect.equal (PropertyBag.tryGet sourcePath merged) (Some "a") "base value is kept"
        }

        test "property should expose its name" {
            Expect.equal irVersion.Name "test.ir.version" "name should be readable"
        }
    ]
