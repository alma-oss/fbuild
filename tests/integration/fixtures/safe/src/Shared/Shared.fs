namespace Fixture.Shared

open System

type Greeting = { Message: string }

type OrderId = OrderId of Guid

type Shape =
    | Point
    | Circle of radius: float
    | Rectangle of width: float * height: float

type Payload = {
    Id: OrderId
    Shape: Shape
    Nickname: string option
    Missing: int option
    Count: int
    Total: int64
    Price: decimal
    Tags: string list
    Lookup: Map<string, int>
    Pair: int * string
    Outcome: Result<Shape, string>
}

type IEchoApi = { Echo: Payload -> Async<Payload> }

[<RequireQualifiedAccess>]
module Payload =
    let sample: Payload = {
        Id = OrderId (Guid "6f1c2a3b-4d5e-4f60-8a7b-9c0d1e2f3a4b")
        Shape = Rectangle (2.5, 4.0)
        Nickname = Some "fixture"
        Missing = None
        Count = 42
        Total = 9007199254740993L
        Price = 19.99m
        Tags = [ "alpha"; "beta" ]
        Lookup = Map [ "one", 1; "two", 2 ]
        Pair = 7, "seven"
        Outcome = Ok (Circle 1.5)
    }
