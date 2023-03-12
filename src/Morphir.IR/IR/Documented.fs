module Morphir.IR.Documented

/// <summary>
/// Type that represents a value that is documented value.
/// </summary>
type Documented<'a> = { Doc : string; Value : 'a}

/// <summary>
/// Maps a function over the value of a documented value.
/// </summary>
let map (f: 'a -> 'b) (d : Documented<'a>) = { Doc = d.Doc; Value = f d.Value }
