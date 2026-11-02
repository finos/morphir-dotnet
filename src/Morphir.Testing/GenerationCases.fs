namespace Morphir.Testing

/// Behavioral expectations are independent of the representation adapters that supply inputs.
module GenerationCases =
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
        }

        let row : GenerationConformance.Variant<(string * string) list> = {
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
