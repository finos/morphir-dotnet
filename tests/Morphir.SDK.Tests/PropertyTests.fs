module Morphir.SDK.Tests.PropertyTests

open Expecto
open Expecto.Flip
open FsCheck
open Morphir.SDK

/// Property-based tests using FsCheck to verify mathematical laws and invariants

[<Tests>]
let tests =
    testList "Property-Based Tests" [
        testList "Maybe (Option) Laws" [
            testProperty "map id = id (functor identity)" <| fun (x: int option) ->
                Maybe.map id x = x

            testProperty "map (f >> g) = map f >> map g (functor composition)" <| fun (x: int option) ->
                let f = (*) 2
                let g = (+) 1
                Maybe.map (f >> g) x = (Maybe.map f >> Maybe.map g) x

            testProperty "andThen with just is identity" <| fun (x: int) ->
                Maybe.andThen Some (Some x) = Some x

            testProperty "withDefault returns value for Some" <| fun (x: int) (d: int) ->
                Maybe.withDefault d (Some x) = x

            testProperty "map2 is associative with addition" <| fun (a: int option) (b: int option) ->
                Maybe.map2 (+) a b = Maybe.map2 (+) b a
        ]

        testList "Result Laws" [
            testProperty "map id = id" <| fun (x: Result<int, string>) ->
                Result.map id x = x

            testProperty "map (f >> g) = map f >> map g" <| fun (x: Result<int, string>) ->
                let f = (*) 2
                let g = (+) 1
                Result.map (f >> g) x = (Result.map f >> Result.map g) x

            testProperty "andThen with ok is identity" <| fun (x: int) ->
                Result.andThen Ok (Ok x) = Ok x

            testProperty "withDefault returns value for Ok" <| fun (x: int) (d: int) ->
                Result.withDefault d (Ok x) = x

            testProperty "mapError preserves Ok values" <| fun (x: int) (f: string -> string) ->
                match Result.mapError f (Ok x) with
                | Ok y -> y = x
                | Error _ -> false
        ]

        testList "List Laws" [
            testProperty "map id = id" <| fun (xs: int list) ->
                List.map id xs = xs

            testProperty "map (f >> g) = map f >> map g" <| fun (xs: int list) ->
                let f = (*) 2
                let g = (+) 1
                List.map (f >> g) xs = (List.map f >> List.map g) xs

            testProperty "filter and map commute" <| fun (xs: int list) ->
                let p x = x > 0
                let f = (*) 2
                List.filter p (List.map f xs) = List.map f (List.filter (fun x -> p (f x)) xs)

            testProperty "append is associative" <| fun (xs: int list) (ys: int list) (zs: int list) ->
                List.append (List.append xs ys) zs = List.append xs (List.append ys zs)

            testProperty "length of append is sum of lengths" <| fun (xs: int list) (ys: int list) ->
                List.length (List.append xs ys) = List.length xs + List.length ys

            testProperty "reverse twice is identity" <| fun (xs: int list) ->
                List.reverse (List.reverse xs) = xs

            testProperty "foldl and foldr with associative op" <| fun (xs: int list) ->
                List.foldl (+) 0 xs = List.foldr (+) 0 xs

            testProperty "contains after insert" <| fun (x: int) (xs: int list) ->
                List.contains x (x :: xs)

            testProperty "unique removes duplicates" <| fun (xs: int list) ->
                let unique = List.unique xs
                unique = List.unique unique  // Applying unique twice doesn't change result

            testProperty "sum equals foldl with plus" <| fun (xs: int list) ->
                List.sum xs = List.foldl (+) 0 xs
        ]

        testList "String Laws" [
            testProperty "length of empty string is 0" <| fun () ->
                String.length "" = 0

            testProperty "length is non-negative" <| fun (s: string) ->
                s <> null ==> lazy (String.length s >= 0)

            testProperty "reverse twice is identity" <| fun (s: string) ->
                s <> null ==> lazy (String.reverse (String.reverse s) = s)

            testProperty "append length is sum of lengths" <| fun (s1: string) (s2: string) ->
                (s1 <> null && s2 <> null) ==> lazy (
                    String.length (String.append s1 s2) = String.length s1 + String.length s2
                )

            testProperty "toUpper then toLower is not identity for uppercase chars" <| fun (s: string) ->
                s <> null ==> lazy (
                    let upper = String.toUpper s
                    let lower = String.toLower upper
                    // At least it should equal the lowercase version
                    lower = String.toLower s
                )

            testProperty "split then join recovers original with simple separator" <| fun (s: string) ->
                (s <> null && not (String.contains "," s)) ==> lazy (
                    String.join "," (String.split "," s) = s
                )
        ]

        testList "Dict Laws" [
            testProperty "get after insert returns value" <| fun (k: string) (v: int) ->
                k <> null ==> lazy (
                    let dict = Dict.empty |> Dict.insert k v
                    Dict.get k dict = Some v
                )

            testProperty "size after insert increases by 1 or stays same" <| fun (k: string) (v: int) ->
                k <> null ==> lazy (
                    let dict = Dict.empty |> Dict.insert k v
                    let size = Dict.size dict
                    size >= 1
                )

            testProperty "contains after insert" <| fun (k: string) (v: int) ->
                k <> null ==> lazy (
                    let dict = Dict.empty |> Dict.insert k v
                    Dict.contains k dict
                )

            testProperty "not contains after remove" <| fun (k: string) (v: int) ->
                k <> null ==> lazy (
                    let dict = Dict.empty |> Dict.insert k v |> Dict.remove k
                    not (Dict.contains k dict)
                )

            testProperty "fromList then toList preserves entries" <| fun (pairs: (string * int) list) ->
                let validPairs = pairs |> List.filter (fun (k, _) -> k <> null && k <> "") |> List.distinctBy fst
                let dict = Dict.fromList validPairs
                let recovered = Dict.toList dict |> List.sortBy fst
                let expected = validPairs |> List.sortBy fst
                recovered = expected
        ]

        testList "Set Laws" [
            testProperty "contains after insert" <| fun (x: int) ->
                let set = Set.empty |> Set.insert x
                Set.contains x set

            testProperty "not contains after remove" <| fun (x: int) ->
                let set = Set.empty |> Set.insert x |> Set.remove x
                not (Set.contains x set)

            testProperty "fromList removes duplicates" <| fun (xs: int list) ->
                let set = Set.fromList xs
                Set.size set <= List.length xs

            testProperty "union is commutative" <| fun (xs: int list) (ys: int list) ->
                let set1 = Set.fromList xs
                let set2 = Set.fromList ys
                Set.union set1 set2 = Set.union set2 set1

            testProperty "intersect is commutative" <| fun (xs: int list) (ys: int list) ->
                let set1 = Set.fromList xs
                let set2 = Set.fromList ys
                Set.intersect set1 set2 = Set.intersect set2 set1

            testProperty "diff of set with itself is empty" <| fun (xs: int list) ->
                let set = Set.fromList xs
                Set.diff set set = Set.empty
        ]

        testList "Basics Laws" [
            testProperty "compare is reflexive" <| fun (x: int) ->
                Basics.compare x x = EQ

            testProperty "compare is antisymmetric" <| fun (x: int) (y: int) ->
                let c1 = Basics.compare x y
                let c2 = Basics.compare y x
                match c1, c2 with
                | LT, GT | GT, LT | EQ, EQ -> true
                | _ -> false

            testProperty "max is commutative" <| fun (x: int) (y: int) ->
                Basics.max x y = Basics.max y x

            testProperty "min is commutative" <| fun (x: int) (y: int) ->
                Basics.min x y = Basics.min y x

            testProperty "clamp bounds value" <| fun (low: int) (high: int) (x: int) ->
                let l = min low high
                let h = max low high
                let clamped = Basics.clamp l h x
                clamped >= l && clamped <= h

            testProperty "abs is non-negative" <| fun (x: int) ->
                Basics.abs x >= 0

            testProperty "negate twice is identity" <| fun (x: int) ->
                Basics.negate (Basics.negate x) = x

            testProperty "add is commutative" <| fun (x: int) (y: int) ->
                Basics.add x y = Basics.add y x

            testProperty "multiply is commutative" <| fun (x: int) (y: int) ->
                Basics.multiply x y = Basics.multiply y x
        ]

        testList "Tuple Laws" [
            testProperty "first of pair returns first element" <| fun (x: int) (y: int) ->
                Tuple.first (Tuple.pair x y) = x

            testProperty "second of pair returns second element" <| fun (x: int) (y: int) ->
                Tuple.second (Tuple.pair x y) = y

            testProperty "mapFirst doesn't affect second" <| fun (x: int) (y: int) ->
                Tuple.second (Tuple.mapFirst ((+) 1) (x, y)) = y

            testProperty "mapSecond doesn't affect first" <| fun (x: int) (y: int) ->
                Tuple.first (Tuple.mapSecond ((+) 1) (x, y)) = x
        ]
    ]
