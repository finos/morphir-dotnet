module Morphir.IR.Distribution
open Morphir.IR.Package

type Distribution =
    | Library of packageName:PackageName
