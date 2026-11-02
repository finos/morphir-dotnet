namespace Morphir.Testing

/// Behavioral expectations are independent of the representation adapters that supply inputs.
module GenerationCases =
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
