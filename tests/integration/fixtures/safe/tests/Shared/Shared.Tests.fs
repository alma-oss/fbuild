module Fixture.Shared.Tests

open Expecto
open Fixture.Shared

let tests =
    testList "Shared" [
        testCase "should retain the message it was given" <| fun _ ->
            let greeting: Greeting = { Message = "Hello" }
            Expect.equal greeting.Message "Hello" "Greeting message must be retained"
    ]
