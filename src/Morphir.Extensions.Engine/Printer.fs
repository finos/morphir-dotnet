module Morphir.Extensions.Engine.Printer

type Printer =
    abstract Line: int
    abstract Column: int
    abstract PrintNewLine: unit -> unit
