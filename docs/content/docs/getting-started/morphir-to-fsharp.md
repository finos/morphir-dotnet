---
title: "Generate F# from Morphir"
linkTitle: "Morphir to F#"
weight: 30
description: "Build and run a car rental model using the Myriad plugin"
---

Compile a Morphir model JSON file into F# as part of an ordinary build. This article uses the supplied car rental example and calls its generated inventory and billing functions.

## Prerequisites

- A checkout of `finos/morphir-dotnet`.
- The .NET SDK pinned in `global.json`, currently 10.0.403.
- Access to NuGet for package restore.

Run the following commands from the repository root. The checked-in model is sufficient; a separate Morphir frontend is not required.

## Build a local plugin package

```sh
dotnet pack src/Morphir.Myriad/Morphir.Myriad.fsproj -c Release \
  -o artifacts/myriad-feed -p:PackageVersion=0.1.0-local.5
```

`Morphir.Myriad` includes its backend, build task and the matched Myriad 0.85.0 host. Myriad.Core 0.85.0 and Fantomas.Core 7.0.5 are the matching plugin dependencies. The bundled host uses an aligned FSharp.Core and major-version roll forward to load the .NET 10 plugin, including on the first cold restore.

## Configure the model input

The example's `examples/car-rental/CarRental.fsproj` references the local plugin version and declares the JSON input once:

```xml
<ItemGroup>
  <PackageReference Include="Morphir.Myriad" Version="0.1.0-local.5" PrivateAssets="all" />
  <MorphirModel Include="models/car-rental-v3.json" />
  <Compile Include="Program.fs" />
</ItemGroup>
```

This snippet uses ordinary NuGet version syntax for a standalone project. In this repository the example uses `VersionOverride` with `MorphirMyriadVersion` to work with central package management. Do not add JSON to `Compile` or enumerate generated modules by hand.

## Restore and run

```sh
dotnet restore examples/car-rental/CarRental.fsproj \
  --configfile examples/car-rental/NuGet.config
dotnet run --project examples/car-rental/CarRental.fsproj --no-restore
```

The supplied NuGet configuration maps `Morphir.Myriad` to the local feed, avoiding the repository's broader source mapping. The application prints:

```text
dailyRate=5000
inventory=Available
reservation=Reserved
pickup=Rented
return=Available
invoice=15000
rejections=7
```

The first generated value is `Car.Rental.Inventory.dailyRate`. The driver also calls `Inventory.lookup "CAR-1" [ Inventory.car ]`, then executes the reservation, pickup, return and billing functions. Three billable days at 5000 cents per day produce a 15000-cent invoice. The final line confirms that seven invalid operations returned the expected business errors.

## Inspect the F# output

Look beneath `examples/car-rental/obj/<configuration>/net10.0/morphir/`. The model has seven generated files: `Domain.fs`, `Inventory.fs`, `Reservation.fs`, `Pickup.fs`, `Return.fs`, `Billing.fs` and `Workflow.fs`. The accompanying `manifest.json` lists them in dependency order. Myriad inserts them before `Program.fs` in compilation.

Edit the model JSON or its optional F# authoring script, then rebuild. Generated files are build artifacts and are regenerated; do not edit them directly. When rebuilding the plugin package, use a fresh version and pass `-p:MorphirMyriadVersion=<version>` to restore/run so NuGet does not reuse an older package.

## Next steps

Follow the [car rental tutorial]({{< relref "/docs/guides/car-rental-tutorial" >}}) for the full model, state transitions, error cases and a rate-change exercise. Consult the [plugin reference]({{< relref "/docs/guides/myriad-fsharp" >}}) for configuration, compatibility and build failures.
