# Opaque Types → Phantom Types

Elm's opaque types (types with private constructors) map to F#'s phantom types using the `private` keyword.

## Pattern

**Elm:**
```elm
-- In module UserId
type UserId = UserId String

-- Smart constructor (only way to create)
userId : String -> Maybe UserId
userId str =
    if String.length str > 0 then
        Just (UserId str)
    else
        Nothing

-- Extraction
getUserIdString : UserId -> String
getUserIdString (UserId str) = str
```

**F#:**
```fsharp
// Private constructor
type UserId = private UserId of string

module UserId =
    let create (str: string) : UserId option =
        if String.length str > 0 then
            Some (UserId str)
        else
            None
    
    let value (UserId str) = str

// Or use active pattern for extraction
let (|UserId|) (UserId str) = str
```

## Use Cases

- Email addresses
- User IDs, session tokens
- Validated domain values (Age, Quantity, etc.)
- Enforcing invariants at type level

## History

**Version:** 1.0  
**Created:** 2025-12-21
