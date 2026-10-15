open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "test.library-fable"
            Summary = "Library fixture with Fable-only tests for integration tests."
            Git = Git.init ()
        }
        Specs = Spec.library LibraryTests.fable
    }

    args |> Args.run
