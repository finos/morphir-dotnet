namespace Acme.CarRental

module Discount =
    let weeklyDiscount (days: int) (total: int) : int =
        if days >= 7 then total * 90 / 100 else total

    let discountedTotal (rental: Quote.Rental) : int =
        weeklyDiscount rental.Days (Quote.total rental)
