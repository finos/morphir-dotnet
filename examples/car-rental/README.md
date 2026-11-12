# Car rental from Morphir to F#

This example compiles classic Morphir model JSON through `Morphir.Myriad`. The model owns inventory lookup, reservation, pickup, return and billing. [Program.fs](Program.fs) supplies inputs, calls the generated functions and checks their results.

`Morphir.Myriad` is on NuGet.org from morphir-dotnet 0.4.0-alpha.2. From the repository root, with the SDK in `global.json` installed:

```sh
dotnet restore examples/car-rental/CarRental.fsproj \
  --configfile examples/car-rental/NuGet.config
dotnet run --project examples/car-rental/CarRental.fsproj --no-restore
```

Expected application output:

```text
dailyRate=5000
inventory=Available
reservation=Reserved
pickup=Rented
return=Available
invoice=15000
rejections=7
```

To select another classic version, pass `-p:MorphirModelFile=models/car-rental-v1.json` or `models/car-rental-v2.json` to `dotnet run`. All three fixtures describe the same rules. The example's NuGet configuration restores every package from NuGet.org. To use another release, pass `-p:MorphirMyriadVersion=<version>` to restore and run.

The build generates `Domain.fs`, `Inventory.fs`, `Reservation.fs`, `Pickup.fs`, `Return.fs`, `Billing.fs` and `Workflow.fs` beneath the project's `obj/` directory. Their manifest specifies compile order. The model deliberately lists modules in reverse dependency order to exercise that planning.

## Rules and boundaries

The sample holds one car, its current rental and pending billing obligations in an immutable record. Inventory lookup also accepts a list of cars. A reservation captures the car's daily rate; pickup requires a confirmed reservation; return requires an active rental and positive billable days. Billing consumes a pending completed rental and creates one invoice. Returning makes the car available for another reservation even if the previous invoice has not been issued; billing preserves that new reservation.

Three days at 5000 cents per day costs 15000 cents. Rates and amounts are integer cents, durations are explicit whole days, and each car has at most one active reservation. The sample excludes scheduling calendars, taxes, discounts, payment processing and persistence. Rejected operations return typed errors and do not mutate the supplied record.

## Fixture provenance

The original model is authored locally as classic Morphir expressions in [tools/generate-model.fsx](tools/generate-model.fsx), using the repository's versioned wire schemas. It builds JSON, not F# source. The application executes only F# transpiled from the JSON files. To update all equivalent fixtures:

```sh
dotnet fsi examples/car-rental/tools/generate-model.fsx
```

The script requires no packages beyond the .NET SDK. It is optional for running the checked-in models. It uses version-specific tags and access wrappers, including v1's object-shaped module entries.

## Documentation and checks

- [Getting started](../../docs/content/docs/getting-started/morphir-to-fsharp.md)
- [Plugin reference](../../docs/content/docs/guides/myriad-fsharp.md)
- [End-to-end tutorial](../../docs/content/docs/guides/car-rental-tutorial.md)

Run the package-consumer scenarios of the black-box tests with:

```sh
./build.sh --target Test --skip Restore Compile --test-group integration
```

The scenarios build clean consumers from locally packed packages, check all three IR versions, verify the full lifecycle and business rejections, and exercise the tutorial's rate change (`tests/Morphir.BlackBox.Tests/Integration/MyriadCarRental.feature`). The F# package checks also cover cold restore, stale output, module additions/removals, paths, cleanup and concurrent configurations (`Integration/MyriadConsumer.feature`). The `Test` target runs them.
