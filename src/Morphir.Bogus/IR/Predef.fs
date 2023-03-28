namespace Morphir.Bogus.IR

open Bogus

type ProgrammingLibDataSet(randomizer: Randomizer) as self =
    inherit DataSet()


    do
        match randomizer with
        | null -> ()
        | _ -> self.Random <- randomizer

    new() = ProgrammingLibDataSet(null)

    member this.TypeIdentity =
        randomizer.
