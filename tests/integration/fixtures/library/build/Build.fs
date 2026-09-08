open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "test.library"
            Summary = "Library fixture for integration tests."
            Git = Git.init ()
        }
        Specs = Spec.defaultLibrary
    }

    args |> Args.run
