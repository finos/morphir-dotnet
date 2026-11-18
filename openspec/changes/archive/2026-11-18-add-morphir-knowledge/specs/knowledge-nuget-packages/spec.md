# Spec Delta

## Purpose

Make the .NET knowledge contracts, OKF tooling and RDF tooling independently installable from NuGet.org for Morphir and downstream applications.

## ADDED Requirements

### Requirement: NuGet packages follow the existing library release

Approved releases containing this change SHALL publish `Morphir.Knowledge`, `Morphir.Knowledge.Okf` and `Morphir.Knowledge.Rdf` to NuGet.org through the repository's library release path. Each SHALL use the release's version and ship library assets for net10.0 and net11.0.

#### Scenario: Packaged release inventory
- **WHEN** the repository packs the libraries for a release containing this change
- **THEN** the inventory contains a package under each of the three IDs with both target frameworks and matching versions

#### Scenario: First approved publication
- **WHEN** the first approved release containing this change publishes its libraries
- **THEN** all three package IDs and the release version resolve through NuGet.org

### Requirement: Package references preserve independent use

Installing the shared contracts SHALL require neither the OKF parsing libraries nor an RDF engine. Installing the OKF package SHALL require no RDF package. Installing the RDF package SHALL require no OKF package or RDF engine. Both format packages SHALL use the same shared contract type identities.

#### Scenario: Minimal package consumers
- **WHEN** fresh projects restore the contracts alone, OKF alone and RDF alone
- **THEN** each gets the dependencies needed for its selected features and the shared APIs use the same type identities

### Requirement: Packaged APIs serve F# and C# consumers

The packages SHALL expose supported F# and C# entry points for constructing a versioned selection, parsing and validating a knowledge bundle, and reading and writing N-Quads. Package verification SHALL restore packages from a feed and execute consumer examples on both library target frameworks.

#### Scenario: Exercise the published surface
- **WHEN** F# and C# consumers install locally packed packages into fresh projects
- **THEN** both can construct a selection, validate a fixture bundle and round-trip an RDF dataset through N-Quads
