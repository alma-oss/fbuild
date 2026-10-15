module Fixture.Test.Doubling

open Alma.Testing
open Fixture.Library

let tests =
    testList "Doubling" [
        testCase "should double the value when given a number" <| fun _ ->
            Expect.equal (double 21) 42 "Doubling 21 must give 42"
    ]
