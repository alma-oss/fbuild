open Fake.Core

open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "Alma.Build"
            Summary = "`fbuild` is a versioned, distributable FAKE + Paket build infrastructure for F# projects."
            Git = Git.init ()
        }
        Specs =
            Spec.defaultLibrary
            |> Spec.mapLibrary (
                fun library -> {
                    library with
                        NugetApi = NugetApi.KeyInEnvironment "NUGET_API_KEY"
                }
            )
    }

    args |> Args.run
