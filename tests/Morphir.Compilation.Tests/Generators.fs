module Morphir.Compilation.Tests.Generators

open FsCheck
open Morphir.Compilation
open Morphir.IR

let private code = DiagnosticCode.Create "test.code"

/// A diagnostic with a random severity and a distinct message, so order is observable.
let diagnostic: Gen<Diagnostic> = gen {
    let! severity =
        Gen.elements [
            DiagnosticSeverity.Error
            DiagnosticSeverity.Warning
            DiagnosticSeverity.Info
            DiagnosticSeverity.Hint
        ]

    let! id = Gen.choose (0, 1_000_000)
    return Diagnostic.make severity code $"diagnostic {id}"
}

/// A diagnostic that is never an error.
let nonError: Gen<Diagnostic> = gen {
    let! severity =
        Gen.elements [
            DiagnosticSeverity.Warning
            DiagnosticSeverity.Info
            DiagnosticSeverity.Hint
        ]

    let! id = Gen.choose (0, 1_000_000)
    return Diagnostic.make severity code $"note {id}"
}

/// An error diagnostic.
let error: Gen<Diagnostic> = gen {
    let! id = Gen.choose (0, 1_000_000)
    return Diagnostic.error code $"error {id}"
}

/// A checked integer result, passed or failed, with diagnostics.
let checkedInt: Gen<Checked<int>> = gen {
    let! value = Arb.generate<int>
    let! diagnostics = Gen.listOf diagnostic
    return Checked.ofDiagnostics value diagnostics
}

let private identifier = gen {
    let! length = Gen.choose (1, 6)

    let! chars =
        Gen.listOfLength
            length
            (Gen.elements (
                [ 'a' .. 'z' ]
                @ [ '0' .. '9' ]
            ))

    return System.String(Array.ofList chars)
}

/// A semantic version, with optional pre-release and build parts.
let semanticVersion: Gen<Versioning.SemanticVersion> = gen {
    let! major = Gen.choose (0, 9)
    let! minor = Gen.choose (0, 3)
    let! patch = Gen.choose (0, 3)
    let! preRelease = Gen.optionOf identifier
    let! build = Gen.optionOf identifier

    return {
        Major = major
        Minor = minor
        Patch = patch
        PreRelease = preRelease
        BuildMetadata = build
    }
}
