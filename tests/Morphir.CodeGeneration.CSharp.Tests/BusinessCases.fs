module Morphir.CodeGeneration.CSharp.Tests.BusinessCases

open System
open System.Reflection
open System.Text.Json
open Morphir.IR
open Morphir.IR.Model

type FixtureSource = {
    Name: string
    Path: string
    Sha256: string
}

let sources = [
    {
        Name = "car-rental-v1.json"
        Path = "examples/car-rental/models/car-rental-v1.json"
        Sha256 = "d0bfc9a2b995ed9ee1b4cbcdd71ea9a52d38c2aa17cf39d3436cf3cd65c6b9fb"
    }
    {
        Name = "car-rental-v2.json"
        Path = "examples/car-rental/models/car-rental-v2.json"
        Sha256 = "2392ecb6de47ec307db9d5e78a492a4d4d2906be7e61c91ef9c1b1ce4141acc0"
    }
    {
        Name = "car-rental-v3.json"
        Path = "examples/car-rental/models/car-rental-v3.json"
        Sha256 = "96e8f19a11c3445ca725530281a32ea42b899e755c57a451a91fd35f9cdb9b97"
    }
    {
        Name = "daily-rate-v3.json"
        Path = "tests/Fixtures/MorphirIR/daily-rate-v3.json"
        Sha256 = "8a30c24fac7d4f3cc86290e9cbd49597b77bb44957c93f9b2eca13966d029ef7"
    }
    {
        Name = "daily-rate-v4.json"
        Path = "tests/Fixtures/MorphirIR/daily-rate-v4.json"
        Sha256 = "2bdaaf2b6415203d6b699bac915a4f104224efd370743ffef5eae09ff70d1568"
    }
    {
        Name = "books-and-records-example.json"
        Path = "tests/Fixtures/MorphirIon/v4/books-and-records-example.json"
        Sha256 = "bdc25e9fb332a49f7f12bfa80c907eca482ae5b63bad07575ef06194ab99fda3"
    }
    {
        Name = "complete-example.json"
        Path = "tests/Fixtures/MorphirIon/v4/complete-example.json"
        Sha256 = "30fbea9879b377dde11d0a4916b926dba152069e7b24bd43e4cf33cb27fcd979"
    }
]


// Resources contain original bytes. JSON reading is a test edge, not a generator dependency.
let read name =
    use stream =
        Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream(
                "Business."
                + name
            )

    if isNull stream then
        failwithf "Missing original fixture %s" name

    use buffer = new IO.MemoryStream()
    stream.CopyTo buffer
    let bytes = buffer.ToArray()

    let source =
        sources
        |> List.find (fun source -> source.Name = name)

    let hash =
        Convert
            .ToHexString(Security.Cryptography.SHA256.HashData bytes)
            .ToLowerInvariant()

    if
        hash
        <> source.Sha256
    then
        failwithf "Original source hash changed: %s" source.Path

    use document = JsonDocument.Parse(ReadOnlyMemory<byte>(bytes))
    let root = document.RootElement
    let version = root.GetProperty("formatVersion").ToString()

    let distribution =
        if version.StartsWith("4", StringComparison.Ordinal) then
            Morphir.Json.V4.V4Json.readDistribution root
            |> Result.map (fun value -> DistributionModel.V4 value.Distribution)
            |> Result.defaultWith (fun error -> failwithf "%s: %A" name error)
        else
            Morphir.Json.Classic.ClassicJson.readDistribution root
            |> Result.map (fun value -> DistributionModel.Classic value.Distribution)
            |> Result.defaultWith (fun error -> failwithf "%s: %A" name error)

    {
        SourceVersion = TypeCases.version version
        Distribution = distribution
    }

