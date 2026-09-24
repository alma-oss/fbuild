module Alma.Build.Tests.BootstrapTests

open Expecto
open Alma.Build
open Alma.Build.Bootstrap
open Alma.Build.Utils

[<Tests>]
let deployedFilesTests =
    let contentsOf path specs =
        deployedFiles
        |> List.tryFind (fun deployedFile -> deployedFile.Path = path)
        |> Option.defaultWith (fun () -> failtestf "No deployed file is registered for %s" path)
        |> fun deployedFile -> deployedFile.Contents specs

    let console runtimeMode =
        Spec.defaultConsoleApplication [ Linux ]
        |> Spec.mapConsoleApplication (fun spec -> { spec with RuntimeMode = runtimeMode })

    testList "deployedFiles" [
        test "should deploy the runtime props when the spec selects a runtime" {
            [ RuntimeMode.AutoDetect; RuntimeMode.Specific Linux ]
            |> List.iter (fun runtimeMode ->
                Expect.equal
                    (contentsOf "Directory.Build.props" (console runtimeMode))
                    (Some (DeployedContents.Bundled "runtime/Directory.Build.props"))
                    "A spec that selects a runtime needs the props that carry it into each project")
        }

        test "should skip the runtime props when no runtime is selected" {
            [ console RuntimeMode.Portable; Spec.defaultLibrary ]
            |> List.iter (fun specs ->
                Expect.isNone
                    (contentsOf "Directory.Build.props" specs)
                    "A spec that builds runtime-agnostic should not receive the props")
        }

        test "should render the tools manifest for every spec" {
            [ Spec.defaultLibrary; console RuntimeMode.AutoDetect ]
            |> List.iter (fun specs ->
                match contentsOf ToolsManifest.path specs with
                | Some (DeployedContents.Rendered contents) ->
                    Expect.stringContains contents "\"isRoot\": true" "The manifest should be rendered as a root manifest"
                | other -> failtestf "The tools manifest should be rendered for %s, got %A" specs.Type other)
        }
    ]
