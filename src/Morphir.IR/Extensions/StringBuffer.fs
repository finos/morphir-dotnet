namespace Morphir

open System
open System.Text
open System.Collections.Generic
open System.Linq

[<AutoOpenAttribute>]
module StringBuffer =
    type StringBuffer = StringBuilder -> unit

    type StringBufferBuilder() =
        member inline __.Yield(txt: string) =
            fun (b: StringBuilder) ->
                b.Append txt
                |> ignore

        member inline __.Yield(c: char) =
            fun (b: StringBuilder) ->
                b.Append c
                |> ignore

        member inline __.Yield(strings: #seq<string>) =
            fun (b: StringBuilder) ->
                for s in strings do
                    s
                    |> b.AppendLine
                    |> ignore

        member inline __.YieldFrom(f: StringBuffer) = f

        member __.Combine(f, g) =
            fun (b: StringBuilder) ->
                f b
                g b

        member __.Delay f = fun (b: StringBuilder) -> (f ()) b
        member __.Zero() = ignore

        member __.For(xs: 'a seq, f: 'a -> StringBuffer) =
            fun (b: StringBuilder) ->
                let e = xs.GetEnumerator()

                while e.MoveNext() do
                    (f e.Current) b

        member __.While(p: unit -> bool, f: StringBuffer) =
            fun (b: StringBuilder) ->
                while p () do
                    f b

        member __.Run(f: StringBuffer) =
            let b = StringBuilder()
            do f b
            b.ToString()

    let stringBuffer = new StringBufferBuilder()
