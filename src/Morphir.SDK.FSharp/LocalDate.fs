namespace Morphir.SDK

open System

/// <summary>
/// LocalDate type for working with dates without time zones.
/// Type alias for .NET 6+ DateOnly.
/// ADAPTED FROM: morphir-elm src/Morphir/SDK/LocalDate.elm
/// </summary>
type LocalDate = DateOnly

/// <summary>
/// LocalDate operations for date manipulation.
/// </summary>
module LocalDate =
    
    /// <summary>Create a LocalDate from year, month, and day</summary>
    let fromParts year month day =
        try
            Some (DateOnly(year, month, day))
        with
        | _ -> None

    /// <summary>Get the year from a LocalDate</summary>
    let inline year (date: LocalDate) = date.Year

    /// <summary>Get the month from a LocalDate (1-12)</summary>
    let inline month (date: LocalDate) = date.Month

    /// <summary>Get the day from a LocalDate (1-31)</summary>
    let inline day (date: LocalDate) = date.Day

    /// <summary>Get day of week (0=Sunday, 6=Saturday)</summary>
    let dayOfWeek (date: LocalDate) = 
        int date.DayOfWeek

    /// <summary>Add days to a LocalDate</summary>
    let inline addDays days (date: LocalDate) = date.AddDays(days)

    /// <summary>Add months to a LocalDate</summary>
    let inline addMonths months (date: LocalDate) = date.AddMonths(months)

    /// <summary>Add years to a LocalDate</summary>
    let inline addYears years (date: LocalDate) = date.AddYears(years)

    /// <summary>Get today's date</summary>
    let inline today () = DateOnly.FromDateTime(DateTime.Today)

    /// <summary>Parse a LocalDate from ISO 8601 string (YYYY-MM-DD)</summary>
    let fromIsoString (s: string) =
        let mutable result = Unchecked.defaultof<DateOnly>
        if DateOnly.TryParseExact(s, "yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.None, &result) then
            Some result
        else
            None

    /// <summary>Format a LocalDate to ISO 8601 string (YYYY-MM-DD)</summary>
    let inline toIsoString (date: LocalDate) = 
        date.ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture)

    /// <summary>Compare two LocalDates</summary>
    let compare (date1: LocalDate) (date2: LocalDate) =
        match date1.CompareTo(date2) with
        | n when n < 0 -> LT
        | 0 -> EQ
        | _ -> GT
