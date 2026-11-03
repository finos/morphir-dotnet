module Car.Rental.Ordering

let compare (left: int) (right: int) : Morphir.SDK.Order =
    (Morphir.SDK.Basics.compare (left) (right))

let less: Morphir.SDK.Order = Morphir.SDK.LT