// SYNTHESIZED TEST INPUT ONLY. This constructs an equivalent typed v4 car model.
// It is neither an original upstream v4 fixture nor production migration support.
// It never supplies an expected business result and is not called by production.
let synthesizedV4Car () =
    let ta = Morphir.IR.V4.TypeAttributes.empty
    let va = Morphir.IR.V4.ValueAttributes.empty

    let access =
        function
        | Morphir.IR.Classic.AccessControlled.Public -> Morphir.IR.V4.Public
        | Morphir.IR.Classic.AccessControlled.Private -> Morphir.IR.V4.Private

    let controlled
        mapper
        (entry: Morphir.IR.Classic.AccessControlled.AccessControlled<_>)
        : Morphir.IR.V4.AccessControlled<_> =
        {
            Access = access entry.Access
            Value = mapper entry.Value
        }

    let documented mapper original : Morphir.IR.V4.Documented<_> = {
        Doc = Morphir.IR.Classic.Documented.doc original
        Value = mapper (Morphir.IR.Classic.Documented.value original)
    }

    let ordered mapper entries =
        entries
        |> Map.toList
        |> List.map (fun (key, entry) -> key, mapper entry)
        |> TypeCases.ordered

    let rec typ =
        function
        | Morphir.IR.Classic.Type.Variable(_, name) -> Morphir.IR.V4.Type.Variable(ta, name)
        | Morphir.IR.Classic.Type.Reference(_, name, args) ->
            Morphir.IR.V4.Type.Reference(ta, name, List.map typ args)
        | Morphir.IR.Classic.Type.Tuple(_, items) ->
            Morphir.IR.V4.Type.Tuple(ta, List.map typ items)
        | Morphir.IR.Classic.Type.Record(_, fields) ->
            Morphir.IR.V4.Type.Record(
                ta,
                fields
                |> List.map (fun field -> field.Name, typ field.Type)
            )
        | Morphir.IR.Classic.Type.ExtensibleRecord _ ->
            failwith "The original car fixture must have no open records."
        | Morphir.IR.Classic.Type.Function(_, input, output) ->
            Morphir.IR.V4.Type.Function(ta, typ input, typ output)
        | Morphir.IR.Classic.Type.Unit _ -> Morphir.IR.V4.Type.Unit ta

    let literal =
        function
        | Morphir.IR.Classic.BoolLiteral x -> Morphir.IR.V4.BoolLiteral x
        | Morphir.IR.Classic.StringLiteral x -> Morphir.IR.V4.StringLiteral x
        | Morphir.IR.Classic.WholeNumberLiteral x -> Morphir.IR.V4.IntegerLiteral(bigint x)
        | other -> failwithf "Unexpected original car literal %A" other

    let rec pattern =
        function
        | Morphir.IR.Classic.WildcardPattern _ -> Morphir.IR.V4.WildcardPattern va
        | Morphir.IR.Classic.AsPattern(_, inner, name) ->
            Morphir.IR.V4.AsPattern(va, pattern inner, name)
        | Morphir.IR.Classic.TuplePattern(_, items) ->
            Morphir.IR.V4.TuplePattern(va, List.map pattern items)
        | Morphir.IR.Classic.ConstructorPattern(_, name, items) ->
            Morphir.IR.V4.ConstructorPattern(va, name, List.map pattern items)
        | Morphir.IR.Classic.EmptyListPattern _ -> Morphir.IR.V4.EmptyListPattern va
        | Morphir.IR.Classic.HeadTailPattern(_, head, tail) ->
            Morphir.IR.V4.HeadTailPattern(va, pattern head, pattern tail)
        | Morphir.IR.Classic.LiteralPattern(_, x) -> Morphir.IR.V4.LiteralPattern(va, literal x)
        | Morphir.IR.Classic.UnitPattern _ -> Morphir.IR.V4.UnitPattern va

    let rec value =
        function
        | Morphir.IR.Classic.Value.Literal(_, x) -> Morphir.IR.V4.Value.Literal(va, literal x)
        | Morphir.IR.Classic.Value.Constructor(_, name) -> Morphir.IR.V4.Value.Constructor(va, name)
        | Morphir.IR.Classic.Value.Tuple(_, items) ->
            Morphir.IR.V4.Value.Tuple(va, List.map value items)
        | Morphir.IR.Classic.Value.List(_, items) ->
            Morphir.IR.V4.Value.List(va, List.map value items)
        | Morphir.IR.Classic.Value.Record(_, fields) ->
            Morphir.IR.V4.Value.Record(
                va,
                fields
                |> Map.toList
                |> List.map (fun (name, x) -> name, value x)
            )
        | Morphir.IR.Classic.Value.Variable(_, name) -> Morphir.IR.V4.Value.Variable(va, name)
        | Morphir.IR.Classic.Value.Reference(_, name) -> Morphir.IR.V4.Value.Reference(va, name)
        | Morphir.IR.Classic.Value.Field(_, target, name) ->
            Morphir.IR.V4.Value.Field(va, value target, name)
        | Morphir.IR.Classic.Value.FieldFunction(_, name) ->
            Morphir.IR.V4.Value.FieldFunction(va, name)
        | Morphir.IR.Classic.Value.Apply(_, func, arg) ->
            Morphir.IR.V4.Value.Apply(va, value func, value arg)
        | Morphir.IR.Classic.Value.Lambda(_, p, body) ->
            Morphir.IR.V4.Value.Lambda(va, pattern p, value body)
        | Morphir.IR.Classic.Value.LetDefinition(_, name, definition, body) ->
            Morphir.IR.V4.Value.LetDefinition(
                va,
                name,
                {
                    Doc = None
                    Value = definitionValue definition
                },
                value body
            )
        | Morphir.IR.Classic.Value.LetRecursion _ ->
            failwith "The original car fixture must have no local recursion."
        | Morphir.IR.Classic.Value.Destructure(_, p, target, body) ->
            Morphir.IR.V4.Value.Destructure(va, pattern p, value target, value body)
        | Morphir.IR.Classic.Value.IfThenElse(_, cond, yes, no) ->
            Morphir.IR.V4.Value.IfThenElse(va, value cond, value yes, value no)
        | Morphir.IR.Classic.Value.PatternMatch(_, target, cases) ->
            Morphir.IR.V4.Value.PatternMatch(
                va,
                value target,
                cases
                |> List.map (fun (p, x) -> pattern p, value x)
            )
        | Morphir.IR.Classic.Value.UpdateRecord(_, target, fields) ->
            Morphir.IR.V4.Value.UpdateRecord(
                va,
                value target,
                fields
                |> Map.toList
                |> List.map (fun (name, x) -> name, value x)
            )
        | Morphir.IR.Classic.Value.Unit _ -> Morphir.IR.V4.Value.Unit va

    and definitionValue
        (definition: Morphir.IR.Classic.ValueDefinition<JsonElement, JsonElement>)
        : Morphir.IR.V4.ValueDefinition =
        {
            InputTypes =
                definition.InputTypes
                |> List.map (fun (name, _, t) -> name, typ t)
            Body = Morphir.IR.V4.ExpressionBody(typ definition.OutputType, value definition.Body)
        }

    let definitionType =
        function
        | Morphir.IR.Classic.Type.TypeAliasDefinition(parameters, body) ->
            Morphir.IR.V4.TypeAliasDefinition(parameters, typ body)
        | Morphir.IR.Classic.Type.CustomTypeDefinition(parameters, cases) ->
            Morphir.IR.V4.CustomTypeDefinition(
                parameters,
                access cases.Access,
                ordered (List.map (fun (name, t) -> name, typ t)) cases.Value
            )

    let definitionModule
        (modul: Morphir.IR.Classic.Module.ModuleDefinition<JsonElement, JsonElement>)
        : Morphir.IR.V4.ModuleDefinition =
        {
            Types = ordered (controlled (documented definitionType)) modul.Types
            Values = ordered (controlled (documented definitionValue)) modul.Values
            Doc = modul.Doc
        }

    match (read "car-rental-v3.json").Distribution with
    | DistributionModel.Classic(Morphir.IR.Classic.Distribution.Library(pkg,
                                                                        dependencies,
                                                                        definition)) when
        Map.isEmpty dependencies
          ->
          {
              SourceVersion = TypeCases.version "4.0.0"
              Distribution =
                  DistributionModel.V4(
                      Morphir.IR.V4.Library(
                          pkg,
                          TypeCases.ordered [],
                          {
                              Modules = ordered (controlled definitionModule) definition.Modules
                          }
                      )
                  )
          }
    | _ -> failwith "Expected original dependency-free classic car library."

