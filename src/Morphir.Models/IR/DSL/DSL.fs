namespace Morphir.IR.DSL

/// <summary>
/// DSL module provides a convenient entry point for all Computation Expression builders.
/// Import this module to access all DSL builders.
/// </summary>
module DSL =

    // Re-export all builders from sub-modules
    open Names
    open Morphir.IR.Classic.DSL.Types
    open Morphir.IR.Classic.DSL.Values
    open Morphir.IR.Classic.DSL.Patterns
    open Morphir.IR.Classic.DSL.Literals
    open Morphir.IR.Classic.DSL.Modules
    open Morphir.IR.Classic.DSL.Packages
    open Morphir.IR.Classic.DSL.Distributions

    /// <summary>
    /// All name-related builders.
    /// </summary>
    module Names = Names

    /// <summary>
    /// All type-related builders.
    /// </summary>
    module Types = Morphir.IR.Classic.DSL.Types

    /// <summary>
    /// All value-related builders.
    /// </summary>
    module Values = Morphir.IR.Classic.DSL.Values

    /// <summary>
    /// All pattern-related builders.
    /// </summary>
    module Patterns = Morphir.IR.Classic.DSL.Patterns

    /// <summary>
    /// All literal-related builders and helpers.
    /// </summary>
    module Literals = Morphir.IR.Classic.DSL.Literals

    /// <summary>
    /// All module-related builders.
    /// </summary>
    module Modules = Morphir.IR.Classic.DSL.Modules

    /// <summary>
    /// All package-related builders.
    /// </summary>
    module Packages = Morphir.IR.Classic.DSL.Packages

    /// <summary>
    /// All distribution-related builders.
    /// </summary>
    module Distributions = Morphir.IR.Classic.DSL.Distributions

