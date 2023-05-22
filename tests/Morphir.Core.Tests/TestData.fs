module Morphir.IR.Tests.TestData

open System
open Bogus
open Morphir.IR


do Randomizer.Seed <- new Random(8675309)

let faker = Faker "en"


let names = [
    for i = 0 to 10 do
        faker.Company.CompanyName()
        |> Name.fromString
]

let paths = [
    for i = 0 to 10 do
        [
            faker.Internet.DomainSuffix()
            |> Name.fromString
        ]
        |> List.append names
        |> Path.fromList
]