let booksTypeSlice () =
    let original = read "books-and-records-example.json"

    match original.Distribution with
    | DistributionModel.V4(Morphir.IR.V4.Library(pkg, dependencies, definition)) ->
        let modules =
            definition.Modules
            |> Morphir.IR.V4.OrderedMap.toList
            |> List.map (fun (path, entry) ->
                let excluded =
                    entry.Value.Values
                    |> Morphir.IR.V4.OrderedMap.toList
                    |> List.map (fun (name, value) ->
                        match value.Value.Value.Body with
                        | Morphir.IR.V4.IncompleteBody _ -> name
                        | _ ->
                            failwith "Only original explicit incomplete bodies may be excluded."
                    )

                if
                    Set.ofList excluded
                    <> Set.ofList [ Name.fromString "open-deal"; Name.fromString "close-deal" ]
                then
                    failwith
                        "The compatible type slice must record exactly the two original draft bodies."

                path,
                { entry with
                    Value =
                        { entry.Value with
                            Values = TypeCases.ordered []
                        }
                }
            )
            |> TypeCases.ordered

        { original with
            Distribution =
                DistributionModel.V4(
                    Morphir.IR.V4.Library(pkg, dependencies, { Modules = modules })
                )
        }
    | _ -> failwith "Expected original v4 books-and-records."

