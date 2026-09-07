module Fixture.Server.Tests

open Expecto
open Fixture.Shared

[<Tests>]
let tests =
    testList "Server" [
        testCase "should expose the shared greeting when the server builds it" <| fun _ ->
            let greeting: Greeting = { Message = "Fixture server" }
            Expect.equal greeting.Message "Fixture server" "Server fixture must use shared data"
    ]

[<EntryPoint>]
let main args =
    runTestsInAssemblyWithCLIArgs [] args
