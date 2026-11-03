module Alma.Build.Tests.TargetsTests

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
let changeLogReleaseTests =
    let parseVersion lines =
        lines
        |> ChangeLogRelease.parse
        |> Option.map (fun release -> release.AssemblyVersion)

    testList "ChangeLogRelease" [
        test "should read the latest released version" {
            let result =
                parseVersion [
                    "# Changelog"
                    ""
                    "## 1.2.3 - 2026-10-14"
                    "- Fix something"
                    ""
                    "## 1.2.2 - 2026-10-01"
                    "- Add something"
                ]

            Expect.equal result (Some "1.2.3") "Version should come from the topmost release"
        }

        test "should skip the Unreleased section header" {
            let result =
                parseVersion [
                    "# Changelog"
                    "## Unreleased"
                    "- Pending change"
                    ""
                    "## 1.2.3 - 2026-10-14"
                    "- Fix something"
                ]

            Expect.equal result (Some "1.2.3") "Unreleased should not be taken for a version"
        }

        test "should skip subsection headers" {
            let result =
                parseVersion [
                    "# Changelog"
                    "## Unreleased"
                    "### Add"
                    "- Pending change"
                    ""
                    "## 1.2.3 - 2026-10-14"
                    "### Fix"
                    "- Fix something"
                    "### Changed"
                    "- Change something"
                ]

            Expect.equal result (Some "1.2.3") "Subsection headers should not break version parsing"
        }

        test "should give no release when the change log holds none" {
            let result =
                parseVersion [
                    "# Changelog"
                    "## Unreleased"
                    "- Pending change"
                ]

            Expect.isNone result "A change log without a release should have no version"
        }

        test "should give no release for empty change log" {
            Expect.isNone (parseVersion []) "An empty change log should have no version"
        }
    ]
