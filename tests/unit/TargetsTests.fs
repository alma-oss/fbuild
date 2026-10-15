module Alma.Build.Tests.TargetsTests

open System
open System.IO
open Expecto
open Alma.Build
open Alma.Build.Command
open Alma.Build.Targets
open Alma.Build.Utils

[<Tests>]
let commandArgumentsTests =
    testList "CommandArguments" [
        test "should append the runtime ID when the command supports runtime selection" {
            let supportedCommands = [ Dotnet Build; Dotnet Tests; Dotnet Run; Dotnet WatchRun ]

            let results =
                supportedCommands
                |> List.map (fun command -> CommandArguments.withRuntimeIdentifier (Some (RuntimeIdentifier.create "linux-x64")) command [ "existing" ])

            results
            |> List.iter (fun result ->
                Expect.equal
                    result
                    [ "existing"; "-p:AlmaBuildRuntimeIdentifier=linux-x64" ]
                    "Runtime ID should follow existing arguments, under the engine's own property")
        }

        test "should preserve arguments when portable mode is selected" {
            let result = CommandArguments.withRuntimeIdentifier None (Dotnet Build) [ "existing" ]

            Expect.equal result [ "existing" ] "Portable mode should not add a runtime ID"
        }

        test "should preserve arguments when the command does not support runtime selection" {
            let result = CommandArguments.withRuntimeIdentifier (Some (RuntimeIdentifier.create "linux-x64")) (Dotnet Restore) [ "existing" ]

            Expect.equal result [ "existing" ] "Unsupported commands should not receive a runtime ID"
        }

        test "should bundle the release when single-file publishing is enabled" {
            let result = ConsoleRelease.publishArguments true "./dist" "src/app.fsproj" Linux

            Expect.contains result "/p:PublishSingleFile=true" "Publish should carry the single-file property"
        }

        test "should leave the release unbundled when single-file publishing is disabled" {
            let result = ConsoleRelease.publishArguments false "./dist" "src/app.fsproj" Linux

            Expect.contains result "/p:PublishSingleFile=false" "Publish should carry the single-file property"
        }

        test "should publish each runtime into its own output and intermediate directory" {
            let result = ConsoleRelease.publishArguments true "./dist" "src/app.fsproj" OSXArm64

            Expect.equal
                result
                [
                    "-c"; "Release"
                    "/p:PublishSingleFile=true"
                    "/p:BaseIntermediateOutputPath=obj/osx-arm64/"
                    "-o"; "./dist/osx-arm64"
                    "--self-contained"
                    "-r"; "osx-arm64"
                    "src/app.fsproj"
                ]
                "Publish should name the runtime in its output path, intermediate path, and runtime ID"
        }

        test "should place the runtime ID inside each wrapped command when Mirrord is used" {
            let results =
                [ Dotnet Run; Dotnet WatchRun ]
                |> List.map (fun command -> CommandArguments.mirrordExecArguments (Some (RuntimeIdentifier.create "linux-x64")) command [])

            Expect.equal
                results
                [
                    [ "exec"; "--config-file"; ".mirrord/mirrord.json"; "--"; "dotnet"; "run"; "-p:AlmaBuildRuntimeIdentifier=linux-x64" ]
                    [ "exec"; "--config-file"; ".mirrord/mirrord.json"; "--"; "dotnet"; "watch"; "run"; "-p:AlmaBuildRuntimeIdentifier=linux-x64" ]
                ]
                "Mirrord should pass the runtime ID to Run and Watch"
        }
    ]

