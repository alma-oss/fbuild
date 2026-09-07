open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "test.console"
            Summary = "Console application fixture for the integration matrix."
            Git = Git.init ()
        }
        Specs = Spec.defaultConsoleApplication [ Linux ]
    }

    args |> Args.run
