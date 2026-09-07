open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "test.library"
            Summary = "Library fixture for the integration matrix."
            Git = Git.init ()
        }
        Specs = Spec.defaultLibrary
    }

    args |> Args.run
