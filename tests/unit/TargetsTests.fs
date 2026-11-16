module Alma.Build.Tests.TargetsTests

open System

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
let cleanTests =
    let withTree (directories: string list) (gitMarkers: string list) assertTree =
        let root = IO.Path.Combine (IO.Path.GetTempPath (), $"fbuild-clean-{Guid.NewGuid ():N}")

        try
            directories |> List.iter (fun directory -> IO.Directory.CreateDirectory (IO.Path.Combine (root, directory)) |> ignore)
            gitMarkers |> List.iter (fun marker -> IO.File.WriteAllText (IO.Path.Combine (root, marker), "gitdir: elsewhere"))
            assertTree root
        finally
            IO.Directory.Delete (root, true)

    testList "Clean.isInNestedWorkingTree" [
        test "should keep the repository's own outputs in scope" {
            withTree [ "src/app/obj" ] [ ".git" ] (fun root ->
                Expect.isFalse
                    (Clean.isInNestedWorkingTree root (IO.Path.Combine (root, "src/app/obj")))
                    "The root's own .git does not make its outputs nested")
        }

        test "should leave a nested worktree's outputs alone" {
            withTree [ ".worktrees/feature/tests/unit/obj" ] [ ".git"; ".worktrees/feature/.git" ] (fun root ->
                Expect.isTrue
                    (Clean.isInNestedWorkingTree root (IO.Path.Combine (root, ".worktrees/feature/tests/unit/obj")))
                    "A worktree's .git file marks its outputs as belonging to it")
        }

        test "should leave a nested repository's outputs alone" {
            withTree [ "vendor/lib/.git"; "vendor/lib/obj" ] [] (fun root ->
                Expect.isTrue
                    (Clean.isInNestedWorkingTree root (IO.Path.Combine (root, "vendor/lib/obj")))
                    "A nested repository's .git directory marks its outputs as belonging to it")
        }
    ]
