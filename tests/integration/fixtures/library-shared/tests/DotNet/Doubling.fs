module Fixture.Test.DoublingDotNet

open Expecto
open Fixture.Library

[<Tests>]
let tests =
    testList "DotNet" [
        testProperty "should double every value when given any integer" <| fun (value: int) ->
            double value = value + value
    ]