// Independent README lifecycle and authored rejection expectations, not a translated oracle.
let carConsumer =
    """
using System;
using Car.Rental;
using Morphir.SDK.CSharp;
public static class Observation {
  static T Ok<E,T>(Result<E,T> value) => value.Match(e => throw new Exception("Unexpected error: " + e), x => x);
  static void Check(bool value) { if (!value) throw new Exception("Car lifecycle observation failed"); }
  static bool Error(Result<DomainModule.RentalError,DomainModule.Vehicle> value, DomainModule.RentalError expected)
    => value.Match(e => Basics.Equal(e,expected), _ => false);
  public static int Run() {
    var initial = InventoryModule.Car();
    Check(InventoryModule.DailyRate() == 5000);
    Check(InventoryModule.Availability(initial) is DomainModule.Availability.Available);
    Check(InventoryModule.Lookup("CAR-1", List<DomainModule.Vehicle>.FromEnumerable(new[]{initial})).Match(x => Basics.Equal(x,initial), () => false));
    var reserved = Ok(ReservationModule.Reserve(3,initial));
    Check(InventoryModule.Availability(reserved) is DomainModule.Availability.Reserved);
    var picked = Ok(PickupModule.Pickup(reserved));
    Check(InventoryModule.Availability(picked) is DomainModule.Availability.Rented);
    var returned = Ok(ReturnModule.ReturnCar(3,picked));
    Check(InventoryModule.Availability(returned) is DomainModule.Availability.Available);
    var billed = Ok(BillingModule.Bill(returned));
    var invoice = BillingModule.Invoice(billed).Match(x => x, () => throw new Exception("Missing invoice"));
    Check(invoice.TotalCents == 15000);
    var next = Ok(ReservationModule.Reserve(2,returned));
    var previous = Ok(BillingModule.Bill(next));
    Check(InventoryModule.Availability(previous) is DomainModule.Availability.Reserved);
    Check(Basics.Equal(BillingModule.Invoice(previous),BillingModule.Invoice(billed)));
    Check(Error(ReservationModule.Reserve(3,reserved),new DomainModule.RentalError.Unavailable()));
    Check(Error(PickupModule.Pickup(initial),new DomainModule.RentalError.ReservationRequired()));
    Check(Error(ReturnModule.ReturnCar(3,initial),new DomainModule.RentalError.ActiveRentalRequired()));
    Check(Error(ReservationModule.Reserve(0,initial),new DomainModule.RentalError.InvalidDuration()));
    Check(Error(ReturnModule.ReturnCar(0,picked),new DomainModule.RentalError.InvalidDuration()));
    Check(Error(BillingModule.Bill(picked),new DomainModule.RentalError.ReturnRequired()));
    Check(Error(BillingModule.Bill(billed),new DomainModule.RentalError.AlreadyBilled()));
    Check(InventoryModule.Availability(initial) is DomainModule.Availability.Available);
    Check(Basics.Equal(Ok(WorkflowModule.Rent(3)),billed));
    return 7;
  }
}
"""

