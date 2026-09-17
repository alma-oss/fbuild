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

let private fixtureRunTwiceTest setup target label assertArtifacts =
    testCase label <| fun () -> withFixtureRunTwice setup target assertArtifacts

let private editedFixtureTest setup edit target label assertArtifacts =
    testCase label <| fun () -> withEditedFixture setup edit target assertArtifacts

let private fileExists (dir: string) (relative: string) = File.Exists (Path.Combine (dir, relative))

let private anyFile (dir: string) (relative: string) (pattern: string) =
    let full = Path.Combine (dir, relative)
    Directory.Exists full && Directory.GetFiles(full, pattern).Length > 0

let private consoleRuntimeIdentifiers = [ "linux-x64"; "osx-x64"; "osx-arm64" ]

let private builtForRuntime (dir: string) (project: string) (assembly: string) =
    let output = Path.Combine (dir, project, "bin/Debug/net10.0")

    Directory.Exists output
    && Directory.GetDirectories output
       |> Array.exists (fun runtime -> File.Exists (Path.Combine (runtime, assembly)))

let private assertConsoleBuiltForRuntime dir =
    Expect.isTrue
        (builtForRuntime dir "src" "test.console.dll")
        "Build should compile the application into its runtime's output directory"

    Expect.isTrue
        (builtForRuntime dir "tests" "Tests.dll")
        "Build should compile the test project into its runtime's output directory"

    Expect.isFalse
        (fileExists dir "src/bin/Debug/net10.0/test.console.dll")
        "A configured runtime should keep the build out of the runtime-agnostic output"

[<Tests>]
let integrationTests =
    testList "Integration tests" [
        fixtureTest Library "Release" "should create a NuGet package when a library is released" (fun dir ->
            Expect.isTrue (anyFile dir "release" "*.nupkg") "Release should move a .nupkg into release/")

        fixtureTest LibraryRoot "Release" "should create a NuGet package when a root library shares its directory with a solution" (fun dir ->
            Expect.isTrue
                (anyFile dir "release" "*.nupkg")
                "Release should move a .nupkg into release/ when the project shares the root with a solution file")

        fixtureTest Library "Solution" "should generate a solution listing src and tests when none exists" (fun dir ->
            let generated = Path.Combine (dir, "test.library.slnx")
            Expect.isTrue (File.Exists generated) "Solution should write <Project.Name>.slnx when the repo root carries none"

            let content = File.ReadAllText generated
            Expect.stringContains content "src/test.library/test.library.fsproj" "The generated solution should list the library project"
            Expect.stringContains content "tests/Tests.fsproj" "The generated solution should list the test project"
            Expect.isFalse (content.Contains "build/build.fsproj") "The generated solution should not list the build harness")

        editedFixtureTest
            Library
            (fun dir -> File.WriteAllText (Path.Combine (dir, "test.library.slnx"), "<Solution></Solution>"))
            "Solution"
            "should overwrite a stale <Project.Name>.slnx with freshly generated content"
            (fun dir ->
                let content = File.ReadAllText (Path.Combine (dir, "test.library.slnx"))

                Expect.stringContains
                    content
                    "src/test.library/test.library.fsproj"
                    "Solution rewrites its own file with the current project list rather than leaving a stale one"
                Expect.isFalse (content = "<Solution></Solution>") "The stale placeholder content is replaced")

        fixtureTest LibraryRoot "Solution" "should leave a differently named checked-in solution alone and still generate its own" (fun dir ->
            let original = File.ReadAllText (Path.Combine (fixtures, "library-root", "fixture.rootlibrary.slnx"))
            let checkedIn = Path.Combine (dir, "fixture.rootlibrary.slnx")
            let generated = Path.Combine (dir, "test.rootlibrary.slnx")

            Expect.equal
                (File.ReadAllText checkedIn)
                original
                "Solution only ever writes its own <Project.Name>.slnx, leaving a differently named solution alone"
            Expect.isTrue (File.Exists generated) "Solution still generates <Project.Name>.slnx alongside it")

        fixtureRunTwiceTest Library "Solution" "should regenerate the same content when run a second time" (fun dir ->
            let generated = Path.Combine (dir, "test.library.slnx")

            Expect.isTrue (File.Exists generated) "Solution should still have the file it generated on the first run"
            Expect.stringContains
                (File.ReadAllText generated)
                "src/test.library/test.library.fsproj"
                "Rerunning Solution regenerates the same content rather than corrupting or duplicating it"
            Expect.equal (Directory.GetFiles(dir, "*.slnx").Length) 1 "Rerunning Solution does not create a second file")

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

        // The console fixture carries a solution, which `-r` cannot be passed to (`NETSDK1134`).
        fixtureTest Console "Build" "should build every project of a solution for the runtime"
            assertConsoleBuiltForRuntime

        // Without a solution the build runs once per project directory, so the runtime reaches each
        // project only through the root `Directory.Build.props` that Bootstrap deployed.
        editedFixtureTest
            Console
            (fun dir ->
                let solution = Path.Combine (dir, "fixture.console.slnx")
                Expect.isTrue (File.Exists solution) "The console fixture should carry the solution this case removes"
                File.Delete solution)
            "Build"
            "should build every project for the runtime without a solution"
            assertConsoleBuiltForRuntime

        fixtureTest Console "Bootstrap" "should deploy the runtime props when a console application is bootstrapped" (fun dir ->
            Expect.isTrue
                (fileExists dir "Directory.Build.props")
                "Bootstrap should deploy the props that carry the runtime into each project")

        fixtureTest Library "Bootstrap" "should skip the runtime props when a library is bootstrapped" (fun dir ->
            Expect.isFalse
                (fileExists dir "Directory.Build.props")
                "A spec that selects no runtime should not receive the props")

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

                let manifest = File.ReadAllText (Path.Combine (dir, ".config", "dotnet-tools.json"))

                Expect.stringContains manifest "\"isRoot\": true" "Bootstrap should render a root tools manifest"
                Expect.stringContains manifest "\"paket\"" "The rendered manifest should pin Paket"
                Expect.stringContains manifest "\"dotnet-fsharplint\"" "The rendered manifest should pin the linter"
                Expect.isFalse (manifest.Contains "\"fable\"") "A library should not get the SAFE-stack tools"
                Expect.isTrue
                    (anyFile dir "release" "*.nupkg")
                    "Release should move a .nupkg into release/ when the engine is consumed as a package")
    ]
