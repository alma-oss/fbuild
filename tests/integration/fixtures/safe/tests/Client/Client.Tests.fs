module Fixture.Client.Tests

open Fable.Mocha

let tests =
    testList "Client" [
        testCase "should pass the fixture assertion" <| fun _ ->
            Expect.equal 1 1 "Fixture assertion must pass"
    ]

Mocha.runTests tests |> ignore
