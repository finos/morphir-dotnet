---
title: "Car rental from model to executable F#"
linkTitle: "Car rental tutorial"
weight: 40
description: "Transpile and run inventory, reservation, pickup, return and billing rules"
---

This tutorial builds a car rental workflow from classic Morphir JSON and executes the generated F#. The business rules live in the model. The handwritten F# application supplies inputs, sequences operations and displays their results.

## 1. Build the example

Complete [getting started]({{< relref "/docs/getting-started/morphir-to-fsharp" >}}), including local package creation, restore and run. Work from the repository root with the .NET SDK in `global.json` installed. No frontend compiler, database or payment service is required.

The input is `examples/car-rental/models/car-rental-v3.json`. Equivalent version 1 and 2 files are supplied. The optional `examples/car-rental/tools/generate-model.fsx` script authors the same model using classic Morphir expressions and regenerates the fixtures with `dotnet fsi`.

## 2. Follow the model modules

| Module | Business responsibility |
|--------|-------------------------|
| `Domain` | Immutable vehicle and invoice records, lifecycle unions and typed errors |
| `Inventory` | Sample car, daily rate, lookup and availability |
| `Reservation` | Reserve a car and capture its daily rate |
| `Pickup` | Start the rental from a confirmed reservation |
| `Return` | Record billable days and release the car |
| `Billing` | Produce one invoice after return |
| `Workflow` | Compose the complete rental journey |

`Inventory.car` describes `CAR-1` at 5000 cents per day. Its initial rental status is `Unreserved`. `Domain.RentalStatus` carries the captured rate through `Confirmed`, `Active` and `Completed`, then carries the invoice in `Invoiced`. A separate immutable pending-rental chain preserves completed rentals awaiting billing, and `lastInvoice` stores the most recently issued invoice.

The sample models one car's current rental at a time. Inventory lookup accepts a list, but this example does not implement a reservation calendar. Use positive whole-day durations and integer cents. Taxes, discounts, payments and persistence are outside the example.

## 3. Inspect generation and compilation

Build the example:

```sh
dotnet build examples/car-rental/CarRental.fsproj --no-restore
```

Find the generated `Domain.fs`, `Inventory.fs`, `Reservation.fs`, `Pickup.fs`, `Return.fs`, `Billing.fs` and `Workflow.fs` under the project's intermediate `morphir` directory. Open `manifest.json` to see compile order. Although the JSON lists dependent modules first, the build places their dependencies before them in F# compilation.

Each generated file preserves the `Car.Rental` package namespace and module names. `Program.fs` can call the generated functions directly. Generated files are disposable build output.

## 4. Reserve, pick up and return a car

The supplied driver follows these calls. `requireOk` extracts successful results and stops the console example if a rule unexpectedly fails.

```fsharp
open Car.Rental

let initial = Inventory.car
let found = Inventory.lookup "CAR-1" [ initial ]
let reserved = Reservation.reserve 3 initial |> requireOk
let pickedUp = Pickup.pickup reserved |> requireOk
let returned = Return.returnCar 3 pickedUp |> requireOk
let billed = Billing.bill returned |> requireOk
```

The generated functions return new records. They do not mutate `initial` or any earlier result.

| Operation | Rental status | Inventory availability |
|-----------|---------------|------------------------|
| Initial inventory | `Unreserved` | `Available` |
| Reserve | `Confirmed 5000` | `Reserved` |
| Pick up | `Active 5000` | `Rented` |
| Return after three days | `Completed (5000, 3)` | `Available` |
| Bill | `Invoiced invoice` | `Available` |

Reservation captures the rate so a later rate change does not alter this rental's price. Return validates the actual billable day count, completes the rental, preserves its billing obligation and releases the car. A new reservation can start before the previous rental is billed; billing that completed rental preserves the new reservation and the original captured rate. The driver checks this case as well. The composed generated function `Workflow.rent 3` performs the basic journey; the driver checks that both routes agree.

## 5. Read the invoice and rejected operations

`Billing.invoice billed` returns `Some invoice` with `dailyRate = 5000`, `days = 3` and `totalCents = 15000`. The amount is `5000 × 3`, expressed in cents throughout.

The driver also verifies these generated error paths:

| Attempt | Typed business error |
|---------|----------------------|
| Reserve the reserved car | `Unavailable` |
| Pick up the initial car | `ReservationRequired` |
| Return the initial car | `ActiveRentalRequired` |
| Reserve for zero days | `InvalidDuration` |
| Return for zero billable days | `InvalidDuration` |
| Bill before return | `ReturnRequired` |
| Bill an already billed rental | `AlreadyBilled` |

Negative durations follow the same invalid-duration rule. Each rejected operation leaves its input state unchanged. These are business results returned by generated F#, so the application can handle them without parsing build diagnostics.

## 6. Change the model and regenerate

In `models/car-rental-v3.json`, locate the `inventory` module's `dailyRate` value definition. Its body is a literal containing `5000`. Change that literal to `6000`. This is the single sample rate definition; references to it stay intact.

Run a fresh journey:

```sh
dotnet run --project examples/car-rental/CarRental.fsproj --no-restore
```

The output now includes `dailyRate=6000` and `invoice=18000`. The build regenerated `Inventory.fs`; the new reservation captured 6000 cents per day and the generated billing function charged three days. No generated source or handwritten business rule changed.

Restore the sample literal to 5000 afterwards. To make a permanent authoring change across every fixture, edit the `integer 5000` daily-rate expression in `tools/generate-model.fsx` and run:

```sh
dotnet fsi examples/car-rental/tools/generate-model.fsx
```

The BDD suite repeats this rate-change exercise in a disposable copy and verifies the invoice total, preventing the tutorial from drifting away from the implementation.

## 7. Try the other classic versions

```sh
dotnet run --project examples/car-rental/CarRental.fsproj --no-restore \
  -p:MorphirModelFile=models/car-rental-v1.json
dotnet run --project examples/car-rental/CarRental.fsproj --no-restore \
  -p:MorphirModelFile=models/car-rental-v2.json
```

Both runs produce the original 15000-cent invoice when their checked-in rates remain 5000. The fixture schemas differ, including v1 access wrappers and version-specific tags; the backend decodes each version before generating equivalent F#.

## Troubleshooting

An `Error Domain.InvalidDuration` is a business rejection. Supply a positive day count. An `unsupported-version` build error means the JSON format is outside versions 1, 2 and 3; use a supported classic file. A missing-model or decoding diagnostic names the input path and available location; correct the configured path or model structure and rebuild.

Generation failures block compilation even if old generated files exist. Check the [plugin reference]({{< relref "/docs/guides/myriad-fsharp" >}}) for host, dependency, output ownership and module-cycle diagnostics. IR 4+, document trees and a Morphir CLI generation command are separate initiatives.
