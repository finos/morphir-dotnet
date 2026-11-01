module CarRentalExample

open Car.Rental
open System
open System.Globalization

let requireOk =
    function
    | Ok value -> value
    | Error _ -> failwith "Unexpected rental error"

let requireError expected =
    function
    | Error actual when actual = expected -> ()
    | _ -> failwith "The generated function did not return the expected rental error"

let printAvailability label availability =
    let text =
        match availability with
        | Domain.Available -> "Available"
        | Domain.Reserved -> "Reserved"
        | Domain.Rented -> "Rented"

    Console.WriteLine(
        label
        + "="
        + text
    )

let initial = Inventory.car

Console.WriteLine(
    "dailyRate="
    + Inventory.dailyRate.ToString(CultureInfo.InvariantCulture)
)

if
    Inventory.lookup "CAR-1" [ initial ]
    <> Some initial
then
    failwith "Inventory lookup did not find the sample car"

printAvailability "inventory" (Inventory.availability initial)

let reserved =
    Reservation.reserve 3 initial
    |> requireOk

printAvailability "reservation" (Inventory.availability reserved)

let pickedUp =
    Pickup.pickup reserved
    |> requireOk

printAvailability "pickup" (Inventory.availability pickedUp)

let returned =
    Return.returnCar 3 pickedUp
    |> requireOk

printAvailability "return" (Inventory.availability returned)

let billed =
    Billing.bill returned
    |> requireOk

// Returning releases the car even when its previous invoice is still pending.
let nextReserved =
    Reservation.reserve 2 returned
    |> requireOk

let previousBilled =
    Billing.bill nextReserved
    |> requireOk

if
    Inventory.availability previousBilled
    <> Domain.Reserved
then
    failwith "Billing a prior rental changed the current reservation"

if
    Billing.invoice previousBilled
    <> Billing.invoice billed
then
    failwith "The prior billing obligation did not survive a new reservation"

match Billing.invoice billed with
| Some invoice ->
    Console.WriteLine(
        "invoice="
        + invoice.totalCents.ToString(CultureInfo.InvariantCulture)
    )
| None -> failwith "Completed billing did not produce an invoice"

// These checks call generated rules. No business decisions are reimplemented here.
Reservation.reserve 3 reserved
|> requireError Domain.Unavailable

Pickup.pickup initial
|> requireError Domain.ReservationRequired

Return.returnCar 3 initial
|> requireError Domain.ActiveRentalRequired

Reservation.reserve 0 initial
|> requireError Domain.InvalidDuration

Return.returnCar 0 pickedUp
|> requireError Domain.InvalidDuration

Billing.bill pickedUp
|> requireError Domain.ReturnRequired

Billing.bill billed
|> requireError Domain.AlreadyBilled

if
    Inventory.availability initial
    <> Domain.Available
then
    failwith "An operation mutated the original inventory"

if
    Workflow.rent 3
    <> Ok billed
then
    failwith "Composed model workflow differs from the individual calls"

Console.WriteLine("rejections=7")
