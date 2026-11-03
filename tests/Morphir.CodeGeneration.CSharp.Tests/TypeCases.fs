module Morphir.CodeGeneration.CSharp.Tests.TypeCases

open System.Text.Json
open Morphir.IR
open Morphir.IR.Model

let n = Name.fromString
let package = PackageName.packageNameFromString "example"
let path = ModulePath.modulePathFromString "data"
let own local = FQName.fqName package path (n local)

let sdk modul local =
    FQName.fqName
        (PackageName.packageNameFromString "morphir.s_d_k")
        (ModulePath.modulePathFromString modul)
        (n local)

let version text =
    SourceFormatVersion.parse text
    |> Result.defaultWith (fun e -> failwithf "%A" e)

let ordered entries =
    Morphir.IR.V4.OrderedMap.ofList entries
    |> Result.defaultWith (fun e -> failwithf "%A" e)

let a = Unchecked.defaultof<JsonElement>

let classic () =
    let reference q args =
        Morphir.IR.Classic.Type.Reference(a, q, args)

    let intType = reference (sdk "basics" "int") []
    let stringType = reference (sdk "string" "string") []
    let listType t = reference (sdk "list" "list") [ t ]

    let alias args body =
        Morphir.IR.Classic.Type.TypeAliasDefinition(args, body)

    let record fields =
        Morphir.IR.Classic.Type.Record(
            a,
            fields
            |> List.map (fun (name, typ) -> {
                Morphir.IR.Classic.Field.Name = n name
                Type = typ
            })
        )

    let publicType value =
        Morphir.IR.Classic.AccessControlled.public' (
            Morphir.IR.Classic.Documented.WithoutDocumentation value
        )

    let privateType value =
        { (publicType value) with
            Access = Morphir.IR.Classic.AccessControlled.Private
        }

    let types =
        [
            n "age", publicType (alias [] intType)
            n "hiddenRecord", privateType (alias [] (record [ "rating", intType ]))
            n "publicView", publicType (alias [] (reference (own "hiddenRecord") []))
            n "person",
            publicType (alias [] (record [ "age", reference (own "age") []; "name", stringType ]))
            n "personTwin",
            publicType (alias [] (record [ "age", reference (own "age") []; "name", stringType ]))
            n "people",
            publicType (
                alias
                    []
                    (record [
                        "first", reference (own "person") []
                        "second", reference (own "personTwin") []
                    ])
            )
            n "box",
            publicType (
                alias
                    [ n "a" ]
                    (record [
                        "items", listType (Morphir.IR.Classic.Type.Variable(a, n "a"))
                        "pair", Morphir.IR.Classic.Type.Tuple(a, [ intType; stringType ])
                    ])
            )
            n "intBox",
            publicType (
                alias
                    []
                    (record [
                        "items", listType intType
                        "pair", Morphir.IR.Classic.Type.Tuple(a, [ intType; stringType ])
                    ])
            )
            n "boxHolder", publicType (alias [] (record [ "box", reference (own "intBox") [] ]))
            n "nestedBox",
            publicType (
                alias [ n "a" ] (record [ "value", Morphir.IR.Classic.Type.Variable(a, n "a") ])
            )
            n "nestedHolder",
            publicType (
                alias
                    []
                    (record [
                        "payload",
                        reference (own "nestedBox") [ reference (own "nestedBox") [ intType ] ]
                    ])
            )
            n "wrapped",
            publicType (
                alias
                    []
                    (record [
                        "meta", record [ "count", intType ]
                        "callback", Morphir.IR.Classic.Type.Function(a, intType, stringType)
                        "unit", Morphir.IR.Classic.Type.Unit a
                    ])
            )
            n "reply",
            publicType (
                alias
                    [ n "a" ]
                    (record [
                        "result",
                        reference (sdk "result" "result") [
                            stringType
                            Morphir.IR.Classic.Type.Variable(a, n "a")
                        ]
                        "maybe",
                        reference (sdk "maybe" "maybe") [
                            Morphir.IR.Classic.Type.Variable(a, n "a")
                        ]
                    ])
            )
            n "keyed",
            publicType (
                alias
                    [ n "k" ]
                    (record [
                        "values",
                        reference (sdk "dict" "dict") [
                            Morphir.IR.Classic.Type.Variable(a, n "k")
                            intType
                        ]
                    ])
            )
            n "choice",
            publicType (
                Morphir.IR.Classic.Type.CustomTypeDefinition(
                    [ n "a" ],
                    Morphir.IR.Classic.AccessControlled.public' (
                        Map.ofList [
                            n "empty", []
                            n "value", [ n "item", Morphir.IR.Classic.Type.Variable(a, n "a") ]
                        ]
                    )
                )
            )
        ]
        |> Map.ofList

    let modul: Morphir.IR.Classic.Module.ModuleDefinition<JsonElement, JsonElement> = {
        Types = types
        Values = Map.empty
        Doc = None
    }

    {
        SourceVersion = version "3"
        Distribution =
            DistributionModel.Classic(
                Morphir.IR.Classic.Distribution.Library(
                    package,
                    Map.empty,
                    {
                        Modules =
                            Map.ofList [ path, Morphir.IR.Classic.AccessControlled.public' modul ]
                    }
                )
            )
    }

let v4 () =
    let a = Morphir.IR.V4.TypeAttributes.empty

    let reference q args =
        Morphir.IR.V4.Type.Reference(a, q, args)

    let intType = reference (sdk "basics" "int") []
    let stringType = reference (sdk "string" "string") []
    let listType t = reference (sdk "list" "list") [ t ]

    let alias args body =
        Morphir.IR.V4.TypeAliasDefinition(args, body)

    let record fields =
        Morphir.IR.V4.Type.Record(
            a,
            fields
            |> List.map (fun (name, typ) -> n name, typ)
        )

    let publicType
        value
        : Morphir.IR.V4.AccessControlled<Morphir.IR.V4.Documented<Morphir.IR.V4.TypeDefinition>> =
        {
            Access = Morphir.IR.V4.Public
            Value = { Doc = None; Value = value }
        }

    let privateType value =
        { (publicType value) with
            Access = Morphir.IR.V4.Private
        }

    let types =
        [
            n "age", publicType (alias [] intType)
            n "hiddenRecord", privateType (alias [] (record [ "rating", intType ]))
            n "publicView", publicType (alias [] (reference (own "hiddenRecord") []))
            n "person",
            publicType (alias [] (record [ "age", reference (own "age") []; "name", stringType ]))
            n "personTwin",
            publicType (alias [] (record [ "age", reference (own "age") []; "name", stringType ]))
            n "people",
            publicType (
                alias
                    []
                    (record [
                        "first", reference (own "person") []
                        "second", reference (own "personTwin") []
                    ])
            )
            n "box",
            publicType (
                alias
                    [ n "a" ]
                    (record [
                        "items", listType (Morphir.IR.V4.Type.Variable(a, n "a"))
                        "pair", Morphir.IR.V4.Type.Tuple(a, [ intType; stringType ])
                    ])
            )
            n "intBox",
            publicType (
                alias
                    []
                    (record [
                        "items", listType intType
                        "pair", Morphir.IR.V4.Type.Tuple(a, [ intType; stringType ])
                    ])
            )
            n "boxHolder", publicType (alias [] (record [ "box", reference (own "intBox") [] ]))
            n "nestedBox",
            publicType (alias [ n "a" ] (record [ "value", Morphir.IR.V4.Type.Variable(a, n "a") ]))
            n "nestedHolder",
            publicType (
                alias
                    []
                    (record [
                        "payload",
                        reference (own "nestedBox") [ reference (own "nestedBox") [ intType ] ]
                    ])
            )
            n "wrapped",
            publicType (
                alias
                    []
                    (record [
                        "meta", record [ "count", intType ]
                        "callback", Morphir.IR.V4.Type.Function(a, intType, stringType)
                        "unit", Morphir.IR.V4.Type.Unit a
                    ])
            )
            n "reply",
            publicType (
                alias
                    [ n "a" ]
                    (record [
                        "result",
                        reference (sdk "result" "result") [
                            stringType
                            Morphir.IR.V4.Type.Variable(a, n "a")
                        ]
                        "maybe",
                        reference (sdk "maybe" "maybe") [ Morphir.IR.V4.Type.Variable(a, n "a") ]
                    ])
            )
            n "keyed",
            publicType (
                alias
                    [ n "k" ]
                    (record [
                        "values",
                        reference (sdk "dict" "dict") [
                            Morphir.IR.V4.Type.Variable(a, n "k")
                            intType
                        ]
                    ])
            )
            n "choice",
            publicType (
                Morphir.IR.V4.CustomTypeDefinition(
                    [ n "a" ],
                    Morphir.IR.V4.Public,
                    ordered [
                        n "empty", []
                        n "value", [ n "item", Morphir.IR.V4.Type.Variable(a, n "a") ]
                    ]
                )
            )
        ]
        |> ordered

    let modul: Morphir.IR.V4.ModuleDefinition = {
        Types = types
        Values = Morphir.IR.V4.OrderedMap.empty
        Doc = None
    }

    {
        SourceVersion = version "4.0.0"
        Distribution =
            DistributionModel.V4(
                Morphir.IR.V4.Library(
                    package,
                    Morphir.IR.V4.OrderedMap.empty,
                    {
                        Modules =
                            ordered [
                                path,
                                {
                                    Access = Morphir.IR.V4.Public
                                    Value = modul
                                }
                            ]
                    }
                )
            )
    }

let withRecursiveAlias model =
    match model.Distribution with
    | DistributionModel.Classic(Morphir.IR.Classic.Distribution.Library(pkg, deps, definition)) ->
        let recursiveAlias =
            Morphir.IR.Classic.Type.TypeAliasDefinition(
                [ n "a" ],
                Morphir.IR.Classic.Type.Reference(
                    a,
                    own "nestedBox",
                    [ Morphir.IR.Classic.Type.Variable(a, n "a") ]
                )
            )
            |> Morphir.IR.Classic.Documented.WithoutDocumentation
            |> Morphir.IR.Classic.AccessControlled.public'

        let modules =
            definition.Modules
            |> Map.map (fun _ controlled ->
                { controlled with
                    Value =
                        { controlled.Value with
                            Types = Map.add (n "nestedBox") recursiveAlias controlled.Value.Types
                        }
                }
            )

        { model with
            Distribution =
                DistributionModel.Classic(
                    Morphir.IR.Classic.Distribution.Library(pkg, deps, { Modules = modules })
                )
        }
    | DistributionModel.V4(Morphir.IR.V4.Library(pkg, deps, definition)) ->
        let attributes = Morphir.IR.V4.TypeAttributes.empty

        let recursiveAlias
            : Morphir.IR.V4.AccessControlled<Morphir.IR.V4.Documented<Morphir.IR.V4.TypeDefinition>> = {
            Access = Morphir.IR.V4.Public
            Value = {
                Doc = None
                Value =
                    Morphir.IR.V4.TypeAliasDefinition(
                        [ n "a" ],
                        Morphir.IR.V4.Type.Reference(
                            attributes,
                            own "nestedBox",
                            [ Morphir.IR.V4.Type.Variable(attributes, n "a") ]
                        )
                    )
            }
        }

        let modules =
            definition.Modules
            |> Morphir.IR.V4.OrderedMap.toList
            |> List.map (fun (path, controlled) ->
                let types =
                    controlled.Value.Types
                    |> Morphir.IR.V4.OrderedMap.toList
                    |> List.map (fun (name, value) ->
                        name, (if name = n "nestedBox" then recursiveAlias else value)
                    )
                    |> ordered

                path,
                { controlled with
                    Value = { controlled.Value with Types = types }
                }
            )
            |> ordered

        { model with
            Distribution =
                DistributionModel.V4(Morphir.IR.V4.Library(pkg, deps, { Modules = modules }))
        }
    | _ -> failwith "Expected a typed library fixture."

let hideConstructors model =
    match model.Distribution with
    | DistributionModel.Classic(Morphir.IR.Classic.Distribution.Library(pkg, deps, definition)) ->
        let modules =
            definition.Modules
            |> Map.map (fun _ controlled ->
                let types =
                    controlled.Value.Types
                    |> Map.map (fun name controlled ->
                        if
                            name
                            <> n "choice"
                        then
                            controlled
                        else
                            let body = Morphir.IR.Classic.Documented.value controlled.Value

                            let body =
                                match body with
                                | Morphir.IR.Classic.Type.CustomTypeDefinition(ps, cases) ->
                                    Morphir.IR.Classic.Type.CustomTypeDefinition(
                                        ps,
                                        Morphir.IR.Classic.AccessControlled.private' cases.Value
                                    )
                                | _ -> failwith "Expected custom fixture type."

                            { controlled with
                                Value = Morphir.IR.Classic.Documented.WithoutDocumentation body
                            }
                    )

                { controlled with
                    Value = { controlled.Value with Types = types }
                }
            )

        { model with
            Distribution =
                DistributionModel.Classic(
                    Morphir.IR.Classic.Distribution.Library(pkg, deps, { Modules = modules })
                )
        }
    | DistributionModel.V4(Morphir.IR.V4.Library(pkg, deps, definition)) ->
        let modules =
            definition.Modules
            |> Morphir.IR.V4.OrderedMap.toList
            |> List.map (fun (path, controlled) ->
                let types =
                    controlled.Value.Types
                    |> Morphir.IR.V4.OrderedMap.toList
                    |> List.map (fun (name, controlled) ->
                        let value =
                            if
                                name
                                <> n "choice"
                            then
                                controlled
                            else
                                let body =
                                    match controlled.Value.Value with
                                    | Morphir.IR.V4.CustomTypeDefinition(ps, _, cases) ->
                                        Morphir.IR.V4.CustomTypeDefinition(
                                            ps,
                                            Morphir.IR.V4.Private,
                                            cases
                                        )
                                    | _ -> failwith "Expected custom fixture type."

                                { controlled with
                                    Value = { controlled.Value with Value = body }
                                }

                        name, value
                    )
                    |> ordered

                path,
                { controlled with
                    Value = { controlled.Value with Types = types }
                }
            )
            |> ordered

        { model with
            Distribution =
                DistributionModel.V4(Morphir.IR.V4.Library(pkg, deps, { Modules = modules }))
        }
    | _ -> failwith "Expected library fixture."

let consumer =
    """
public static class Observation
{
    public static int Run()
    {
        var person = new Example.DataModule.Person(7, "Ada");
        var view = new Example.DataModule.PublicView(9);
        if (view.Rating != 9) throw new System.Exception("Public alias of private structural record");
        var reply = new Example.DataModule.Reply<int>(Morphir.SDK.CSharp.Result<string,int>.Err("bad"),Morphir.SDK.CSharp.Maybe<int>.Just(7));
        if (reply.Result.Match(error => error.Length, value => value) != 3 || reply.Maybe.Match(value => value, () => 0) != 7) throw new System.Exception("Result error/value meaning or Maybe mapping");
        var keyed = new Example.DataModule.Keyed<string>(Morphir.SDK.CSharp.Dict<string,int>.Singleton("k",9,Morphir.SDK.CSharp.MorphirComparers.String));
        if (keyed.Values.Get("k").Match(value => value, () => 0) != 9) throw new System.Exception("Immutable generic dictionary");
        var box = new Example.DataModule.Box<int>(Morphir.SDK.CSharp.List<int>.Singleton(5), (2, "two"));
        var nested = new Example.DataModule.NestedHolder(new Example.DataModule.NestedBox<Example.DataModule.NestedBox<int>>(new Example.DataModule.NestedBox<int>(42)));
        var equalNested = new Example.DataModule.NestedHolder(new Example.DataModule.NestedBox<Example.DataModule.NestedBox<int>>(new Example.DataModule.NestedBox<int>(42)));
        if (nested.Payload.Value.Value != 42 || !nested.Equals(equalNested) || nested.GetHashCode() != equalNested.GetHashCode()) throw new System.Exception("Finite nested aliases preserve typed structural values");
        Example.DataModule.Choice<int> choice = new Example.DataModule.Choice<int>.Value(3);
        return person.Age + box.Items[0] + box.Pair.Item1 + ((Example.DataModule.Choice<int>.Value)choice).Item;
    }
}
"""

let equalityConsumer =
    """
public static class Observation
{
    public static int Run()
    {
        var a = new Example.DataModule.Box<double>(Morphir.SDK.CSharp.List<double>.Singleton(double.NaN), (2, "two"));
        var b = new Example.DataModule.Box<double>(Morphir.SDK.CSharp.List<double>.Singleton(double.NaN), (2, "two"));
        if (a.Equals(b) || Morphir.SDK.CSharp.Basics.Equal((object)a, (object)b)) throw new System.Exception("NaN acquired CLR equality");
        var x = new Example.DataModule.Box<double>(Morphir.SDK.CSharp.List<double>.Singleton(-0.0), (2, "two"));
        var y = new Example.DataModule.Box<double>(Morphir.SDK.CSharp.List<double>.Singleton(+0.0), (2, "two"));
        if (!x.Equals(y) || x.GetHashCode() != y.GetHashCode()) throw new System.Exception("Structural zero equality/hash");
        object c = new Example.DataModule.Choice<Example.DataModule.Box<double>>.Value(x);
        object d = new Example.DataModule.Choice<Example.DataModule.Box<double>>.Value(y);
        Example.DataModule.Choice<int> firstCase = new Example.DataModule.Choice<int>.Value(3);
        Example.DataModule.Choice<int> secondCase = new Example.DataModule.Choice<int>.Value(3);
        if (firstCase != secondCase || !firstCase.Equals(secondCase)) throw new System.Exception("Base-typed case equality");
        if (!Morphir.SDK.CSharp.Basics.Equal(c,d) || c.GetHashCode() != d.GetHashCode()) throw new System.Exception("Boxed nested case equality/hash");
        var m = new Example.DataModule.Box<decimal>(Morphir.SDK.CSharp.List<decimal>.Singleton(1.00m), (2,"two"));
        var n = new Example.DataModule.Box<decimal>(Morphir.SDK.CSharp.List<decimal>.Singleton(1.0m), (2,"two"));
        if (!m.Equals(n) || m.GetHashCode() != n.GetHashCode()) throw new System.Exception("Decimal scale equality/hash");
        return 1;
    }
}
"""

let aliasConsumer =
    """
public static class Observation
{
    public static int Run()
    {
        var pair = new Example.DataModule.People(new Example.DataModule.Person(7,"Ada"),new Example.DataModule.Person(8,"Grace"));
        var holder = new Example.DataModule.BoxHolder(new Example.DataModule.Box<int>(Morphir.SDK.CSharp.List<int>.Singleton(1),(2,"two")));
        if (holder.Box.Items[0] != 1) throw new System.Exception("Generic record alias transparency");
        return pair.First.Age + pair.Second.Age;
    }
}
"""

let nestedConsumer =
    """
public static class Observation
{
    public static int Run()
    {
        var type = typeof(Example.DataModule.Wrapped);
        var meta = type.GetProperty("Meta");
        if (meta.CanWrite || meta.PropertyType.GetProperty("Count").CanWrite) throw new System.Exception("Mutable record property");
        if (meta.PropertyType.GetProperty("Count").PropertyType != typeof(int)) throw new System.Exception("Nested field type");
        if (type.GetProperty("Callback").PropertyType != typeof(System.Func<int,string>)) throw new System.Exception("Function type");
        if (type.GetProperty("Unit").PropertyType != typeof(Morphir.SDK.CSharp.Unit)) throw new System.Exception("Unit type");
        return 1;
    }
}
"""