let dailyConsumer =
    """
public static class Observation { public static int Run() {
  if (Car.Rental.InventoryModule.DailyRate() != 5000) throw new System.Exception("Independent daily rate");
  return 5000;
} }
"""

let booksConsumer =
    """
using System;
using Morphir.Reference.Model;
using Morphir.SDK.CSharp;
public static class Observation { public static int Run() {
  var first = new BooksAndRecordsModule.Deal("ABC",12.5,3);
  var second = new BooksAndRecordsModule.Deal("ABC",12.5,3);
  if (!Basics.Equal(first,second) || MorphirValue.Hash(first) != MorphirValue.Hash(second)) throw new Exception("Independent record equality/hash");
  var one = new BooksAndRecordsModule.DealEvent.DealOpened("ABC",12.5,3);
  var two = new BooksAndRecordsModule.DealEvent.DealOpened("ABC",12.5,3);
  if (!Basics.Equal(one,two) || MorphirValue.Hash(one) != MorphirValue.Hash(two)) throw new Exception("Independent case equality/hash");
  var oldBook = Dict<string,BooksAndRecordsModule.Deal>.Empty(MorphirComparers.String);
  var newBook = oldBook.Insert("ABC",first);
  if (oldBook.Get("ABC").IsJust || !newBook.Get("ABC").Match(x => Basics.Equal(x,second), () => false)) throw new Exception("Nested persistence");
  if (!Basics.Equal(newBook,newBook.Insert("ABC",second))) throw new Exception("Nested structural equality");
  if (first.Product != "ABC" || first.Price != 12.5 || first.Quantity != 3) throw new Exception("Payload fidelity");
  foreach (var property in typeof(BooksAndRecordsModule.Deal).GetProperties()) if (property.SetMethod != null) throw new Exception("Mutable generated payload");
  return 1;
} }
"""

// One parameterized corpus is used by the focused Roslyn checks and actual SDK consumers.
let cases = [
    for version in [ "1"; "2"; "3" ] ->
        "car-classic-"
        + version,
        "ORIGINAL CLASSIC FIXTURE",
        [
            "car-rental-v"
            + version
            + ".json"
        ],
        7,
        (fun () ->
            read (
                "car-rental-v"
                + version
                + ".json"
            )
        ),
        carConsumer
    yield
        "car-v4-synthesized",
        "SYNTHESIZED TEST INPUT",
        [ "car-rental-v3.json" ],
        7,
        synthesizedV4Car,
        carConsumer
    yield
        "daily-classic",
        "ORIGINAL CLASSIC FIXTURE",
        [ "daily-rate-v3.json" ],
        5000,
        (fun () -> read "daily-rate-v3.json"),
        dailyConsumer
    yield
        "daily-v4",
        "ORIGINAL V4 FIXTURE",
        [ "daily-rate-v4.json" ],
        5000,
        (fun () -> read "daily-rate-v4.json"),
        dailyConsumer
    yield
        "books-v4-types",
        "COMPATIBLE TYPE SLICE",
        [ "books-and-records-example.json" ],
        1,
        booksTypeSlice,
        booksConsumer
]
