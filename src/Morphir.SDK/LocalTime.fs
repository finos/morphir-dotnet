namespace Morphir.SDK

open System

/// <summary>
/// LocalTime type for working with time without time zones.
/// Type alias for .NET 6+ TimeOnly.
/// ADAPTED FROM: morphir-elm src/Morphir/SDK/LocalTime.elm
/// </summary>
type LocalTime = TimeOnly

/// <summary>
/// LocalTime operations for time manipulation.
/// </summary>
module LocalTime =
    
    /// <summary>Create a LocalTime from hour, minute, second, and millisecond</summary>
    let fromParts hour minute second millisecond =
        try
            Some (TimeOnly(hour, minute, second, millisecond))
        with
        | _ -> None

    /// <summary>Get the hour from a LocalTime (0-23)</summary>
    let inline hour (time: LocalTime) = time.Hour

    /// <summary>Get the minute from a LocalTime (0-59)</summary>
    let inline minute (time: LocalTime) = time.Minute

    /// <summary>Get the second from a LocalTime (0-59)</summary>
    let inline second (time: LocalTime) = time.Second

    /// <summary>Get the millisecond from a LocalTime (0-999)</summary>
    let inline millisecond (time: LocalTime) = time.Millisecond

    /// <summary>Add hours to a LocalTime</summary>
    let inline addHours hours (time: LocalTime) = time.AddHours(hours)

    /// <summary>Add minutes to a LocalTime</summary>
    let inline addMinutes minutes (time: LocalTime) = time.AddMinutes(minutes)

    /// <summary>Add seconds to a LocalTime</summary>
    let inline addSeconds seconds (time: LocalTime) = time.Add(TimeSpan.FromSeconds(float seconds))

    /// <summary>Get current time</summary>
    let inline now () = TimeOnly.FromDateTime(DateTime.Now)

    /// <summary>Parse a LocalTime from ISO 8601 string (HH:mm:ss)</summary>
    let fromIsoString (s: string) =
        let mutable result = Unchecked.defaultof<TimeOnly>
        if TimeOnly.TryParseExact(s, "HH:mm:ss", Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.None, &result) then
            Some result
        else
            None

    /// <summary>Format a LocalTime to ISO 8601 string (HH:mm:ss)</summary>
    let inline toIsoString (time: LocalTime) = 
        time.ToString("HH:mm:ss", Globalization.CultureInfo.InvariantCulture)

    /// <summary>Compare two LocalTimes</summary>
    let compare (time1: LocalTime) (time2: LocalTime) =
        match time1.CompareTo(time2) with
        | n when n < 0 -> LT
        | 0 -> EQ
        | _ -> GT
