open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "test.library-shared"
            Summary = "Library fixture with shared tests for integration tests."
            Git = Git.init ()
        }
        Specs = Spec.library LibraryTests.dotNetAndFable
    }

    args |> Args.run
