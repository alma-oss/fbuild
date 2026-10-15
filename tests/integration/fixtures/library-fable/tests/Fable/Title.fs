module Fixture.Test.Title

open Alma.Testing
open Fixture.Library

let tests =
    testList "Title" [
        testCase "should read the title back when the document title is set" <| fun _ ->
            setTitle "Fixture"

            Expect.equal (title ()) "Fixture" "The document should hold the title it was given"
    ]
