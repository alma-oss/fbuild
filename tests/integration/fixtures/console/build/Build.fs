open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "test.console"
            Summary = "Console application fixture for integration tests."
            Git = Git.init ()
        }
        Specs =
            Spec.defaultConsoleApplication [ Linux; OSX; OSXArm64 ]
            |> Spec.mapConsoleApplication (fun spec -> { spec with RuntimeMode = RuntimeMode.AutoDetect })
    }

    args |> Args.run
