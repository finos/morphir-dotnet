namespace Morphir.Bogus.IR

open System
open Bogus
open Morphir.Bogus
open Morphir.Bogus.LibDataLoader

type LibDataSet(randomizer: Randomizer) as self =
    inherit DataSet()

    let typeIdentities =
        lazy
            LibDataLoader.TypeIdentities
            |> Seq.toArray

    do
        match randomizer with
        | null -> ()
        | _ -> self.Random <- randomizer

    //type internal Loader = ProgrammingLibDataLoader

    new() = LibDataSet(null)

    member this.ModuleNames(?num: int) =
        let num = defaultArg num 1
        Guard.AgainstNegative(num, nameof (num))

        seq {
            for i in 1..num do
                yield this.ModuleName()
        }

    member this.ModuleName() =
        let index =
            this.Random.Number(
                ProgrammingLibData.Count
                - 1
            )

        ProgrammingLibData[index].Module
        |> Option.defaultValue (this.Random.Word())

    member this.Namespaces(?num: int) =
        let num = defaultArg num 1
        Guard.AgainstNegative(num, nameof (num))

        seq {
            for i in 1..num do
                yield this.Namespace()
        }

    member this.Namespace() = this.TypeIdentity().Namespace

    member this.TypeIdentities(?num: int) =
        let num = defaultArg num 1
        Guard.AgainstNegative(num, nameof (num))

        seq {
            for i in 1..num do
                yield this.TypeIdentity()
        }

    member this.TypeIdentity() =
        let index =
            this.Random.Number(
                typeIdentities.Value.Length
                - 1
            )

        typeIdentities.Value[index]
