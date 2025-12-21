# Dict Limitations and Workarounds

Elm's Dict has restrictions on key types. F#'s Map and Dictionary have different trade-offs.

## Elm Limitation

```elm
-- ❌ ERROR: Only comparable types (Int, String, Float, etc.)
type UserId = UserId String
type alias Users = Dict UserId User  -- Won't compile!
```

## F# Solutions

### Solution 1: Use Map with Comparison

```fsharp
type UserId = UserId of string
    with
        interface IComparable with
            member this.CompareTo(obj) =
                match obj with
                | :? UserId as other ->
                    let (UserId a) = this
                    let (UserId b) = other
                    compare a b
                | _ -> -1

// Now can use in Map
let users : Map<UserId, User> = Map.empty
```

### Solution 2: Use Dictionary with Custom Comparer

```fsharp
open System.Collections.Generic

type UserIdComparer() =
    interface IEqualityComparer<UserId> with
        member _.Equals(UserId a, UserId b) = a = b
        member _.GetHashCode(UserId str) = hash str

let users = Dictionary<UserId, User>(UserIdComparer())
```

### Solution 3: Extract Key

```fsharp
// Simpler: Use underlying type as key
let users : Map<string, User> = Map.empty
// Access: users.[UserId.value userId]
```

## Recommendation

For Morphir IR: Use **Solution 3** (extract key) for simplicity unless custom comparison logic is needed.

## History

**Version:** 1.0  
**Created:** 2025-12-21
