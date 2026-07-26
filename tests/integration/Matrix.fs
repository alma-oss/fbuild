module Alma.Build.Tests.Integration.Matrix

open System.IO
open Expecto
open Helpers

// --------------------------------------------------------------------------------------------------------
// One scenario per `ProjectSpec` case. Each copies the checked-in fixture for that case — a complete
// consumer repo, `build/Build.fs` included — into a throwaway directory, then runs the spec's terminal
// release target through the engine's own entry point and asserts the artifacts it should have produced.
// Landing on the terminal target pulls the whole chain (AssemblyInfo, Build, Lint, Tests) with it.
// --------------------------------------------------------------------------------------------------------

let private scenario (fixture: string) (target: string) (assertArtifacts: string -> unit) =
    testCase fixture <| fun () ->
        withFixture fixture (fun dir ->
            execOk dir "dotnet" $"run --project build/build.fsproj -- {target}"

            assertArtifacts dir)

let private fileExists (dir: string) (relative: string) = File.Exists (Path.Combine (dir, relative))

let private anyFile (dir: string) (relative: string) (pattern: string) =
    let full = Path.Combine (dir, relative)
    Directory.Exists full && Directory.GetFiles(full, pattern).Length > 0

[<Tests>]
let matrix =
    testList "Integration matrix" [
        scenario "library" "Release" (fun dir ->
            Expect.isTrue (anyFile dir "release" "*.nupkg") "Release should move a .nupkg into release/")

        scenario "library-root" "Release" (fun dir ->
            Expect.isTrue
                (anyFile dir "release" "*.nupkg")
                "Release should move a .nupkg into release/ when the project shares the root with a solution file")

        scenario "executable" "Release" (fun dir ->
            Expect.isTrue
                (fileExists dir "app/test.executable.dll")
                "Release should publish the executable into app/")

        scenario "console" "Release" (fun dir ->
            Expect.isTrue
                (fileExists dir "dist/linux-x64.zip")
                "Release should publish and zip the linux-x64 runtime into dist/")

        scenario "safe" "Bundle" (fun dir ->
            Expect.isTrue (anyFile dir "deploy" "*.dll") "Bundle should publish the server into deploy/"
            Expect.isTrue
                (anyFile dir "deploy/public" "*.html")
                "Bundle should build the client bundle into deploy/public/")
    ]
