namespace Acme.CarRental

module Quote =
    type Rental =
        { Vehicle: Rates.VehicleClass
          Days: int
          Insurance: bool }

    let insurancePerDay = 1500

    let total (rental: Rental) : int =
        let rentalCost = Rates.dailyRate rental.Vehicle * rental.Days

        if rental.Insurance then
            rentalCost + insurancePerDay * rental.Days
        else
            rentalCost
