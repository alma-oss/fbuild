open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "test.executable"
            Summary = "Executable fixture for the integration matrix."
            Git = Git.init ()
        }
        Specs = Spec.defaultExecutable |> Spec.mapExecutable (fun spec -> { spec with ReleaseDir = "app" })
    }

    args |> Args.run
