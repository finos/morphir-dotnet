// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
// Fixture authoring only. The generated Morphir model contains the rental rules.
// Run: dotnet fsi examples/car-rental/tools/generate-model.fsx

open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

type Json =
    | Text of string
    | Number of int
    | Null
    | Array of Json list
    | Object of (string * Json) list

let rec write (writer: Utf8JsonWriter) =
    function
    | Text text -> writer.WriteStringValue text
    | Number number -> writer.WriteNumberValue number
    | Null -> writer.WriteNullValue()
    | Array values ->
        writer.WriteStartArray()
        values |> List.iter (write writer)
        writer.WriteEndArray()
    | Object fields ->
        writer.WriteStartObject()

        for key, value in fields do
            writer.WritePropertyName key
            write writer value

        writer.WriteEndObject()

let serialize json =
    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
    write writer json
    writer.Flush()
    Encoding.UTF8.GetString(stream.ToArray()) + "\n"

let words value =
    Regex.Matches(value, "[A-Z]?[a-z]+|[0-9]+")
    |> Seq.map (fun word -> word.Value.ToLowerInvariant())
    |> Seq.toList

let name value = words value |> List.map Text |> Array

let path (value: string) =
    value.Split('.') |> Array.toList |> List.map name |> Array

let package = path "Car.Rental"
let sdk = Array [ Array [ Text "morphir" ]; Array [ Text "s"; Text "d"; Text "k" ] ]

