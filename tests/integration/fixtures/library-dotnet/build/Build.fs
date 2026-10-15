open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "test.rootlibrary"
            Summary = "Library fixture whose project sits in the repository root next to a solution file."
            Git = Git.init ()
        }
        Specs = Spec.library LibraryTests.dotNet
    }

    args |> Args.run
