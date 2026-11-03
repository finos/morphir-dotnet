namespace Morphir.SDK

/// <summary>
/// Ordering enumeration for comparisons.
/// Corresponds to Morphir.SDK.Basics.Order in morphir-elm.
/// ADAPTED FROM: main-archive src/Morphir.SDK.Core/Basics.fs
/// </summary>
type Order =
    | LT  // Less than
    | EQ  // Equal
    | GT  // Greater than

/// <summary>
/// Basic types and operations from Morphir.SDK.Basics.
/// Provides fundamental functions for comparison, arithmetic, and composition.
/// ADAPTED FROM: morphir-elm src/Morphir/SDK/Basics.elm and main-archive
/// </summary>
[<AutoOpen>]
module Basics =
    
    /// <summary>Type alias for Int in Morphir (uses F# int which is int32)</summary>
    type Int = int
    
    /// <summary>Type alias for Float in Morphir (uses F# float which is float64)</summary>
    type Float = float

    /// <summary>Identity function - returns the value unchanged</summary>
    let inline identity value = id value

    /// <summary>Addition operator</summary>
    let inline add value1 value2 =
        match box value1, box value2 with
        | (:? int as left), (:? int as right) -> unbox (box (Checked.(+) left right))
        | _ -> (+) value1 value2

    /// <summary>Subtraction operator</summary>
    let inline subtract value1 value2 =
        match box value1, box value2 with
        | (:? int as left), (:? int as right) -> unbox (box (Checked.(-) left right))
        | _ -> (-) value1 value2

    /// <summary>Multiplication operator</summary>
    let inline multiply value1 value2 =
        match box value1, box value2 with
        | (:? int as left), (:? int as right) -> unbox (box (Checked.(*) left right))
        | _ -> (*) value1 value2

    /// <summary>Division operator</summary>
    let inline divide value1 value2 = (/) value1 value2

    /// <summary>Absolute value</summary>
    let inline abs (n: ^a) = Microsoft.FSharp.Core.Operators.abs n

    /// <summary>Power function - x raised to integer power n</summary>
    let inline pow (x: ^a) (n: int) = Microsoft.FSharp.Core.Operators.pown x n

    /// <summary>Maximum of two values</summary>
    let inline max x y = Microsoft.FSharp.Core.Operators.max x y

    /// <summary>Minimum of two values</summary>
    let inline min x y = Microsoft.FSharp.Core.Operators.min x y

    /// <summary>Clamp a value between low and high bounds</summary>
    let clamp low high number =
        if number < low then low
        elif number > high then high
        else number

    /// <summary>Negate a number</summary>
    let inline negate number =
        match box number with
        | :? int as value -> unbox (box (Checked.(~-) value))
        | _ -> -number

    /// <summary>Compare two values and return Order</summary>
    let inline compare x y =
        let comparison =
            match box x, box y with
            | (:? string as left), (:? string as right) -> System.String.CompareOrdinal(left, right)
            | _ -> System.Collections.Generic.Comparer<_>.Default.Compare(x, y)

        match comparison with
        | n when n < 0 -> LT
        | 0 -> EQ
        | _ -> GT

    /// <summary>Check if first value is less than second</summary>
    let inline lessThan x y = x < y

    /// <summary>Check if first value is greater than second</summary>
    let inline greaterThan x y = x > y

    /// <summary>Check if first value is less than or equal to second</summary>
    let inline lessThanOrEqual x y = x <= y

    /// <summary>Check if first value is greater than or equal to second</summary>
    let inline greaterThanOrEqual x y = x >= y

    /// <summary>Logical NOT</summary>
    let inline not a = not a

    /// <summary>Compose two functions (f >> g)</summary>
    let inline composeRight f g x = g (f x)

    /// <summary>Compose two functions (g << f)</summary>
    let inline composeLeft g f x = g (f x)

    /// <summary>Always returns the given value, ignoring any input</summary>
    let inline always value _ = value

    /// <summary>Square root</summary>
    let inline sqrt x = sqrt x

    /// <summary>Remainder after division (modulo)</summary>
    let inline remainderBy divisor dividend =
        match box divisor, box dividend with
        | (:? int as divisor), (:? int as dividend) when
            divisor = -1
            && dividend = System.Int32.MinValue
            ->
            unbox (box 0)
        | _ -> dividend % divisor

    /// <summary>Modulo operation (different from remainder for negative numbers)</summary>
    let modBy divisor dividend =
        let r = remainderBy divisor dividend

        if (r > 0 && divisor < 0) || (r < 0 && divisor > 0) then
            r + divisor
        else
            r

    /// <summary>Integer division (floor division)</summary>
    let inline divideBy divisor dividend = dividend / divisor

    /// <summary>Convert degrees to radians</summary>
    let inline degrees deg = deg * System.Math.PI / 180.0

    /// <summary>Convert radians to degrees</summary>
    let inline radians rad = rad * 180.0 / System.Math.PI

    /// <summary>Convert turns to radians (1 turn = 2π radians)</summary>
    let inline turns t = t * 2.0 * System.Math.PI

    let private checkedInt (value: float) =
        if not (System.Double.IsFinite value) then
            raise (System.ArithmeticException("A non-finite Float has no Int32 result."))

        if
            value < float System.Int32.MinValue
            || value > float System.Int32.MaxValue
        then
            raise (System.OverflowException("The rounded value is outside Int32."))

        int value

    /// <summary>Round to the nearest Int32, resolving halves toward positive infinity.</summary>
    let round (value: float) =
        let low = System.Math.Floor value

        (if
             value
             - low
             >= 0.5
         then
             low + 1.0
         else
             low)
        |> checkedInt

    /// <summary>Round down to Int32; reject non-finite and out-of-range results.</summary>
    let floor (value: float) =
        System.Math.Floor value
        |> checkedInt

    /// <summary>Round up to Int32; reject non-finite and out-of-range results.</summary>
    let ceiling (value: float) =
        System.Math.Ceiling value
        |> checkedInt

    /// <summary>Truncate toward zero to Int32; reject non-finite and out-of-range results.</summary>
    let truncate (value: float) =
        System.Math.Truncate value
        |> checkedInt