let build version =
    let tag kind value =
        if
            version = 1
            || (version = 2 && List.contains kind [ "value"; "pattern"; "literal" ])
        then
            words value |> String.concat "_" |> Text
        else
            Text value

    let access value =
        if version = 1 then
            Array [ Text "public"; value ]
        else
            Object [ "access", Text "Public"; "value", value ]

    let fq isSdk moduleName local =
        Array [ (if isSdk then sdk else package); path moduleName; name local ]

    let typ isSdk moduleName local args =
        Array [ tag "type" "Reference"; Object []; fq isSdk moduleName local; Array args ]

    let core moduleName local args = typ true moduleName local args
    let localType local = typ false "Domain" local []

    let node valueTag attrs parts =
        let metadata =
            if attrs = Null then
                Object []
            else
                Object [ ("type", attrs) ]

        Array(tag "value" valueTag :: metadata :: parts)

    let plain valueTag parts = node valueTag Null parts
    let variable local = plain "Variable" [ name local ]

    let reference isSdk moduleName local =
        plain "Reference" [ fq isSdk moduleName local ]

    let call expression args =
        args |> List.fold (fun func arg -> plain "Apply" [ func; arg ]) expression

    let invoke moduleName local args =
        call (reference false moduleName local) args

    let op local args =
        call (reference true "Basics" local) args

    let ctor isSdk moduleName local args =
        call (plain "Constructor" [ fq isSdk moduleName local ]) args

    let domainCase local args = ctor false "Domain" local args

    let literal valueTag value =
        plain "Literal" [ Array [ tag "literal" valueTag; value ] ]

    let integer value =
        literal "WholeNumberLiteral" (Number value)

    let text value = literal "StringLiteral" (Text value)
    let field obj local = plain "Field" [ obj; name local ]

    let pattern patternTag parts =
        Array(tag "pattern" patternTag :: Object [] :: parts)

    let wild = pattern "WildcardPattern" []

    let bind local =
        pattern "AsPattern" [ wild; name local ]

    let case isSdk moduleName local patterns =
        pattern "ConstructorPattern" [ fq isSdk moduleName local; Array patterns ]

    let domainPattern local patterns = case false "Domain" local patterns

    let matchWith expression cases =
        plain "PatternMatch" [ expression; Array(List.map Array cases) ]

    let fields pairs =
        pairs |> List.map (fun (key, value) -> Array [ name key; value ]) |> Array

    let record recordType pairs =
        node "Record" recordType [ fields pairs ]

    let update obj recordType pairs =
        node "UpdateRecord" recordType [ obj; fields pairs ]

    let definition local inputs output body =
        let value =
            Object
                [ "inputTypes", inputs |> List.map (fun (n, t) -> Array [ name n; Object []; t ]) |> Array
                  "outputType", output
                  "body", body ]

        Array [ name local; access (Object [ "doc", Text local; "value", value ]) ]

    let alias local pairs =
        let recordType =
            Array
                [ tag "type" "Record"
                  Object []
                  pairs |> List.map (fun (n, t) -> Object [ "name", name n; "tpe", t ]) |> Array ]

        let value = Array [ tag "type" "TypeAliasDefinition"; Array []; recordType ]
        Array [ name local; access (Object [ "doc", Text local; "value", value ]) ]

    let union local cases =
        let constructors =
            cases |> List.map (fun (n, args) -> Array [ name n; fields args ]) |> Array

        let value =
            Array [ tag "type" "CustomTypeDefinition"; Array []; access constructors ]

        Array [ name local; access (Object [ "doc", Text local; "value", value ]) ]

    let moduleDefinition local types values =
        let value = Object [ "types", Array types; "values", Array values ]

        if version = 1 then
            Object [ "name", path local; "def", access value ]
        else
            Array [ path local; access value ]

    let intType = core "Basics" "Int" []
    let stringType = core "String" "String" []
    let vehicleType = localType "Vehicle"
    let invoiceType = localType "Invoice"
    let completionType = localType "CompletedRental"
    let pendingType = localType "PendingRentals"
    let maybeInvoice = core "Maybe" "Maybe" [ invoiceType ]
    let resultType = core "Result" "Result" [ localType "RentalError"; vehicleType ]
    let current = variable "vehicle"
    let days = variable "days"
    let rate = variable "rate"
    let ok value = ctor true "Result" "Ok" [ value ]

    let error local =
        ctor true "Result" "Err" [ domainCase local [] ]

    let updateStatus status =
        update current vehicleType [ ("status", status) ] |> ok

    let invalidDays success =
        plain "IfThenElse" [ op "LessThanOrEqual" [ days; integer 0 ]; error "InvalidDuration"; success ]

    let domainTypes =
        [ union "Availability" ([ "Available"; "Reserved"; "Rented" ] |> List.map (fun n -> n, []))
          union
              "RentalError"
              ([ "Unavailable"
                 "ReservationRequired"
                 "ActiveRentalRequired"
                 "InvalidDuration"
                 "ReturnRequired"
                 "AlreadyBilled" ]
               |> List.map (fun n -> n, []))
          alias
              "Invoice"
              [ "vehicleId", stringType
                "dailyRate", intType
                "days", intType
                "totalCents", intType ]
          alias "CompletedRental" [ "vehicleId", stringType; "dailyRate", intType; "days", intType ]
          union
              "PendingRentals"
              [ "NoPendingRentals", []
                "PendingRental", [ "rental", completionType; "remaining", pendingType ] ]
          union
              "RentalStatus"
              [ "Unreserved", []
                "Confirmed", [ ("rate", intType) ]
                "Active", [ ("rate", intType) ]
                "Completed", [ "rate", intType; "days", intType ]
                "Invoiced", [ ("invoice", invoiceType) ] ]
          alias
              "Vehicle"
              [ "id", stringType
                "dailyRate", intType
                "status", localType "RentalStatus"
                "pendingRentals", pendingType
                "lastInvoice", maybeInvoice ] ]

    let domain = moduleDefinition "Domain" domainTypes []

    let inventory =
        moduleDefinition
            "Inventory"
            []
            [ definition "dailyRate" [] intType (integer 5000)
              definition
                  "car"
                  []
                  vehicleType
                  (record
                      vehicleType
                      [ "id", text "CAR-1"
                        "dailyRate", reference false "Inventory" "DailyRate"
                        "status", domainCase "Unreserved" []
                        "pendingRentals", domainCase "NoPendingRentals" []
                        "lastInvoice", ctor true "Maybe" "Nothing" [] ])
              definition
                  "availability"
                  [ ("vehicle", vehicleType) ]
                  (localType "Availability")
                  (matchWith
                      (field current "status")
                      [ [ domainPattern "Confirmed" [ wild ]; domainCase "Reserved" [] ]
                        [ domainPattern "Active" [ wild ]; domainCase "Rented" [] ]
                        [ wild; domainCase "Available" [] ] ])
              definition
                  "lookup"
                  [ "id", stringType; "vehicles", core "List" "List" [ vehicleType ] ]
                  (core "Maybe" "Maybe" [ vehicleType ])
                  (matchWith
                      (variable "vehicles")
                      [ [ pattern "EmptyListPattern" []; ctor true "Maybe" "Nothing" [] ]
                        [ pattern "HeadTailPattern" [ bind "head"; bind "tail" ]
                          plain
                              "IfThenElse"
                              [ op "Equal" [ field (variable "head") "id"; variable "id" ]
                                ctor true "Maybe" "Just" [ variable "head" ]
                                invoke "Inventory" "Lookup" [ variable "id"; variable "tail" ] ] ] ]) ]

    let reserve = updateStatus (domainCase "Confirmed" [ field current "dailyRate" ])

    let reservation =
        moduleDefinition
            "Reservation"
            []
            [ definition
                  "reserve"
                  [ "days", intType; "vehicle", vehicleType ]
                  resultType
                  (invalidDays (
                      matchWith
                          (field current "status")
                          [ [ domainPattern "Unreserved" []; reserve ]
                            [ domainPattern "Completed" [ wild; wild ]; reserve ]
                            [ domainPattern "Invoiced" [ wild ]; reserve ]
                            [ wild; error "Unavailable" ] ]
                  )) ]

    let pickup =
        moduleDefinition
            "Pickup"
            []
            [ definition
                  "pickup"
                  [ ("vehicle", vehicleType) ]
                  resultType
                  (matchWith
                      (field current "status")
                      [ [ domainPattern "Confirmed" [ bind "rate" ]
                          updateStatus (domainCase "Active" [ rate ]) ]
                        [ wild; error "ReservationRequired" ] ]) ]

    let returns =
        moduleDefinition
            "Return"
            []
            [ definition
                  "returnCar"
                  [ "days", intType; "vehicle", vehicleType ]
                  resultType
                  (invalidDays (
                      matchWith
                          (field current "status")
                          [ [ domainPattern "Active" [ bind "rate" ]
                              update
                                  current
                                  vehicleType
                                  [ "status", domainCase "Completed" [ rate; days ]
                                    "pendingRentals",
                                    domainCase
                                        "PendingRental"
                                        [ record
                                              completionType
                                              [ "vehicleId", field current "id"; "dailyRate", rate; "days", days ]
                                          field current "pendingRentals" ] ]
                              |> ok ]
                            [ wild; error "ActiveRentalRequired" ] ]
                  )) ]

    let completion = variable "completion"

    let invoice =
        record
            invoiceType
            [ "vehicleId", field completion "vehicleId"
              "dailyRate", field completion "dailyRate"
              "days", field completion "days"
              "totalCents", op "Multiply" [ field completion "dailyRate"; field completion "days" ] ]

    let billedStatus =
        matchWith
            (field current "status")
            [ [ domainPattern "Completed" [ wild; wild ]; domainCase "Invoiced" [ invoice ] ]
              [ bind "currentStatus"; variable "currentStatus" ] ]

    let billing =
        moduleDefinition
            "Billing"
            []
            [ definition
                  "bill"
                  [ ("vehicle", vehicleType) ]
                  resultType
                  (matchWith
                      (field current "pendingRentals")
                      [ [ domainPattern "PendingRental" [ bind "completion"; bind "remaining" ]
                          update
                              current
                              vehicleType
                              [ "status", billedStatus
                                "pendingRentals", variable "remaining"
                                "lastInvoice", ctor true "Maybe" "Just" [ invoice ] ]
                          |> ok ]
                        [ domainPattern "NoPendingRentals" []
                          matchWith
                              (field current "lastInvoice")
                              [ [ case true "Maybe" "Nothing" []; error "ReturnRequired" ]
                                [ wild; error "AlreadyBilled" ] ] ] ])
              definition "invoice" [ ("vehicle", vehicleType) ] maybeInvoice (field current "lastInvoice") ]

    let andThen expression local following =
        matchWith
            expression
            [ [ case true "Result" "Ok" [ bind local ]; following ]
              [ case true "Result" "Err" [ bind "error" ]
                ctor true "Result" "Err" [ variable "error" ] ] ]

    let journey =
        andThen
            (invoke "Reservation" "Reserve" [ days; reference false "Inventory" "Car" ])
            "reserved"
            (andThen
                (invoke "Pickup" "Pickup" [ variable "reserved" ])
                "pickedUp"
                (andThen
                    (invoke "Return" "ReturnCar" [ days; variable "pickedUp" ])
                    "returned"
                    (invoke "Billing" "Bill" [ variable "returned" ])))

    let workflow =
        moduleDefinition "Workflow" [] [ definition "rent" [ ("days", intType) ] resultType journey ]

    Object
        [ "formatVersion", Number version
          "distribution",
          Array
              [ tag "type" "Library"
                package
                Array []
                Object [ ("modules", Array [ workflow; billing; returns; pickup; reservation; inventory; domain ]) ] ] ]

let output = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "models"))
Directory.CreateDirectory output |> ignore

for version in [ 1; 2; 3 ] do
    File.WriteAllText(Path.Combine(output, $"car-rental-v{version}.json"), serialize (build version))

printfn "Wrote classic IR v1/v2/v3 car rental fixtures to %s" output
