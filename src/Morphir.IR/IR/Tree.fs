module Morphir.IR.Tree

open Morphir.IR.Name
open Morphir.IR.Type

type TypeDef<'a> =
    | TypeAliasDef of name:Name * typeParameters: Name list * rhs:Type<'a>
