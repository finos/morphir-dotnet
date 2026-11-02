namespace Morphir.IR.V4

open Morphir.IR

/// <summary>The semantic category of an entry point.</summary>
type EntryPointKind =
    /// <summary>The primary entry point.</summary>
    | Main
    /// <summary>A CLI subcommand.</summary>
    | Command
    /// <summary>A service endpoint or message handler.</summary>
    | Handler
    /// <summary>A batch or scheduled job.</summary>
    | Job
    /// <summary>A business policy or rule.</summary>
    | Policy

/// <summary>A way into an application.</summary>
type EntryPoint = {
    /// <summary>The value to invoke.</summary>
    Target: FQName
    /// <summary>The semantic category.</summary>
    Kind: EntryPointKind
    /// <summary>The documentation.</summary>
    Doc: string option
}

/// <summary>A v4 distribution. Dependencies keep document order, which does not count in equality.</summary>
type Distribution =
    /// <summary>A reusable package with its implementations.</summary>
    | Library of
        packageName: PackageName *
        dependencies: OrderedMap<PackageName, PackageSpecification> *
        definition: PackageDefinition
    /// <summary>A package's public interface and nothing else.</summary>
    | Specs of
        packageName: PackageName *
        dependencies: OrderedMap<PackageName, PackageSpecification> *
        specification: PackageSpecification
    /// <summary>
    /// An executable package with statically linked dependencies. Entry point names are arbitrary text, and
    /// their order counts in equality.
    /// </summary>
    | Application of
        packageName: PackageName *
        dependencies: OrderedMap<PackageName, PackageDefinition> *
        definition: PackageDefinition *
        entryPoints: (string * EntryPoint) list
