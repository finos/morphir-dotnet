namespace Morphir.Testing

/// Behavioral expectations are independent of the representation adapters that supply inputs.
module GenerationCases =
    let derivedDependency required variants : GenerationConformance.BehaviorCase<'Input> = {
        Id = "metadata.derived-dependency"
        Consumer = "printfn \"%d\" Car.Rental.Refusal.value"
        ExpectedOutput = "42"
        Required = required
        Variants = variants
    }

    let sdkMappings required variants : GenerationConformance.BehaviorCase<'Input> = {
        Id = "sdk.collection-mappings"
        Consumer =
            "printfn \"%A %A %A\" Car.Rental.Mappings.numbers Car.Rental.Mappings.maybe Car.Rental.Mappings.result"
        ExpectedOutput = "[2; 3] Some 4 Ok 5"
        Required = required
        Variants = variants
    }

    let letRecOrder required variants : GenerationConformance.BehaviorCase<'Input> = {
        Id = "scope.letrec-dependency-order"
        Consumer = "printfn \"%d\" Car.Rental.Locals.ordered"
        ExpectedOutput = "7"
        Required = required
        Variants = variants
    }

    let lexicalCapture required variants : GenerationConformance.BehaviorCase<'Input> = {
        Id = "scope.lexical-capture"
        Consumer = "printfn \"%A\" Car.Rental.Capture.locals"
        ExpectedOutput = "(5007, 5007, 5007, 5007, 5000)"
        Required = required
        Variants = variants
    }

    let localRecursion required variants : GenerationConformance.BehaviorCase<'Input> = {
        Id = "scope.recursion-and-shadowing"
        Consumer = "printfn \"%d %d\" (Car.Rental.Locals.countdown 5) (Car.Rental.Locals.shadow 2)"
        ExpectedOutput = "0 9"
        Required = required
        Variants = variants
    }

    let recordOperations required variants : GenerationConformance.BehaviorCase<'Input> = {
        Id = "record.update"
        Consumer =
            "let old = Car.Rental.Records.item\nlet changed = Car.Rental.Records.change old\nprintfn \"%d %d\" (Car.Rental.Records.accessor old) changed.value"
        ExpectedOutput = "3 7"
        Required = required
        Variants = variants
    }

    let expressionCore required variants : GenerationConformance.BehaviorCase<'Input> = {
        Id = "expressions.core"
        Consumer =
            "open Car.Rental.Expressions\n"
            + "let reports = [report 0; report 2; report (-1)] |> String.concat \"|\"\n"
            + "let items = mapped |> List.map string |> String.concat \",\"\n"
            + "let partialText = match partialGood \"ok\" with Good(n, label) -> string n + \":\" + label | Bad -> \"bad\"\n"
            + "printfn \"%s|%s|%d|%s|%d|%d|%d|%s\" reports items (scoreOf record) (pairText pair) (headOrZero mapped) (unitToZero unitValue) anonymous.score partialText"
        ExpectedOutput = "zero|good|bad|2,3|3|hi|2|0|4|7:ok"
        Required = required
        Variants = variants
    }

    let recordBilling required variants : GenerationConformance.BehaviorCase<'Input> = {
        Id = "record.billing"
        Consumer = "printfn \"%d\" (Car.Rental.Workflow.bill 3 Car.Rental.Workflow.car)"
        ExpectedOutput = "15000"
        Required = required
        Variants = variants
    }

    let typeStructure required variants : GenerationConformance.BehaviorCase<'Input> = {
        Id = "types.generic-record-and-union"
        Consumer =
            "let box: Car.Rental.Higher.Boxed = { value = 21 }\n"
            + "let envelope: Car.Rental.Types.Envelope<int> = Car.Rental.Types.Wrapped box\n"
            + "match envelope with Car.Rental.Types.Wrapped item -> printfn \"%d\" (item.value * 2)"
        ExpectedOutput = "42"
        Required = required
        Variants = variants
    }

    let dailyRate required variants : GenerationConformance.BehaviorCase<'Input> = {
        Id = "inventory.daily-rate"
        Consumer = "printfn \"%d\" Car.Rental.Inventory.dailyRate"
        ExpectedOutput = "5000"
        Required = required
        Variants = variants
    }

    /// A C#-friendly adapter to the same case, assertions and compiler used by F# tests.
    let checkDailyRate
        version
        format
        layout
        (files: System.Collections.Generic.IEnumerable<string * string>)
        =
        let representation: GenerationConformance.Representation = {
            Version = version
            Format = format
            Layout = layout
            Optimize = true
        }

        let row: GenerationConformance.Variant<(string * string) list> = {
            Representation = representation
            Input = GenerationConformance.Supported(fun () -> Seq.toList files)
        }

        let case = dailyRate [ representation ] [ row ]
        GenerationConformance.run (fun files -> Ok files) case row

    let rentalTotal required variants : GenerationConformance.BehaviorCase<'Input> = {
        Id = "rental.total"
        Consumer =
            "match Car.Rental.Workflow.rent 3 with\n"
            + "| Ok vehicle ->\n"
            + "    match vehicle.status with\n"
            + "    | Car.Rental.Domain.Invoiced invoice -> printfn \"%d\" invoice.totalCents\n"
            + "    | _ -> failwith \"not invoiced\"\n"
            + "| Error _ -> failwith \"rental failed\""
        ExpectedOutput = "15000"
        Required = required
        Variants = variants
    }
