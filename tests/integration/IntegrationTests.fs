module Alma.Build.Tests.Integration.IntegrationTests

open System.IO
open System.IO.Compression
open Expecto
open Utils

// --------------------------------------------------------------------------------------------------------
// Each scenario copies its checked-in fixture — a complete consumer repo, `build/Build.fs` included —
// into a throwaway directory, then runs the requested target through the engine's own entry point and
// asserts its behavior. Landing on a terminal target pulls its whole dependency chain with it.
// --------------------------------------------------------------------------------------------------------

let private fixtureTest setup target label assertArtifacts =
    testCase label <| fun () -> withFixture setup target assertArtifacts

let private fileExists (dir: string) (relative: string) = File.Exists (Path.Combine (dir, relative))

let private anyFile (dir: string) (relative: string) (pattern: string) =
    let full = Path.Combine (dir, relative)
    Directory.Exists full && Directory.GetFiles(full, pattern).Length > 0

let private consoleRuntimeIdentifiers = [ "linux-x64"; "osx-x64"; "osx-arm64" ]

[<Tests>]
let integrationTests =
    testList "Integration tests" [
        fixtureTest Library "Release" "should create a NuGet package when a library is released" (fun dir ->
            Expect.isTrue (anyFile dir "release" "*.nupkg") "Release should move a .nupkg into release/")

        fixtureTest LibraryRoot "Release" "should create a NuGet package when a root library shares its directory with a solution" (fun dir ->
            Expect.isTrue
                (anyFile dir "release" "*.nupkg")
                "Release should move a .nupkg into release/ when the project shares the root with a solution file")

        fixtureTest Executable "Release" "should publish the executable when an executable application is released" (fun dir ->
            Expect.isTrue
                (fileExists dir "app/test.executable.dll")
                "Release should publish the executable into app/")

        fixtureTest Console "Release" "should archive every configured runtime when a console application is released" (fun dir ->
            consoleRuntimeIdentifiers
            |> List.iter (fun runtimeIdentifier ->
                let archivePath = Path.Combine (dir, $"dist/{runtimeIdentifier}.zip")

                Expect.isTrue
                    (File.Exists archivePath)
                    $"Release should publish and zip {runtimeIdentifier} into dist/"

                use archive = ZipFile.OpenRead archivePath
                Expect.isTrue
                    (archive.Entries |> Seq.exists (fun entry -> entry.FullName.EndsWith "/test.console"))
                    $"Bundled {runtimeIdentifier} release should include the application executable"))

        fixtureTest Safe "Bundle" "should publish the server and client when a SAFE application is bundled" (fun dir ->
            Expect.isTrue (anyFile dir "deploy" "*.dll") "Bundle should publish the server into deploy/"
            Expect.isTrue
                (anyFile dir "deploy/public" "*.html")
                "Bundle should build the client bundle into deploy/public/")

        testCase "should release a library through the packaged engine" <| fun () ->
            withPackagedFixture Library "Release" (fun dir ->
                Expect.isTrue
                    (fileExists dir "build/README.md")
                    "Bootstrap should deploy the bundled files with their directory structure"
                Expect.isTrue
                    (anyFile dir "release" "*.nupkg")
                    "Release should move a .nupkg into release/ when the engine is consumed as a package")
    ]
