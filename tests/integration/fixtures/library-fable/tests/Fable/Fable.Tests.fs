module Fixture.Test.Fable.Tests

open Alma.Testing
open Fixture.Test

let tests = testList "Fable" [ Title.tests ]

register tests
