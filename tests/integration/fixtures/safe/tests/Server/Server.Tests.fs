module Fixture.Test.Server.Tests

open Expecto

[<Tests>]
let sharedTests = Fixture.Test.Shared.Tests.tests

[<EntryPoint>]
let main args = Tests.runTestsInAssemblyWithCLIArgs [] args
