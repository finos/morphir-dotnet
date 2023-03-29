namespace Morphir.Bogus

type internal Guard =
    static member AgainstNegative(value:int, argumentName:string) =
        if value < 0 then
            raise (new System.ArgumentOutOfRangeException(argumentName, "Value cannot be negative."))
        else ()

    static member AgainstWhiteSpace(value:string, argumentName:string) =
        if System.String.IsNullOrWhiteSpace(value) then
            raise (new System.ArgumentException("Value cannot be null or whitespace.", argumentName))
        else ()

