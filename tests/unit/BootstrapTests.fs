module Alma.Build.Tests.BootstrapTests

open System.IO
open Expecto
open Alma.Build
open Alma.Build.Bootstrap
open Alma.Build.Utils

[<Tests>]
let deployedFilesTests =
    let contentsOf path specs =
        deployedFiles
        |> List.collect (fun deployedFile -> deployedFile specs)
        |> List.tryFind (fst >> (=) path)
        |> Option.map snd

    let vitestConfigs specs =
        deployedFiles
        |> List.collect (fun deployedFile -> deployedFile specs)
        |> List.map fst
        |> List.filter (fun path -> path.EndsWith "vitest.config.mts")

    let withLibrary (directories: string list) tests f =
        let root = Path.Combine (Path.GetTempPath (), $"fbuild-bootstrap-{System.Guid.NewGuid ():N}")

        directories
        |> List.iter (fun segment ->
            let directory = Path.Combine (root, "tests", segment)
            Directory.CreateDirectory directory |> ignore
            File.WriteAllText (Path.Combine (directory, $"{segment}.fsproj"), "")
        )

        let configOf segment =
            Path.GetRelativePath (Directory.GetCurrentDirectory (), Path.Combine (root, "tests", segment, "vitest.config.mts"))
            |> fun path -> path.Replace ("\\", "/")

        try f (Spec.library (tests root)) configOf
        finally Directory.Delete (root, true)

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
            [ console RuntimeMode.Portable; Spec.library LibraryTests.noTests ]
            |> List.iter (fun specs ->
                Expect.isNone
                    (contentsOf "Directory.Build.props" specs)
                    "A spec that builds runtime-agnostic should not receive the props")
        }

        test "should render the Vitest config into the client tests project when the spec is a SAFE-stack application" {
            let specs = Spec.defaultSAFEStackApplication "5.0.3"

            let result = contentsOf "tests/Client/vitest.config.mts" specs

            match result with
            | Some (DeployedContents.Rendered contents) ->
                Expect.stringContains contents "defineConfig" "The deployed file should be a Vitest config"
            | other -> failtestf "A SAFE-stack application should receive a rendered Vitest config, got %A" other
        }

        test "should skip the Vitest config when the library has no Fable tests" {
            let result = vitestConfigs (Spec.library LibraryTests.noTests)

            Expect.isEmpty result "A library testing only on .NET has no Vitest suite"
        }

        test "should render a node Vitest config into tests/Shared when the library has shared tests" {
            withLibrary [ "Shared"; "DotNet" ] LibraryTests.dotNetAndFableAt (fun specs configOf ->
                Expect.equal (vitestConfigs specs) [ configOf "Shared" ] "Only the Shared tests run under Vitest"

                match contentsOf (configOf "Shared") specs with
                | Some (DeployedContents.Rendered contents) ->
                    Expect.stringContains contents "environment: \"node\"," "Shared tests run in plain Node"
                | other -> failtestf "Expected a rendered config, got %A" other
            )
        }

        test "should render a jsdom Vitest config into tests/Fable when the library has Fable-only tests" {
            withLibrary [ "Fable" ] LibraryTests.fableAt (fun specs configOf ->
                Expect.equal (vitestConfigs specs) [ configOf "Fable" ] "Only the Fable tests run under Vitest"

                match contentsOf (configOf "Fable") specs with
                | Some (DeployedContents.Rendered contents) ->
                    Expect.stringContains contents "environment: \"jsdom\"," "Fable-only tests run with a DOM"
                | other -> failtestf "Expected a rendered config, got %A" other
            )
        }

        test "should render the tools manifest for every spec" {
            [ Spec.library LibraryTests.noTests; console RuntimeMode.AutoDetect ]
            |> List.iter (fun specs ->
                match contentsOf ToolsManifest.path specs with
                | Some (DeployedContents.Rendered contents) ->
                    Expect.stringContains contents "\"isRoot\": true" "The manifest should be rendered as a root manifest"
                | other -> failtestf "The tools manifest should be rendered for %s, got %A" specs.Type other)
        }
    ]

[<Tests>]
let vitestConfigTests =
    let client = {
        Path = "tests/Client"
        Environment = VitestEnvironment.Jsdom
        Include = [ "output/Client.Tests.js" ]
    }

    testList "VitestConfig.render" [
        test "should run the suite's test modules in its environment" {
            let result = VitestConfig.render client

            Expect.stringContains result "environment: \"jsdom\"," "Client tests should get a DOM from Vitest"
            Expect.stringContains result "include: [\"output/Client.Tests.js\"]," "The SAFE template's runner should be the test file"
        }

        test "should render the node environment when the suite runs in Node" {
            let result = VitestConfig.render { client with Environment = VitestEnvironment.Node; Include = [ "output/Shared.Tests.js" ] }

            Expect.stringContains result "environment: \"node\"," "A node suite gets no DOM"
            Expect.stringContains result "include: [\"output/Shared.Tests.js\"]," "The suite's own include is rendered"
        }

        test "should hide frames of Fable-compiled packages from failure stacks" {
            let result = VitestConfig.render client

            Expect.stringContains
                result
                "onStackTrace: (_error, frame) => !/fable_modules/.test(frame.file),"
                "A failure should point at the test line, not inside Alma.Testing"
        }
    ]
