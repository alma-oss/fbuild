module Fixture.Test.Messages

open Alma.Testing
open Fixture.Shared

let tests =
    testList "Messages" [
        testCase "should retain the message it was given" <| fun _ ->
            let greeting: Greeting = { Message = "Hello" }
            Expect.equal greeting.Message "Hello" "Greeting message must be retained"
    ]
