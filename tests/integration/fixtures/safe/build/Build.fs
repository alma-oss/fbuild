open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "test.safe"
            Summary = "SAFE-Stack fixture for integration tests."
            Git = Git.init ()
        }
        Specs = Spec.defaultSAFEStackApplication "5.0.3"
    }

    args |> Args.run
