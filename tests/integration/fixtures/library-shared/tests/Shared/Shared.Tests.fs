module Fixture.Test.Shared.Tests

open Alma.Testing
open Fixture.Test

let tests = testList "Shared" [ Doubling.tests ]

register tests
