// Generated test adapter from independent frozen inputs/expectations; see author-conformance-tests.py.
module Morphir.SDK.Tests.ConformanceCases

open System
open Expecto
open Morphir.SDK
open Morphir.SDK.Tests.ConformanceAssertions

let cases = [
    "Basics.add/int",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 7
        let a1: int = 3
        let invoke () : int = Basics.add a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.add/int" (Choice1Of2(10)) invoke preserve callbacks invariants
    )
    "Basics.add/float",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "1.5"
        let a1: float = floatInput "0.5"
        let invoke () : float = Basics.add a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()
        check "Basics.add/float" (Choice1Of2(floatInput "2")) invoke preserve callbacks invariants
    )
    "Basics.add/int-overflow",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 2147483647
        let a1: int = 1
        let invoke () : int = Basics.add a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.add/int-overflow"
            (Choice2Of2 "Overflow")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.add/float-nan",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "NaN"
        let a1: float = floatInput "1"
        let invoke () : float = Basics.add a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.add/float-nan"
            (Choice1Of2(floatInput "NaN"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.subtract/int",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 7
        let a1: int = 3
        let invoke () : int = Basics.subtract a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.subtract/int" (Choice1Of2(4)) invoke preserve callbacks invariants
    )
    "Basics.subtract/float",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "1.5"
        let a1: float = floatInput "0.5"
        let invoke () : float = Basics.subtract a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.subtract/float"
            (Choice1Of2(floatInput "1"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.subtract/int-overflow",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = -2147483648
        let a1: int = 1
        let invoke () : int = Basics.subtract a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.subtract/int-overflow"
            (Choice2Of2 "Overflow")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.subtract/float-nan",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "NaN"
        let a1: float = floatInput "1"
        let invoke () : float = Basics.subtract a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.subtract/float-nan"
            (Choice1Of2(floatInput "NaN"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.multiply/int",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 7
        let a1: int = 3
        let invoke () : int = Basics.multiply a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.multiply/int" (Choice1Of2(21)) invoke preserve callbacks invariants
    )
    "Basics.multiply/float",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "1.5"
        let a1: float = floatInput "0.5"
        let invoke () : float = Basics.multiply a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.multiply/float"
            (Choice1Of2(floatInput "0.75"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.multiply/int-overflow",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 2147483647
        let a1: int = 2
        let invoke () : int = Basics.multiply a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.multiply/int-overflow"
            (Choice2Of2 "Overflow")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.multiply/float-nan",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "NaN"
        let a1: float = floatInput "1"
        let invoke () : float = Basics.multiply a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.multiply/float-nan"
            (Choice1Of2(floatInput "NaN"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.divide/normal",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "7"
        let a1: float = floatInput "2"
        let invoke () : float = Basics.divide a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.divide/normal"
            (Choice1Of2(floatInput "3.5"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.divide/positive-zero",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "1"
        let a1: float = floatInput "+0"
        let invoke () : float = Basics.divide a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.divide/positive-zero"
            (Choice1Of2(floatInput "+Infinity"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.divide/negative-zero",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "1"
        let a1: float = floatInput "-0"
        let invoke () : float = Basics.divide a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.divide/negative-zero"
            (Choice1Of2(floatInput "-Infinity"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.divide/zero-zero",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "+0"
        let a1: float = floatInput "+0"
        let invoke () : float = Basics.divide a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.divide/zero-zero"
            (Choice1Of2(floatInput "NaN"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.divide/negative-result-zero",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "-0"
        let a1: float = floatInput "2"
        let invoke () : float = Basics.divide a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.divide/negative-result-zero"
            (Choice1Of2(floatInput "-0"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.divide/infinite",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "+Infinity"
        let a1: float = floatInput "2"
        let invoke () : float = Basics.divide a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.divide/infinite"
            (Choice1Of2(floatInput "+Infinity"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.integerDivide/positive",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 7
        let a1: int = 3
        let invoke () : int = Basics.divide a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.integerDivide/positive" (Choice1Of2(2)) invoke preserve callbacks invariants
    )
    "Basics.integerDivide/negative",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = -7
        let a1: int = 3
        let invoke () : int = Basics.divide a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.integerDivide/negative" (Choice1Of2(-2)) invoke preserve callbacks invariants
    )
    "Basics.integerDivide/both-negative",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = -7
        let a1: int = -3
        let invoke () : int = Basics.divide a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.integerDivide/both-negative"
            (Choice1Of2(2))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.integerDivide/zero",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 7
        let a1: int = 0
        let invoke () : int = Basics.divide a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.integerDivide/zero"
            (Choice2Of2 "DivisionByZero")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.integerDivide/min-neg-one",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = -2147483648
        let a1: int = -1
        let invoke () : int = Basics.divide a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.integerDivide/min-neg-one"
            (Choice2Of2 "Overflow")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.modBy/negative-dividend",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 3
        let a1: int = -7
        let invoke () : int = Basics.modBy a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.modBy/negative-dividend" (Choice1Of2(2)) invoke preserve callbacks invariants
    )
    "Basics.modBy/negative-divisor",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = -3
        let a1: int = 7
        let invoke () : int = Basics.modBy a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.modBy/negative-divisor" (Choice1Of2(-2)) invoke preserve callbacks invariants
    )
    "Basics.modBy/min-neg-one",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = -1
        let a1: int = -2147483648
        let invoke () : int = Basics.modBy a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.modBy/min-neg-one" (Choice1Of2(0)) invoke preserve callbacks invariants
    )
    "Basics.modBy/zero",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 0
        let a1: int = 7
        let invoke () : int = Basics.modBy a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.modBy/zero"
            (Choice2Of2 "DivisionByZero")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.remainderBy/negative-dividend",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 3
        let a1: int = -7
        let invoke () : int = Basics.remainderBy a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.remainderBy/negative-dividend"
            (Choice1Of2(-1))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.remainderBy/negative-divisor",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = -3
        let a1: int = 7
        let invoke () : int = Basics.remainderBy a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.remainderBy/negative-divisor"
            (Choice1Of2(1))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.remainderBy/min-neg-one",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = -1
        let a1: int = -2147483648
        let invoke () : int = Basics.remainderBy a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.remainderBy/min-neg-one" (Choice1Of2(0)) invoke preserve callbacks invariants
    )
    "Basics.remainderBy/zero",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 0
        let a1: int = 7
        let invoke () : int = Basics.remainderBy a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.remainderBy/zero"
            (Choice2Of2 "DivisionByZero")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.negate/int",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = -3
        let invoke () : int = Basics.negate a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.negate/int" (Choice1Of2(3)) invoke preserve callbacks invariants
    )
    "Basics.negate/int-min",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = -2147483648
        let invoke () : int = Basics.negate a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.negate/int-min" (Choice2Of2 "Overflow") invoke preserve callbacks invariants
    )
    "Basics.negate/signed-zero",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "-0"
        let invoke () : float = Basics.negate a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.negate/signed-zero"
            (Choice1Of2(floatInput "+0"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.abs/int",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = -3
        let invoke () : int = Basics.abs a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.abs/int" (Choice1Of2(3)) invoke preserve callbacks invariants
    )
    "Basics.abs/int-min",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = -2147483648
        let invoke () : int = Basics.abs a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.abs/int-min" (Choice2Of2 "Overflow") invoke preserve callbacks invariants
    )
    "Basics.abs/signed-zero",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "-0"
        let invoke () : float = Basics.abs a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.abs/signed-zero"
            (Choice1Of2(floatInput "+0"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.power/int",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 2
        let a1: int = 10
        let invoke () : int = Basics.pow a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.power/int" (Choice1Of2(1024)) invoke preserve callbacks invariants
    )
    "Basics.power/int-overflow",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 2
        let a1: int = 31
        let invoke () : int = Basics.pow a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.power/int-overflow"
            (Choice2Of2 "Overflow")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.power/float",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "-1"
        let a1: float = floatInput "0.5"

        let invoke () : float =
            (fun left right ->
                left
                ** right
            )
                a0
                a1

        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.power/float"
            (Choice1Of2(floatInput "NaN"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.toFloat/max",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 2147483647
        let invoke () : float = Int.toFloat a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.toFloat/max"
            (Choice1Of2(floatInput "2147483647"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.toFloat/min",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = -2147483648
        let invoke () : float = Int.toFloat a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.toFloat/min"
            (Choice1Of2(floatInput "-2147483648"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.round/negative-half",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "-2.5"
        let invoke () : int = Basics.round a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.round/negative-half" (Choice1Of2(-2)) invoke preserve callbacks invariants
    )
    "Basics.round/nonfinite",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "NaN"
        let invoke () : int = Basics.round a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.round/nonfinite"
            (Choice2Of2 "NonFiniteConversion")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.round/out-of-range",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "2147483648"
        let invoke () : int = Basics.round a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.round/out-of-range"
            (Choice2Of2 "Overflow")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.floor/negative-half",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "-2.5"
        let invoke () : int = Basics.floor a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.floor/negative-half" (Choice1Of2(-3)) invoke preserve callbacks invariants
    )
    "Basics.floor/nonfinite",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "NaN"
        let invoke () : int = Basics.floor a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.floor/nonfinite"
            (Choice2Of2 "NonFiniteConversion")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.floor/out-of-range",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "2147483648"
        let invoke () : int = Basics.floor a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.floor/out-of-range"
            (Choice2Of2 "Overflow")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.ceiling/negative-half",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "-2.5"
        let invoke () : int = Basics.ceiling a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.ceiling/negative-half" (Choice1Of2(-2)) invoke preserve callbacks invariants
    )
    "Basics.ceiling/nonfinite",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "NaN"
        let invoke () : int = Basics.ceiling a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.ceiling/nonfinite"
            (Choice2Of2 "NonFiniteConversion")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.ceiling/out-of-range",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "2147483648"
        let invoke () : int = Basics.ceiling a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.ceiling/out-of-range"
            (Choice2Of2 "Overflow")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.truncate/negative-half",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "-2.5"
        let invoke () : int = Basics.truncate a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.truncate/negative-half" (Choice1Of2(-2)) invoke preserve callbacks invariants
    )
    "Basics.truncate/nonfinite",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "NaN"
        let invoke () : int = Basics.truncate a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.truncate/nonfinite"
            (Choice2Of2 "NonFiniteConversion")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.truncate/out-of-range",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "2147483648"
        let invoke () : int = Basics.truncate a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.truncate/out-of-range"
            (Choice2Of2 "Overflow")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.isNaN/nan",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "NaN"
        let invoke () : bool = System.Double.IsNaN a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.isNaN/nan" (Choice1Of2(true)) invoke preserve callbacks invariants
    )
    "Basics.isNaN/infinity",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "+Infinity"
        let invoke () : bool = System.Double.IsNaN a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.isNaN/infinity" (Choice1Of2(false)) invoke preserve callbacks invariants
    )
    "Basics.isInfinite/nan",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "NaN"
        let invoke () : bool = System.Double.IsInfinity a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.isInfinite/nan" (Choice1Of2(false)) invoke preserve callbacks invariants
    )
    "Basics.isInfinite/infinity",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "+Infinity"
        let invoke () : bool = System.Double.IsInfinity a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.isInfinite/infinity" (Choice1Of2(true)) invoke preserve callbacks invariants
    )
    "Basics.compare/int-lt",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: int = 2
        let invoke () : Morphir.SDK.Order = Basics.compare a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Morphir.SDK.Order) = ()

        check
            "Basics.compare/int-lt"
            (Choice1Of2(Morphir.SDK.LT))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.compare/int-eq",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 2
        let a1: int = 2
        let invoke () : Morphir.SDK.Order = Basics.compare a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Morphir.SDK.Order) = ()

        check
            "Basics.compare/int-eq"
            (Choice1Of2(Morphir.SDK.EQ))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.compare/int-gt",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 3
        let a1: int = 2
        let invoke () : Morphir.SDK.Order = Basics.compare a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Morphir.SDK.Order) = ()

        check
            "Basics.compare/int-gt"
            (Choice1Of2(Morphir.SDK.GT))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.compare/float-zero",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "-0"
        let a1: float = floatInput "+0"
        let invoke () : Morphir.SDK.Order = Basics.compare a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Morphir.SDK.Order) = ()

        check
            "Basics.compare/float-zero"
            (Choice1Of2(Morphir.SDK.EQ))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.compare/decimal-text-order",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "10"
        let a1: string = "2"
        let invoke () : Morphir.SDK.Order = Basics.compare a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Morphir.SDK.Order) = ()

        check
            "Basics.compare/decimal-text-order"
            (Choice1Of2(Morphir.SDK.LT))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.compare/utf16",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "\ud800\udc00"
        let a1: string = "\ue000"
        let invoke () : Morphir.SDK.Order = Basics.compare a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Morphir.SDK.Order) = ()

        check
            "Basics.compare/utf16"
            (Choice1Of2(Morphir.SDK.LT))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.compare/no-normalization",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "e\u0301"
        let a1: string = "\u00e9"
        let invoke () : Morphir.SDK.Order = Basics.compare a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Morphir.SDK.Order) = ()

        check
            "Basics.compare/no-normalization"
            (Choice1Of2(Morphir.SDK.LT))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.compare/char",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: char = char 65
        let a1: char = char 97
        let invoke () : Morphir.SDK.Order = Basics.compare a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Morphir.SDK.Order) = ()

        check
            "Basics.compare/char"
            (Choice1Of2(Morphir.SDK.LT))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.compare/list-prefix",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<int> = [ (1) ]
        let a1: list<int> = [ (1); (0) ]
        let invoke () : Morphir.SDK.Order = Basics.compare a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Morphir.SDK.Order) = ()

        check
            "Basics.compare/list-prefix"
            (Choice1Of2(Morphir.SDK.LT))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.compare/tuple",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int * string) = (1, "b")
        let a1: (int * string) = (1, "a")
        let invoke () : Morphir.SDK.Order = Basics.compare a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Morphir.SDK.Order) = ()

        check
            "Basics.compare/tuple"
            (Choice1Of2(Morphir.SDK.GT))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.lessThan/int",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: int = 2
        let invoke () : bool = Basics.lessThan a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.lessThan/int" (Choice1Of2(true)) invoke preserve callbacks invariants
    )
    "Basics.lessThanOrEqual/int",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: int = 2
        let invoke () : bool = Basics.lessThanOrEqual a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.lessThanOrEqual/int" (Choice1Of2(true)) invoke preserve callbacks invariants
    )
    "Basics.greaterThan/int",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: int = 2
        let invoke () : bool = Basics.greaterThan a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.greaterThan/int" (Choice1Of2(false)) invoke preserve callbacks invariants
    )
    "Basics.greaterThanOrEqual/int",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: int = 2
        let invoke () : bool = Basics.greaterThanOrEqual a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.greaterThanOrEqual/int"
            (Choice1Of2(false))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.min/int",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: int = 2
        let invoke () : int = Basics.min a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.min/int" (Choice1Of2(1)) invoke preserve callbacks invariants
    )
    "Basics.max/int",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: int = 2
        let invoke () : int = Basics.max a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.max/int" (Choice1Of2(2)) invoke preserve callbacks invariants
    )
    "Basics.clamp/below",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: int = 5
        let a2: int = 0
        let invoke () : int = Basics.clamp a0 a1 a2
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.clamp/below" (Choice1Of2(1)) invoke preserve callbacks invariants
    )
    "Basics.clamp/above",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: int = 5
        let a2: int = 6
        let invoke () : int = Basics.clamp a0 a1 a2
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.clamp/above" (Choice1Of2(5)) invoke preserve callbacks invariants
    )
    "Basics.clamp/inside-float",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "1"
        let a1: float = floatInput "5"
        let a2: float = floatInput "3"
        let invoke () : float = Basics.clamp a0 a1 a2
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.clamp/inside-float"
            (Choice1Of2(floatInput "3"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.LT/constructor",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let invoke () : Morphir.SDK.Order = Morphir.SDK.LT
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Morphir.SDK.Order) = ()

        check
            "Basics.LT/constructor"
            (Choice1Of2(Morphir.SDK.LT))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.EQ/constructor",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let invoke () : Morphir.SDK.Order = Morphir.SDK.EQ
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Morphir.SDK.Order) = ()

        check
            "Basics.EQ/constructor"
            (Choice1Of2(Morphir.SDK.EQ))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.GT/constructor",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let invoke () : Morphir.SDK.Order = Morphir.SDK.GT
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Morphir.SDK.Order) = ()

        check
            "Basics.GT/constructor"
            (Choice1Of2(Morphir.SDK.GT))
            invoke
            preserve
            callbacks
            invariants
    )
    "List.singleton/nested",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: option<list<int>> = Some([ (1); (2) ])
        let invoke () : list<option<list<int>>> = List.singleton a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: list<option<list<int>>>) = ()

        check
            "List.singleton/nested"
            (Choice1Of2([ (Some([ (1); (2) ])) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "List.cons/prepend",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: list<int> = [ (2); (3) ]
        let before1 = snapshot (box a1)

        let invoke () : list<int> =
            (fun head tail ->
                head
                :: tail
            )
                a0
                a1

        let preserve () =
            Expect.equal (snapshot (box a1)) before1 "operand 1 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: list<int>) = ()

        check
            "List.cons/prepend"
            (Choice1Of2([ (1); (2); (3) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "List.map/normal",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int -> string) =
            callback (fun (value: int) ->
                value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            )

        let a1: list<int> = [ (1); (2) ]
        let before1 = snapshot (box a1)
        let invoke () : list<string> = List.map a0 a1

        let preserve () =
            Expect.equal (snapshot (box a1)) before1 "operand 1 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 2 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: list<string>) = ()
        check "List.map/normal" (Choice1Of2([ ("1"); ("2") ])) invoke preserve callbacks invariants
    )
    "List.map/empty",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int -> string) =
            callback (fun (value: int) ->
                value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            )

        let a1: list<int> = []
        let invoke () : list<string> = List.map a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: list<string>) = ()
        check "List.map/empty" (Choice1Of2([])) invoke preserve callbacks invariants
    )
    "List.append/normal",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<int> = [ (1) ]
        let a1: list<int> = [ (2) ]
        let before0 = snapshot (box a0)
        let before1 = snapshot (box a1)
        let invoke () : list<int> = List.append a0 a1

        let preserve () =
            Expect.equal (snapshot (box a0)) before0 "operand 0 unchanged"
            Expect.equal (snapshot (box a1)) before1 "operand 1 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: list<int>) = ()
        check "List.append/normal" (Choice1Of2([ (1); (2) ])) invoke preserve callbacks invariants
    )
    "List.append/empty",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<int> = []
        let a1: list<int> = []
        let invoke () : list<int> = List.append a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: list<int>) = ()
        check "List.append/empty" (Choice1Of2([])) invoke preserve callbacks invariants
    )
    "List.head/normal",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<int> = [ (1); (2) ]
        let invoke () : option<int> = List.head a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<int>) = ()
        check "List.head/normal" (Choice1Of2(Some(1))) invoke preserve callbacks invariants
    )
    "List.head/empty",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<int> = []
        let invoke () : option<int> = List.head a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<int>) = ()
        check "List.head/empty" (Choice1Of2(None)) invoke preserve callbacks invariants
    )
    "List.tail/normal",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<int> = [ (1); (2) ]
        let invoke () : option<list<int>> = List.tail a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<list<int>>) = ()
        check "List.tail/normal" (Choice1Of2(Some([ (2) ]))) invoke preserve callbacks invariants
    )
    "List.tail/empty",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<int> = []
        let invoke () : option<list<int>> = List.tail a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<list<int>>) = ()
        check "List.tail/empty" (Choice1Of2(None)) invoke preserve callbacks invariants
    )
    "List.tail/singleton",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<int> = [ (1) ]
        let invoke () : option<list<int>> = List.tail a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<list<int>>) = ()
        check "List.tail/singleton" (Choice1Of2(Some([]))) invoke preserve callbacks invariants
    )
    "List.length/normal",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<int> = [ (1); (2) ]
        let invoke () : int = List.length a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "List.length/normal" (Choice1Of2(2)) invoke preserve callbacks invariants
    )
    "List.length/empty",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<int> = []
        let invoke () : int = List.length a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "List.length/empty" (Choice1Of2(0)) invoke preserve callbacks invariants
    )
    "Maybe.Just/constructor",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 7
        let invoke () : option<int> = Maybe.just a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<int>) = ()
        check "Maybe.Just/constructor" (Choice1Of2(Some(7))) invoke preserve callbacks invariants
    )
    "Maybe.Nothing/constructor",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let invoke () : option<int> = Maybe.nothing
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<int>) = ()
        check "Maybe.Nothing/constructor" (Choice1Of2(None)) invoke preserve callbacks invariants
    )
    "Maybe.map/present",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int -> string) =
            callback (fun (value: int) ->
                value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            )

        let a1: option<int> = Some(7)
        let invoke () : option<string> = Maybe.map a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 1 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<string>) = ()
        check "Maybe.map/present" (Choice1Of2(Some("7"))) invoke preserve callbacks invariants
    )
    "Maybe.map/absent",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int -> string) =
            callback (fun (value: int) ->
                value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            )

        let a1: option<int> = None
        let invoke () : option<string> = Maybe.map a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<string>) = ()
        check "Maybe.map/absent" (Choice1Of2(None)) invoke preserve callbacks invariants
    )
    "Maybe.andThen/present",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int -> option<int>) =
            callback (fun value -> if value > 0 then Some value else None)

        let a1: option<int> = Some(7)
        let invoke () : option<int> = Maybe.andThen a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 1 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<int>) = ()
        check "Maybe.andThen/present" (Choice1Of2(Some(7))) invoke preserve callbacks invariants
    )
    "Maybe.andThen/absent",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int -> option<int>) =
            callback (fun value -> if value > 0 then Some value else None)

        let a1: option<int> = None
        let invoke () : option<int> = Maybe.andThen a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<int>) = ()
        check "Maybe.andThen/absent" (Choice1Of2(None)) invoke preserve callbacks invariants
    )
    "Maybe.andThen/callback-failure",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int -> option<int>) =
            callback (fun value -> if value > 0 then Some value else None)

        let a1: option<int> = Some(-1)
        let invoke () : option<int> = Maybe.andThen a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 1 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<int>) = ()

        check
            "Maybe.andThen/callback-failure"
            (Choice1Of2(None))
            invoke
            preserve
            callbacks
            invariants
    )
    "Result.Ok/constructor",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 7
        let invoke () : Result<int, string> = Result.ok a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Result<int, string>) = ()
        check "Result.Ok/constructor" (Choice1Of2(Ok(7))) invoke preserve callbacks invariants
    )
    "Result.Err/constructor",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "failure"
        let invoke () : Result<int, string> = Result.err a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Result<int, string>) = ()

        check
            "Result.Err/constructor"
            (Choice1Of2(Error("failure")))
            invoke
            preserve
            callbacks
            invariants
    )
    "Result.map/present",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int -> string) =
            callback (fun (value: int) ->
                value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            )

        let a1: Result<int, string> = Ok(7)
        let invoke () : Result<string, string> = Result.map a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 1 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Result<string, string>) = ()
        check "Result.map/present" (Choice1Of2(Ok("7"))) invoke preserve callbacks invariants
    )
    "Result.map/absent",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int -> string) =
            callback (fun (value: int) ->
                value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            )

        let a1: Result<int, string> = Error("failure")
        let invoke () : Result<string, string> = Result.map a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Result<string, string>) = ()

        check
            "Result.map/absent"
            (Choice1Of2(Error("failure")))
            invoke
            preserve
            callbacks
            invariants
    )
    "Result.andThen/present",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int -> Result<int, string>) =
            callback (fun value -> if value > 0 then Ok value else Error "negative")

        let a1: Result<int, string> = Ok(7)
        let invoke () : Result<int, string> = Result.andThen a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 1 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Result<int, string>) = ()
        check "Result.andThen/present" (Choice1Of2(Ok(7))) invoke preserve callbacks invariants
    )
    "Result.andThen/absent",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int -> Result<int, string>) =
            callback (fun value -> if value > 0 then Ok value else Error "negative")

        let a1: Result<int, string> = Error("failure")
        let invoke () : Result<int, string> = Result.andThen a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Result<int, string>) = ()

        check
            "Result.andThen/absent"
            (Choice1Of2(Error("failure")))
            invoke
            preserve
            callbacks
            invariants
    )
    "Result.andThen/callback-failure",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int -> Result<int, string>) =
            callback (fun value -> if value > 0 then Ok value else Error "negative")

        let a1: Result<int, string> = Ok(-1)
        let invoke () : Result<int, string> = Result.andThen a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 1 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Result<int, string>) = ()

        check
            "Result.andThen/callback-failure"
            (Choice1Of2(Error("negative")))
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.empty/empty",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let invoke () : Map<string, int> = Dict.empty
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<string, int>) = ()
        check "Dict.empty/empty" (Choice1Of2(Map.ofList [])) invoke preserve callbacks invariants
    )
    "Dict.singleton/normal",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "a"
        let a1: int = 1

        let invoke () : Map<string, int> =
            (fun key value -> Dict.fromList [ key, value ]) a0 a1

        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<string, int>) = ()

        check
            "Dict.singleton/normal"
            (Choice1Of2(Map.ofList [ ("a", 1) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.fromList/duplicate-last",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<(string * int)> = [ (("b", 2)); (("a", 0)); (("a", 1)) ]
        let before0 = snapshot (box a0)
        let invoke () : Map<string, int> = Dict.fromList a0

        let preserve () =
            Expect.equal (snapshot (box a0)) before0 "operand 0 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<string, int>) =
            let equivalent: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]

            Expect.isTrue
                (actual = equivalent)
                "equalTo independently constructed structural equality"

            let equivalent: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]

            Expect.isTrue
                (actual = equivalent)
                "hashEqualTo independently constructed structural equality"

            Expect.equal
                (hash actual)
                (hash equivalent)
                "equal values have equal structural hashes"

        check
            "Dict.fromList/duplicate-last"
            (Choice1Of2(Map.ofList [ ("a", 1); ("b", 2) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.fromList/empty",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<(string * int)> = []
        let invoke () : Map<string, int> = Dict.fromList a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<string, int>) = ()

        check
            "Dict.fromList/empty"
            (Choice1Of2(Map.ofList []))
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.fromList/nested-keys-values",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<(list<int> * option<list<int>>)> = [
            (([ (1); (0) ], None))
            (([ (1) ], Some([ (7) ])))
        ]

        let invoke () : Map<list<int>, option<list<int>>> = Dict.fromList a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<list<int>, option<list<int>>>) =
            let equivalent: Map<list<int>, option<list<int>>> =
                Map.ofList [ ([ (1) ], Some([ (7) ])); ([ (1); (0) ], None) ]

            Expect.isTrue
                (actual = equivalent)
                "equalTo independently constructed structural equality"

            let equivalent: Map<list<int>, option<list<int>>> =
                Map.ofList [ ([ (1) ], Some([ (7) ])); ([ (1); (0) ], None) ]

            Expect.isTrue
                (actual = equivalent)
                "hashEqualTo independently constructed structural equality"

            Expect.equal
                (hash actual)
                (hash equivalent)
                "equal values have equal structural hashes"

        check
            "Dict.fromList/nested-keys-values"
            (Choice1Of2(Map.ofList [ ([ (1) ], Some([ (7) ])); ([ (1); (0) ], None) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.get/present",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "a"
        let a1: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let invoke () : option<int> = Dict.get a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<int>) = ()
        check "Dict.get/present" (Choice1Of2(Some(1))) invoke preserve callbacks invariants
    )
    "Dict.get/absent",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "z"
        let a1: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let invoke () : option<int> = Dict.get a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<int>) = ()
        check "Dict.get/absent" (Choice1Of2(None)) invoke preserve callbacks invariants
    )
    "Dict.insert/replace",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "a"
        let a1: int = 7
        let a2: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let before2 = snapshot (box a2)
        let invoke () : Map<string, int> = Dict.insert a0 a1 a2

        let preserve () =
            Expect.equal (snapshot (box a2)) before2 "operand 2 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<string, int>) = ()

        check
            "Dict.insert/replace"
            (Choice1Of2(Map.ofList [ ("a", 7); ("b", 2) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.insert/new",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "c"
        let a1: int = 3
        let a2: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let before2 = snapshot (box a2)
        let invoke () : Map<string, int> = Dict.insert a0 a1 a2

        let preserve () =
            Expect.equal (snapshot (box a2)) before2 "operand 2 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<string, int>) = ()

        check
            "Dict.insert/new"
            (Choice1Of2(Map.ofList [ ("a", 1); ("b", 2); ("c", 3) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.update/present",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "a"

        let a1: (option<int> -> option<int>) =
            callback (Option.map (fun value -> Checked.(+) value 1))

        let a2: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let before2 = snapshot (box a2)
        let invoke () : Map<string, int> = Dict.update a0 a1 a2

        let preserve () =
            Expect.equal (snapshot (box a2)) before2 "operand 2 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 1 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<string, int>) = ()

        check
            "Dict.update/present"
            (Choice1Of2(Map.ofList [ ("a", 2); ("b", 2) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.update/missing-insert",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "c"
        let a1: (option<int> -> option<int>) = callback (fun _ -> Some 7)
        let a2: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let before2 = snapshot (box a2)
        let invoke () : Map<string, int> = Dict.update a0 a1 a2

        let preserve () =
            Expect.equal (snapshot (box a2)) before2 "operand 2 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 1 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<string, int>) = ()

        check
            "Dict.update/missing-insert"
            (Choice1Of2(Map.ofList [ ("a", 1); ("b", 2); ("c", 7) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.update/remove",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "a"
        let a1: (option<int> -> option<int>) = callback (fun _ -> None)
        let a2: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let before2 = snapshot (box a2)
        let invoke () : Map<string, int> = Dict.update a0 a1 a2

        let preserve () =
            Expect.equal (snapshot (box a2)) before2 "operand 2 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 1 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<string, int>) = ()

        check
            "Dict.update/remove"
            (Choice1Of2(Map.ofList [ ("b", 2) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.update/missing-noop",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "z"

        let a1: (option<int> -> option<int>) =
            callback (Option.map (fun value -> Checked.(+) value 1))

        let a2: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let before2 = snapshot (box a2)
        let invoke () : Map<string, int> = Dict.update a0 a1 a2

        let preserve () =
            Expect.equal (snapshot (box a2)) before2 "operand 2 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 1 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<string, int>) = ()

        check
            "Dict.update/missing-noop"
            (Choice1Of2(Map.ofList [ ("a", 1); ("b", 2) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.remove/present",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "a"
        let a1: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let before1 = snapshot (box a1)
        let invoke () : Map<string, int> = Dict.remove a0 a1

        let preserve () =
            Expect.equal (snapshot (box a1)) before1 "operand 1 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<string, int>) = ()

        check
            "Dict.remove/present"
            (Choice1Of2(Map.ofList [ ("b", 2) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.remove/missing",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "z"
        let a1: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let before1 = snapshot (box a1)
        let invoke () : Map<string, int> = Dict.remove a0 a1

        let preserve () =
            Expect.equal (snapshot (box a1)) before1 "operand 1 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<string, int>) = ()

        check
            "Dict.remove/missing"
            (Choice1Of2(Map.ofList [ ("a", 1); ("b", 2) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.toList/ordered",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let invoke () : list<(string * int)> = Dict.toList a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: list<(string * int)>) = ()

        check
            "Dict.toList/ordered"
            (Choice1Of2([ (("a", 1)); (("b", 2)) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.toList/empty",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Map<string, int> = Map.ofList []
        let invoke () : list<(string * int)> = Dict.toList a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: list<(string * int)>) = ()
        check "Dict.toList/empty" (Choice1Of2([])) invoke preserve callbacks invariants
    )
    "Dict.union/first-wins",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let a1: Map<string, int> = Map.ofList [ ("a", 99); ("c", 3) ]
        let before0 = snapshot (box a0)
        let before1 = snapshot (box a1)
        let invoke () : Map<string, int> = Dict.union a0 a1

        let preserve () =
            Expect.equal (snapshot (box a0)) before0 "operand 0 unchanged"
            Expect.equal (snapshot (box a1)) before1 "operand 1 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<string, int>) = ()

        check
            "Dict.union/first-wins"
            (Choice1Of2(Map.ofList [ ("a", 1); ("b", 2); ("c", 3) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.union/empty-left",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Map<string, int> = Map.ofList []
        let a1: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let invoke () : Map<string, int> = Dict.union a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<string, int>) = ()

        check
            "Dict.union/empty-left"
            (Choice1Of2(Map.ofList [ ("a", 1); ("b", 2) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Set.empty/empty",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let invoke () : Set<int> = Set.empty
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Set<int>) = ()
        check "Set.empty/empty" (Choice1Of2(Set.ofList [])) invoke preserve callbacks invariants
    )
    "Set.singleton/normal",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1

        let invoke () : Set<int> =
            (fun value -> Set.fromList [ value ]) a0

        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Set<int>) = ()

        check
            "Set.singleton/normal"
            (Choice1Of2(Set.ofList [ (1) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Set.fromList/duplicates",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<int> = [ (3); (1); (3); (2) ]
        let before0 = snapshot (box a0)
        let invoke () : Set<int> = Set.fromList a0

        let preserve () =
            Expect.equal (snapshot (box a0)) before0 "operand 0 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Set<int>) =
            let equivalent: Set<int> = Set.ofList [ (1); (2); (3) ]

            Expect.isTrue
                (actual = equivalent)
                "equalTo independently constructed structural equality"

            let equivalent: Set<int> = Set.ofList [ (1); (2); (3) ]

            Expect.isTrue
                (actual = equivalent)
                "hashEqualTo independently constructed structural equality"

            Expect.equal
                (hash actual)
                (hash equivalent)
                "equal values have equal structural hashes"

        check
            "Set.fromList/duplicates"
            (Choice1Of2(Set.ofList [ (1); (2); (3) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Set.insert/new",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 2
        let a1: Set<int> = Set.ofList [ (1) ]
        let before1 = snapshot (box a1)
        let invoke () : Set<int> = Set.insert a0 a1

        let preserve () =
            Expect.equal (snapshot (box a1)) before1 "operand 1 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Set<int>) = ()

        check
            "Set.insert/new"
            (Choice1Of2(Set.ofList [ (1); (2) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Set.insert/duplicate",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: Set<int> = Set.ofList [ (1) ]
        let before1 = snapshot (box a1)
        let invoke () : Set<int> = Set.insert a0 a1

        let preserve () =
            Expect.equal (snapshot (box a1)) before1 "operand 1 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Set<int>) = ()

        check
            "Set.insert/duplicate"
            (Choice1Of2(Set.ofList [ (1) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Set.remove/present",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: Set<int> = Set.ofList [ (1); (2) ]
        let before1 = snapshot (box a1)
        let invoke () : Set<int> = Set.remove a0 a1

        let preserve () =
            Expect.equal (snapshot (box a1)) before1 "operand 1 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Set<int>) = ()

        check
            "Set.remove/present"
            (Choice1Of2(Set.ofList [ (2) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Set.remove/missing",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 7
        let a1: Set<int> = Set.ofList [ (1) ]
        let invoke () : Set<int> = Set.remove a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Set<int>) = ()

        check
            "Set.remove/missing"
            (Choice1Of2(Set.ofList [ (1) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Set.toList/ordered",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Set<int> = Set.ofList [ (1); (2) ]
        let invoke () : list<int> = Set.toList a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: list<int>) = ()
        check "Set.toList/ordered" (Choice1Of2([ (1); (2) ])) invoke preserve callbacks invariants
    )
    "Set.union/overlap",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Set<int> = Set.ofList [ (1); (2) ]
        let a1: Set<int> = Set.ofList [ (2); (3) ]
        let before0 = snapshot (box a0)
        let before1 = snapshot (box a1)
        let invoke () : Set<int> = Set.union a0 a1

        let preserve () =
            Expect.equal (snapshot (box a0)) before0 "operand 0 unchanged"
            Expect.equal (snapshot (box a1)) before1 "operand 1 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Set<int>) = ()

        check
            "Set.union/overlap"
            (Choice1Of2(Set.ofList [ (1); (2); (3) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.equal/list",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<option<int>> = [ (Some(1)) ]
        let a1: list<option<int>> = [ (Some(1)) ]
        let invoke () : bool = (=) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.equal/list" (Choice1Of2(true)) invoke preserve callbacks invariants
    )
    "Basics.equal/dict",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let a1: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let invoke () : bool = (=) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.equal/dict" (Choice1Of2(true)) invoke preserve callbacks invariants
    )
    "Basics.equal/set",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Set<int> = Set.ofList [ (1); (2) ]
        let a1: Set<int> = Set.ofList [ (1); (2) ]
        let invoke () : bool = (=) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.equal/set" (Choice1Of2(true)) invoke preserve callbacks invariants
    )
    "Basics.equal/result",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Result<int, string> = Error("failure")
        let a1: Result<int, string> = Error("failure")
        let invoke () : bool = (=) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.equal/result" (Choice1Of2(true)) invoke preserve callbacks invariants
    )
    "Basics.equal/decimal-scale",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "15" 1
        let a1: decimal = decimalInput "150" 2
        let invoke () : bool = (=) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.equal/decimal-scale" (Choice1Of2(true)) invoke preserve callbacks invariants
    )
    "Basics.equal/unit",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: unit = ()
        let a1: unit = ()
        let invoke () : bool = (=) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.equal/unit" (Choice1Of2(true)) invoke preserve callbacks invariants
    )
    "Basics.equal/nan",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "NaN"
        let a1: float = floatInput "NaN"
        let invoke () : bool = (=) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.equal/nan" (Choice1Of2(false)) invoke preserve callbacks invariants
    )
    "Basics.equal/signed-zero",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "-0"
        let a1: float = floatInput "+0"
        let invoke () : bool = (=) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.equal/signed-zero" (Choice1Of2(true)) invoke preserve callbacks invariants
    )
    "Basics.notEqual/list",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<option<int>> = [ (Some(1)) ]
        let a1: list<option<int>> = [ (Some(1)) ]
        let invoke () : bool = (<>) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.notEqual/list" (Choice1Of2(false)) invoke preserve callbacks invariants
    )
    "Basics.notEqual/dict",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let a1: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let invoke () : bool = (<>) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.notEqual/dict" (Choice1Of2(false)) invoke preserve callbacks invariants
    )
    "Basics.notEqual/set",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Set<int> = Set.ofList [ (1); (2) ]
        let a1: Set<int> = Set.ofList [ (1); (2) ]
        let invoke () : bool = (<>) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.notEqual/set" (Choice1Of2(false)) invoke preserve callbacks invariants
    )
    "Basics.notEqual/result",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Result<int, string> = Error("failure")
        let a1: Result<int, string> = Error("failure")
        let invoke () : bool = (<>) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.notEqual/result" (Choice1Of2(false)) invoke preserve callbacks invariants
    )
    "Basics.notEqual/decimal-scale",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "15" 1
        let a1: decimal = decimalInput "150" 2
        let invoke () : bool = (<>) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.notEqual/decimal-scale"
            (Choice1Of2(false))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.notEqual/unit",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: unit = ()
        let a1: unit = ()
        let invoke () : bool = (<>) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.notEqual/unit" (Choice1Of2(false)) invoke preserve callbacks invariants
    )
    "Basics.notEqual/nan",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "NaN"
        let a1: float = floatInput "NaN"
        let invoke () : bool = (<>) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.notEqual/nan" (Choice1Of2(true)) invoke preserve callbacks invariants
    )
    "Basics.notEqual/signed-zero",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "-0"
        let a1: float = floatInput "+0"
        let invoke () : bool = (<>) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.notEqual/signed-zero"
            (Choice1Of2(false))
            invoke
            preserve
            callbacks
            invariants
    )
    "Decimal.add/exact",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "15" 1
        let a1: decimal = decimalInput "25" 2
        let invoke () : decimal = Decimal.add a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: decimal) = ()

        check
            "Decimal.add/exact"
            (Choice1Of2(decimalInput "175" 2))
            invoke
            preserve
            callbacks
            invariants
    )
    "Decimal.add/overflow",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "79228162514264337593543950335" 0
        let a1: decimal = decimalInput "1" 0
        let invoke () : decimal = Decimal.add a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: decimal) = ()
        check "Decimal.add/overflow" (Choice2Of2 "Overflow") invoke preserve callbacks invariants
    )
    "Decimal.add/min-scale",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "1" 28
        let a1: decimal = decimalInput "0" 0
        let invoke () : decimal = Decimal.add a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: decimal) = ()

        check
            "Decimal.add/min-scale"
            (Choice1Of2(decimalInput "1" 28))
            invoke
            preserve
            callbacks
            invariants
    )
    "Decimal.add/max",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "79228162514264337593543950335" 0
        let a1: decimal = decimalInput "0" 0
        let invoke () : decimal = Decimal.add a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: decimal) = ()

        check
            "Decimal.add/max"
            (Choice1Of2(decimalInput "79228162514264337593543950335" 0))
            invoke
            preserve
            callbacks
            invariants
    )
    "Decimal.sub/exact",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "15" 1
        let a1: decimal = decimalInput "25" 2
        let invoke () : decimal = Decimal.subtract a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: decimal) = ()

        check
            "Decimal.sub/exact"
            (Choice1Of2(decimalInput "125" 2))
            invoke
            preserve
            callbacks
            invariants
    )
    "Decimal.mul/exact",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "15" 1
        let a1: decimal = decimalInput "25" 2
        let invoke () : decimal = Decimal.multiply a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: decimal) = ()

        check
            "Decimal.mul/exact"
            (Choice1Of2(decimalInput "375" 3))
            invoke
            preserve
            callbacks
            invariants
    )
    "Decimal.div/exact",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "1" 0
        let a1: decimal = decimalInput "8" 0
        let invoke () : option<decimal> = Decimal.div a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<decimal>) = ()

        check
            "Decimal.div/exact"
            (Choice1Of2(Some(decimalInput "125" 3)))
            invoke
            preserve
            callbacks
            invariants
    )
    "Decimal.div/zero",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "1" 0
        let a1: decimal = decimalInput "0" 0
        let invoke () : option<decimal> = Decimal.div a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<decimal>) = ()
        check "Decimal.div/zero" (Choice1Of2(None)) invoke preserve callbacks invariants
    )
    "Decimal.compare/scale",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "150" 2
        let a1: decimal = decimalInput "15" 1
        let invoke () : Morphir.SDK.Order = Decimal.compare a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Morphir.SDK.Order) = ()

        check
            "Decimal.compare/scale"
            (Choice1Of2(Morphir.SDK.EQ))
            invoke
            preserve
            callbacks
            invariants
    )
    "Decimal.toString/trim-scale",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "150" 2
        let invoke () : string = Decimal.toString a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: string) = ()

        check
            "Decimal.toString/trim-scale"
            (Choice1Of2("1.5"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Decimal.toString/zero",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "0" 2
        let invoke () : string = Decimal.toString a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: string) = ()
        check "Decimal.toString/zero" (Choice1Of2("0")) invoke preserve callbacks invariants
    )
    "Basics.multiply/float-overflow",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "1.7976931348623157E+308"
        let a1: float = floatInput "2"
        let invoke () : float = Basics.multiply a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.multiply/float-overflow"
            (Choice1Of2(floatInput "+Infinity"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.multiply/float-underflow",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "-5E-324"
        let a1: float = floatInput "0.5"
        let invoke () : float = Basics.multiply a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.multiply/float-underflow"
            (Choice1Of2(floatInput "-0"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.add/float-smallest",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "5E-324"
        let a1: float = floatInput "+0"
        let invoke () : float = Basics.add a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.add/float-smallest"
            (Choice1Of2(floatInput "5E-324"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.add/int-minimum",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = -2147483648
        let a1: int = 0
        let invoke () : int = Basics.add a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()

        check
            "Basics.add/int-minimum"
            (Choice1Of2(-2147483648))
            invoke
            preserve
            callbacks
            invariants
    )
    "List.cons/structural-hash",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: list<int> = [ (2) ]
        let before1 = snapshot (box a1)

        let invoke () : list<int> =
            (fun head tail ->
                head
                :: tail
            )
                a0
                a1

        let preserve () =
            Expect.equal (snapshot (box a1)) before1 "operand 1 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: list<int>) =
            let equivalent: list<int> = [ (1); (2) ]

            Expect.isTrue
                (actual = equivalent)
                "equalTo independently constructed structural equality"

            let equivalent: list<int> = [ (1); (2) ]

            Expect.isTrue
                (actual = equivalent)
                "hashEqualTo independently constructed structural equality"

            Expect.equal
                (hash actual)
                (hash equivalent)
                "equal values have equal structural hashes"

        check
            "List.cons/structural-hash"
            (Choice1Of2([ (1); (2) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Decimal.sub/negative-overflow",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "-79228162514264337593543950335" 0
        let a1: decimal = decimalInput "1" 0
        let invoke () : decimal = Decimal.subtract a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: decimal) = ()

        check
            "Decimal.sub/negative-overflow"
            (Choice2Of2 "Overflow")
            invoke
            preserve
            callbacks
            invariants
    )
    "Decimal.mul/overflow",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "79228162514264337593543950335" 0
        let a1: decimal = decimalInput "2" 0
        let invoke () : decimal = Decimal.multiply a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: decimal) = ()
        check "Decimal.mul/overflow" (Choice2Of2 "Overflow") invoke preserve callbacks invariants
    )
    "Basics.lessThan/equal",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: int = 1
        let invoke () : bool = Basics.lessThan a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.lessThan/equal" (Choice1Of2(false)) invoke preserve callbacks invariants
    )
    "Basics.lessThan/reversed",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 2
        let a1: int = 1
        let invoke () : bool = Basics.lessThan a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.lessThan/reversed" (Choice1Of2(false)) invoke preserve callbacks invariants
    )
    "Basics.lessThanOrEqual/equal",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: int = 1
        let invoke () : bool = Basics.lessThanOrEqual a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.lessThanOrEqual/equal"
            (Choice1Of2(true))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.lessThanOrEqual/reversed",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 2
        let a1: int = 1
        let invoke () : bool = Basics.lessThanOrEqual a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.lessThanOrEqual/reversed"
            (Choice1Of2(false))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.greaterThan/equal",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: int = 1
        let invoke () : bool = Basics.greaterThan a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.greaterThan/equal" (Choice1Of2(false)) invoke preserve callbacks invariants
    )
    "Basics.greaterThan/reversed",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 2
        let a1: int = 1
        let invoke () : bool = Basics.greaterThan a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.greaterThan/reversed" (Choice1Of2(true)) invoke preserve callbacks invariants
    )
    "Basics.greaterThanOrEqual/equal",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 1
        let a1: int = 1
        let invoke () : bool = Basics.greaterThanOrEqual a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.greaterThanOrEqual/equal"
            (Choice1Of2(true))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.greaterThanOrEqual/reversed",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 2
        let a1: int = 1
        let invoke () : bool = Basics.greaterThanOrEqual a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.greaterThanOrEqual/reversed"
            (Choice1Of2(true))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.equal/unequal-nested-list",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<option<int>> = [ (Some(1)) ]
        let a1: list<option<int>> = [ (Some(2)) ]
        let invoke () : bool = (=) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.equal/unequal-nested-list"
            (Choice1Of2(false))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.equal/unequal-dict",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let a1: Map<string, int> = Map.ofList [ ("a", 7); ("b", 2) ]
        let invoke () : bool = (=) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.equal/unequal-dict" (Choice1Of2(false)) invoke preserve callbacks invariants
    )
    "Basics.equal/unequal-set",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Set<int> = Set.ofList [ (1) ]
        let a1: Set<int> = Set.ofList [ (2) ]
        let invoke () : bool = (=) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.equal/unequal-set" (Choice1Of2(false)) invoke preserve callbacks invariants
    )
    "Basics.equal/unequal-result-constructor",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Result<string, string> = Ok("same")
        let a1: Result<string, string> = Error("same")
        let invoke () : bool = (=) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.equal/unequal-result-constructor"
            (Choice1Of2(false))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.equal/unequal-maybe-constructor",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: option<int> = None
        let a1: option<int> = Some(1)
        let invoke () : bool = (=) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.equal/unequal-maybe-constructor"
            (Choice1Of2(false))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.equal/unequal-decimal",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "15" 1
        let a1: decimal = decimalInput "16" 1
        let invoke () : bool = (=) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.equal/unequal-decimal"
            (Choice1Of2(false))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.notEqual/unequal-nested-list",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<option<int>> = [ (Some(1)) ]
        let a1: list<option<int>> = [ (Some(2)) ]
        let invoke () : bool = (<>) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.notEqual/unequal-nested-list"
            (Choice1Of2(true))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.notEqual/unequal-dict",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Map<string, int> = Map.ofList [ ("a", 1); ("b", 2) ]
        let a1: Map<string, int> = Map.ofList [ ("a", 7); ("b", 2) ]
        let invoke () : bool = (<>) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.notEqual/unequal-dict"
            (Choice1Of2(true))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.notEqual/unequal-set",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Set<int> = Set.ofList [ (1) ]
        let a1: Set<int> = Set.ofList [ (2) ]
        let invoke () : bool = (<>) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()
        check "Basics.notEqual/unequal-set" (Choice1Of2(true)) invoke preserve callbacks invariants
    )
    "Basics.notEqual/unequal-result-constructor",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Result<string, string> = Ok("same")
        let a1: Result<string, string> = Error("same")
        let invoke () : bool = (<>) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.notEqual/unequal-result-constructor"
            (Choice1Of2(true))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.notEqual/unequal-maybe-constructor",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: option<int> = None
        let a1: option<int> = Some(1)
        let invoke () : bool = (<>) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.notEqual/unequal-maybe-constructor"
            (Choice1Of2(true))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.notEqual/unequal-decimal",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "15" 1
        let a1: decimal = decimalInput "16" 1
        let invoke () : bool = (<>) a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: bool) = ()

        check
            "Basics.notEqual/unequal-decimal"
            (Choice1Of2(true))
            invoke
            preserve
            callbacks
            invariants
    )
    "List.singleton/decimal-scale-hash",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "150" 2
        let invoke () : list<decimal> = List.singleton a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: list<decimal>) =
            let equivalent: list<decimal> = [ (decimalInput "150" 2) ]

            Expect.isTrue
                (actual = equivalent)
                "equalTo independently constructed structural equality"

            let equivalent: list<decimal> = [ (decimalInput "150" 2) ]

            Expect.isTrue
                (actual = equivalent)
                "hashEqualTo independently constructed structural equality"

            Expect.equal
                (hash actual)
                (hash equivalent)
                "equal values have equal structural hashes"

        check
            "List.singleton/decimal-scale-hash"
            (Choice1Of2([ (decimalInput "15" 1) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "List.singleton/nested-result-hash",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: Result<list<decimal>, string> = Ok([ (decimalInput "150" 2) ])
        let invoke () : list<Result<list<decimal>, string>> = List.singleton a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: list<Result<list<decimal>, string>>) =
            let equivalent: list<Result<list<decimal>, string>> = [
                (Ok([ (decimalInput "150" 2) ]))
            ]

            Expect.isTrue
                (actual = equivalent)
                "equalTo independently constructed structural equality"

            let equivalent: list<Result<list<decimal>, string>> = [
                (Ok([ (decimalInput "150" 2) ]))
            ]

            Expect.isTrue
                (actual = equivalent)
                "hashEqualTo independently constructed structural equality"

            Expect.equal
                (hash actual)
                (hash equivalent)
                "equal values have equal structural hashes"

        check
            "List.singleton/nested-result-hash"
            (Choice1Of2([ (Ok([ (decimalInput "15" 1) ])) ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Result.Ok/decimal-scale-hash",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<decimal> = [ (decimalInput "150" 2) ]
        let invoke () : Result<list<decimal>, string> = Result.ok a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Result<list<decimal>, string>) =
            let equivalent: Result<list<decimal>, string> = Ok([ (decimalInput "150" 2) ])

            Expect.isTrue
                (actual = equivalent)
                "equalTo independently constructed structural equality"

            let equivalent: Result<list<decimal>, string> = Ok([ (decimalInput "150" 2) ])

            Expect.isTrue
                (actual = equivalent)
                "hashEqualTo independently constructed structural equality"

            Expect.equal
                (hash actual)
                (hash equivalent)
                "equal values have equal structural hashes"

        check
            "Result.Ok/decimal-scale-hash"
            (Choice1Of2(Ok([ (decimalInput "15" 1) ])))
            invoke
            preserve
            callbacks
            invariants
    )
    "Result.Err/nested-error-hash",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<int> = [ (1); (2) ]
        let invoke () : Result<string, list<int>> = Result.err a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Result<string, list<int>>) =
            let equivalent: Result<string, list<int>> = Error([ (1); (2) ])

            Expect.isTrue
                (actual = equivalent)
                "equalTo independently constructed structural equality"

            let equivalent: Result<string, list<int>> = Error([ (1); (2) ])

            Expect.isTrue
                (actual = equivalent)
                "hashEqualTo independently constructed structural equality"

            Expect.equal
                (hash actual)
                (hash equivalent)
                "equal values have equal structural hashes"

        check
            "Result.Err/nested-error-hash"
            (Choice1Of2(Error([ (1); (2) ])))
            invoke
            preserve
            callbacks
            invariants
    )
    "List.map/callback-overflow",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int -> int) = callback (fun value -> Checked.(+) value 1)
        let a1: list<int> = [ (0); (2147483647) ]
        let before1 = snapshot (box a1)
        let invoke () : list<int> = List.map a0 a1

        let preserve () =
            Expect.equal (snapshot (box a1)) before1 "operand 1 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 2 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: list<int>) = ()

        check
            "List.map/callback-overflow"
            (Choice2Of2 "Overflow")
            invoke
            preserve
            callbacks
            invariants
    )
    "Maybe.map/callback-overflow",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int -> int) = callback (fun value -> Checked.(+) value 1)
        let a1: option<int> = Some(2147483647)
        let invoke () : option<int> = Maybe.map a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 1 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: option<int>) = ()

        check
            "Maybe.map/callback-overflow"
            (Choice2Of2 "Overflow")
            invoke
            preserve
            callbacks
            invariants
    )
    "Result.map/callback-overflow",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int -> int) = callback (fun value -> Checked.(+) value 1)
        let a1: Result<int, string> = Ok(2147483647)
        let invoke () : Result<int, string> = Result.map a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 1 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Result<int, string>) = ()

        check
            "Result.map/callback-overflow"
            (Choice2Of2 "Overflow")
            invoke
            preserve
            callbacks
            invariants
    )
    "Dict.update/callback-overflow",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: string = "a"

        let a1: (option<int> -> option<int>) =
            callback (Option.map (fun value -> Checked.(+) value 1))

        let a2: Map<string, int> = Map.ofList [ ("a", 2147483647) ]
        let before2 = snapshot (box a2)
        let invoke () : Map<string, int> = Dict.update a0 a1 a2

        let preserve () =
            Expect.equal (snapshot (box a2)) before2 "operand 2 unchanged"

        let callbacks observation =
            Expect.equal callbackCalls.Value 1 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Map<string, int>) = ()

        check
            "Dict.update/callback-overflow"
            (Choice2Of2 "Overflow")
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.clamp/reversed-bounds",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 5
        let a1: int = 1
        let a2: int = 3
        let invoke () : int = Basics.clamp a0 a1 a2
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.clamp/reversed-bounds" (Choice1Of2(5)) invoke preserve callbacks invariants
    )
    "Basics.clamp/equal-bound",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: int = 3
        let a1: int = 3
        let a2: int = 2
        let invoke () : int = Basics.clamp a0 a1 a2
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: int) = ()
        check "Basics.clamp/equal-bound" (Choice1Of2(3)) invoke preserve callbacks invariants
    )
    "Basics.clamp/nan-value",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "1"
        let a1: float = floatInput "5"
        let a2: float = floatInput "NaN"
        let invoke () : float = Basics.clamp a0 a1 a2
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.clamp/nan-value"
            (Choice1Of2(floatInput "NaN"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.clamp/nan-low",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "NaN"
        let a1: float = floatInput "5"
        let a2: float = floatInput "3"
        let invoke () : float = Basics.clamp a0 a1 a2
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.clamp/nan-low"
            (Choice1Of2(floatInput "3"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.clamp/infinite-value",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "1"
        let a1: float = floatInput "5"
        let a2: float = floatInput "+Infinity"
        let invoke () : float = Basics.clamp a0 a1 a2
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.clamp/infinite-value"
            (Choice1Of2(floatInput "5"))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.clamp/signed-zero",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "-1"
        let a1: float = floatInput "1"
        let a2: float = floatInput "-0"
        let invoke () : float = Basics.clamp a0 a1 a2
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: float) = ()

        check
            "Basics.clamp/signed-zero"
            (Choice1Of2(floatInput "-0"))
            invoke
            preserve
            callbacks
            invariants
    )
    "List.singleton/signed-zero-hash",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: float = floatInput "-0"
        let invoke () : list<float> = List.singleton a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: list<float>) =
            let equivalent: list<float> = [ (floatInput "+0") ]

            Expect.isTrue
                (actual = equivalent)
                "equalTo independently constructed structural equality"

            let equivalent: list<float> = [ (floatInput "+0") ]

            Expect.isTrue
                (actual = equivalent)
                "hashEqualTo independently constructed structural equality"

            Expect.equal
                (hash actual)
                (hash equivalent)
                "equal values have equal structural hashes"

        check
            "List.singleton/signed-zero-hash"
            (Choice1Of2([ (floatInput "-0") ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Set.fromList/ordered-float",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: list<float> = [ (floatInput "2.5"); (floatInput "1.5") ]
        let invoke () : Set<float> = Set.fromList a0
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Set<float>) = ()

        check
            "Set.fromList/ordered-float"
            (Choice1Of2(Set.ofList [ (floatInput "1.5"); (floatInput "2.5") ]))
            invoke
            preserve
            callbacks
            invariants
    )
    "Basics.compare/tuple3",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: (int * string * char) = (1, "a", char 98)
        let a1: (int * string * char) = (1, "a", char 99)
        let invoke () : Morphir.SDK.Order = Basics.compare a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: Morphir.SDK.Order) = ()

        check
            "Basics.compare/tuple3"
            (Choice1Of2(Morphir.SDK.LT))
            invoke
            preserve
            callbacks
            invariants
    )
    "Decimal.add/normalized-input-scale",
    (fun () ->
        let callbackCalls = ref 0
        let callbackErrors = ResizeArray<exn>()

        let callback fn value =
            callbackCalls.Value <-
                callbackCalls.Value
                + 1

            try
                fn value
            with error ->
                callbackErrors.Add error
                reraise ()

        let a0: decimal = decimalInput "10" 29
        let a1: decimal = decimalInput "0" 0
        let invoke () : decimal = Decimal.add a0 a1
        let preserve () = ()

        let callbacks observation =
            Expect.equal callbackCalls.Value 0 "callback invocation count"

            match observation with
            | Choice2Of2 error when callbackErrors.Count > 0 ->
                Expect.isTrue
                    (Object.ReferenceEquals(error, callbackErrors.[0]))
                    "callback exception propagated unchanged"
            | _ -> ()

        let invariants (actual: decimal) = ()

        check
            "Decimal.add/normalized-input-scale"
            (Choice1Of2(decimalInput "1" 28))
            invoke
            preserve
            callbacks
            invariants
    )
]

[<Tests>]
let tests =
    testSequenced
    <| testList "Frozen F# SDK conformance" [
        for caseId, run in cases do
            testCase caseId run
        testCase
            "all 207 required IDs executed and passed"
            (fun () ->
                assertComplete (
                    cases
                    |> List.map fst
                )
            )
    ]
