module Fixture.Test.Client.Tests

open Alma.Testing
open Fixture.Test

let tests = testList "Client" [ Rendering.tests; Shared.Tests.tests ]

register tests