[<Tests>]
let fableSuitesTests =
    let suite environment = { Path = "tests/Shared"; Environment = environment; Include = [ "output/Shared.Tests.js" ] }

    testList "FableSuites" [
        test "should detect Fable.Mocha when the suite references it" {
            let project = Path.Combine (Path.GetTempPath (), $"fbuild-fable-suite-{Guid.NewGuid ():N}")
            Directory.CreateDirectory project |> ignore
            File.WriteAllLines (Path.Combine (project, "paket.references"), [ "FSharp.Core"; "Fable.Mocha" ])

            let result =
                try FableSuites.usesFableMocha project
                finally Directory.Delete (project, true)

            Expect.isTrue result "A Fable.Mocha reference should be reported before Vitest finds no tests"
        }

        test "should not detect Fable.Mocha when the suite uses Alma.Testing" {
            let project = Path.Combine (Path.GetTempPath (), $"fbuild-fable-suite-{Guid.NewGuid ():N}")
            Directory.CreateDirectory project |> ignore
            File.WriteAllLines (Path.Combine (project, "paket.references"), [ "FSharp.Core"; "Alma.Testing" ])

            let result =
                try FableSuites.usesFableMocha project
                finally Directory.Delete (project, true)

            Expect.isFalse result "Alma.Testing tests should pass the check"
        }

        test "should require only vitest when every suite runs in Node" {
            let result = FableSuites.npmPrerequisites [ suite VitestEnvironment.Node ]

            Expect.equal result [ "vitest" ] "A node suite needs no DOM package"
        }

        test "should require jsdom too when a suite runs in jsdom" {
            let result = FableSuites.npmPrerequisites [ suite VitestEnvironment.Node; suite VitestEnvironment.Jsdom ]

            Expect.equal result [ "vitest"; "jsdom" ] "A jsdom suite needs Vitest's DOM package"
        }

        test "should require nothing when there is no Fable suite" {
            let result = FableSuites.npmPrerequisites []

            Expect.isEmpty result "A spec without Fable suites does not touch npm"
        }

        test "should reset NODE_ENV to test when Fable runs Vitest" {
            let result = FableSuites.fableArguments [ "run" ]

            Expect.equal
                result
                [ "-o"; "output"; "-s"; "--noRestore"; "--run"; "env"; "NODE_ENV=test"; "npx"; "vitest"; "run" ]
                "Fable compiles the restored project into output, then Vitest runs under NODE_ENV=test"
        }
    ]

[<Tests>]
let dotNetSuiteTests =
    testList "DotNetSuite" [
        test "should skip the rebuild when the project is tested" {
            let result = DotNetSuite.testsArguments "tests/Tests.fsproj"

            Expect.equal result [ "--no-build"; "--project"; "tests/Tests.fsproj" ] "Build already compiled the tests"
        }

        test "should watch the project's directory when the suite names a project file" {
            let result = DotNetSuite.watchDirectory "tests/unit/unit.fsproj"

            Expect.equal result "tests/unit" "dotnet watch runs in the project's directory"
        }

        test "should watch the directory itself when the suite names a directory" {
            let directory = Path.GetTempPath ()

            let result = DotNetSuite.watchDirectory directory

            Expect.equal result directory "A directory suite is already the project's directory"
        }
    ]

[<Tests>]
let testAssemblyTests =
    testList "TestAssembly.name" [
        test "should use the project's file name when it declares no assembly name" {
            let result = TestAssembly.name "tests/Shared/Shared.Tests.fsproj" "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"

            Expect.equal result "Shared.Tests" "MSBuild names the assembly after the project file"
        }

        test "should use the declared assembly name when the project sets one" {
            let project = "<Project><PropertyGroup><AssemblyName>Alma.Build.Tests</AssemblyName></PropertyGroup></Project>"

            let result = TestAssembly.name "tests/unit/unit.fsproj" project

            Expect.equal result "Alma.Build.Tests" "InternalsVisibleTo must name the assembly the project produces"
        }

        test "should fall back to the file name when the declared assembly name is an MSBuild expression" {
            let project = "<Project><PropertyGroup><AssemblyName>$(MSBuildProjectName).Tests</AssemblyName></PropertyGroup></Project>"

            let result = TestAssembly.name "tests/unit/unit.fsproj" project

            Expect.equal result "unit" "An unevaluated property is not an assembly name"
        }
    ]
