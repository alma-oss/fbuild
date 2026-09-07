module Fixture.Tests

open Expecto

[<Tests>]
let tests =
    testList "Fixture" [
        testCase "should pass the fixture assertion" <| fun _ ->
            Expect.equal 1 1 "Fixture assertion must pass"
    ]

[<EntryPoint>]
let main args =
    runTestsInAssemblyWithCLIArgs [] args
