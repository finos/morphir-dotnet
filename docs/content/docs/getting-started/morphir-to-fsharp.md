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

## Choose the plugin version

`Morphir.Myriad` is on NuGet.org, with a version for each morphir-dotnet release. This article uses 0.4.0-alpha.2; use the version of the release you want. The package brings everything the build needs; the [plugin reference]({{< relref "/docs/guides/myriad-fsharp" >}}) lists the versions it works with.

## Configure the model input

The example's `examples/car-rental/CarRental.fsproj` references the plugin and declares the JSON input once:

```xml
<ItemGroup>
  <PackageReference Include="Morphir.Myriad" Version="0.4.0-alpha.2" PrivateAssets="all" />
  <MorphirModel Include="models/car-rental-v3.json" />
  <Compile Include="Program.fs" />
</ItemGroup>
```

In your own project, write the version as above. In this repository the example takes it from the property `MorphirMyriadVersion`, which is why the commands below pass `-p:MorphirMyriadVersion`. Don't add the JSON file to `Compile`, and don't list the generated modules by hand.

## Restore and run

```sh
dotnet restore examples/car-rental/CarRental.fsproj \
  --configfile examples/car-rental/NuGet.config -p:MorphirMyriadVersion=0.4.0-alpha.2
dotnet run --project examples/car-rental/CarRental.fsproj --no-restore -p:MorphirMyriadVersion=0.4.0-alpha.2
```

The supplied NuGet configuration restores every package from NuGet.org, and keeps the repository's own source mapping out of the example. The application prints:

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

Edit the model JSON or its optional F# authoring script, then rebuild. Generated files are build artifacts and are regenerated; do not edit them directly.

## Next steps

Follow the [car rental tutorial]({{< relref "/docs/guides/car-rental-tutorial" >}}) for the full model, state transitions, error cases and a rate-change exercise. Consult the [plugin reference]({{< relref "/docs/guides/myriad-fsharp" >}}) for configuration, compatibility and build failures.
