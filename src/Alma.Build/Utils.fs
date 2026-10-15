namespace Alma.Build

module Utils =
    open System
    open System.IO
    open System.Runtime.InteropServices

    open Fake.Core
    open Fake.DotNet
    open Fake.IO
    open Fake.IO.FileSystemOperators
    open Fake.IO.Globbing.Operators
    open Fake.Core.TargetOperators
    open Fake.Tools.Git

    open Command

    [<RequireQualifiedAccess>]
    module Args =
        let init args =
            args
            |> Array.toList
            |> Context.FakeExecutionContext.Create false "build.fsx"
            |> Context.RuntimeContext.Fake
            |> Context.setExecutionContext

            CompactTrace.install ()

        let defaultTarget = "Build"

        let run args =
            let runTarget () =
                match args |> Array.toList with
                | []
                | [ "-t" ] -> Target.run 1 defaultTarget []
                | "-t" :: target :: buildArguments
                | target :: buildArguments -> Target.run 1 target buildArguments

            try
                runTarget ()
                0
            // CompactTrace mutes the Build Time Report and its failure status, so under RTK the
            // message is all that is left to report; otherwise the inner detail is worth keeping.
            with :? BuildFailedException as ex ->
                eprintfn "%s" (if isRtkActive then ex.Message else string ex)
                1

    let tee f a =
        f a
        a

    let skipOn option action p =
        if p.Context.Arguments |> Seq.contains option
        then Trace.tracefn "Skipped ..."
        else action p

    let stringToOption = function
        | null | "" -> None
        | string -> Some string

    let envVar name =
        if Environment.hasEnvironVar name
            then Environment.environVar name |> stringToOption
            else None

    [<RequireQualifiedAccess>]
    module Option =
        let bindNone f = function
            | Some v -> Some v
            | None -> f None

        let requireSome error = function
            | Some v -> v
            | None -> failwith error

    type RuntimeIdentifier = private RuntimeIdentifier of string

    [<RequireQualifiedAccess>]
    module RuntimeIdentifier =
        let internal create value = RuntimeIdentifier value
        let value (RuntimeIdentifier value) = value

    type RuntimeTarget =
        | OSX
        | OSXArm64
        | Windows
        | Linux
        | ArmLinux
        | AlpineLinux
        | RaspberryPiHassioAddon
        | Custom of string

    [<RequireQualifiedAccess>]
    module RuntimeTarget =
        /// Runtime IDs: https://docs.microsoft.com/en-us/dotnet/core/rid-catalog#known-rids
        let value = function
            | OSX -> "osx-x64"
            | OSXArm64 -> "osx-arm64"
            | Windows -> "win-x64"
            | Linux -> "linux-x64"
            | ArmLinux -> "linux-arm64"
            | AlpineLinux -> "linux-musl-x64"
            | RaspberryPiHassioAddon -> "linux-musl-arm64"
            | Custom value -> value

        let toRuntimeIdentifier runtimeTarget =
            runtimeTarget |> value |> RuntimeIdentifier.create

        let detect () =
            let isMusl = RuntimeInformation.RuntimeIdentifier.Contains "musl"

            match RuntimeInformation.OSArchitecture with
            | Architecture.X64 when RuntimeInformation.IsOSPlatform OSPlatform.Windows -> Windows
            | Architecture.X64 when RuntimeInformation.IsOSPlatform OSPlatform.OSX -> OSX
            | Architecture.Arm64 when RuntimeInformation.IsOSPlatform OSPlatform.OSX -> OSXArm64
            | Architecture.X64 when RuntimeInformation.IsOSPlatform OSPlatform.Linux && isMusl -> AlpineLinux
            | Architecture.X64 when RuntimeInformation.IsOSPlatform OSPlatform.Linux -> Linux
            | Architecture.Arm64 when RuntimeInformation.IsOSPlatform OSPlatform.Linux -> ArmLinux
            | _ -> Custom RuntimeInformation.RuntimeIdentifier

    [<RequireQualifiedAccess>]
    type RuntimeMode =
        | Portable
        | AutoDetect
        | Specific of RuntimeTarget

    [<RequireQualifiedAccess>]
    module RuntimeMode =
        let internal runtimeIdentifier = function
            | RuntimeMode.Portable -> None
            | RuntimeMode.AutoDetect -> Some (RuntimeTarget.detect () |> RuntimeTarget.toRuntimeIdentifier)
            | RuntimeMode.Specific runtimeTarget -> Some (RuntimeTarget.toRuntimeIdentifier runtimeTarget)

    type RuntimeConfiguration = {
        RuntimeTargets: RuntimeTarget list
        RuntimeMode: RuntimeMode
    }

    [<RequireQualifiedAccess>]
    type internal RuntimeConfigurationError =
        | UnsupportedRuntime of RuntimeIdentifier

    [<RequireQualifiedAccess>]
    module internal RuntimeConfigurationError =
        let format = function
            | RuntimeConfigurationError.UnsupportedRuntime runtimeIdentifier ->
                runtimeIdentifier
                |> RuntimeIdentifier.value
                |> sprintf "Build runtime %s is not supported."

    [<RequireQualifiedAccess>]
    module internal RuntimeConfiguration =
        let resolve configuration =
            match configuration.RuntimeMode |> RuntimeMode.runtimeIdentifier with
            | None -> Ok None
            | Some selectedRuntimeIdentifier ->
                configuration.RuntimeTargets
                |> List.exists (RuntimeTarget.toRuntimeIdentifier >> (=) selectedRuntimeIdentifier)
                |> function
                    | true -> Ok (Some selectedRuntimeIdentifier)
                    | false -> Error (RuntimeConfigurationError.UnsupportedRuntime selectedRuntimeIdentifier)

    [<RequireQualifiedAccess>]
    type SolutionPickError =
        | Ambiguous of candidates: string list

    [<RequireQualifiedAccess>]
    module SolutionPickError =
        let format = function
            | SolutionPickError.Ambiguous candidates ->
                candidates
                |> String.concat ", "
                |> sprintf "Found several solutions (%s); keep only one committed so the build knows which to use."

    [<RequireQualifiedAccess>]
    type SolutionFileNameError =
        | NothingUsable of projectName: string

    [<RequireQualifiedAccess>]
    module SolutionFileNameError =
        let format = function
            | SolutionFileNameError.NothingUsable projectName ->
                sprintf "Project name %A leaves no usable solution file name once whitespace and path-unsafe characters are dropped." projectName

    /// A `.slnx` file name derived from a project name, safe to check out on any OS.
    type SolutionFileName = private SolutionFileName of string

    [<RequireQualifiedAccess>]
    module SolutionFileName =
        // Fixed across OSes: the generated name must also check out on Windows, whichever OS ran the target.
        let private unsafeChars = set [ '/'; '\\'; ':'; '*'; '?'; '"'; '<'; '>'; '|' ]

        /// `<projectName>.slnx` with whitespace, control and path-unsafe characters dropped and trailing dots trimmed.
        let create (projectName: string): Result<SolutionFileName, SolutionFileNameError> =
            projectName
            |> String.filter (fun c -> not (Char.IsWhiteSpace c || Char.IsControl c || unsafeChars.Contains c))
            |> fun name -> name.TrimEnd '.'
            |> function
                | "" -> Error (SolutionFileNameError.NothingUsable projectName)
                | name -> Ok (SolutionFileName (sprintf "%s.slnx" name))

        let value (SolutionFileName fileName) = fileName

    [<RequireQualifiedAccess>]
    module Solution =
        open System.Xml.Linq

        /// The single `.slnx`/`.sln` among the candidates; `Ok None` when there is none.
        let pick (candidates: seq<string>): Result<string option, SolutionPickError> =
            let isSolution (candidate: string) =
                [ ".slnx"; ".sln" ] |> List.contains (IO.Path.GetExtension(candidate).ToLowerInvariant ())

            match candidates |> Seq.filter isSolution |> Seq.toList with
            | [] -> Ok None
            | [ solution ] -> Ok (Some solution)
            | several -> Error (SolutionPickError.Ambiguous several)

        /// Every `.slnx`/`.sln` sitting at the current directory.
        let internal all () = !! "*.slnx" ++ "*.sln" |> Seq.toList

        /// The solution sitting at the current directory, if any.
        let internal current () = all () |> pick

        /// Forward-slashed and relative to `root`, which a path outside it reaches back into with `../`.
        let internal relativeTo (root: string) (path: string): string =
            let forwardSlashed (value: string) = value.Replace('\\', '/')

            IO.Path.GetRelativePath(forwardSlashed root, forwardSlashed path) |> forwardSlashed

        /// `.slnx` contents for the given forward-slashed relative project paths; `None` when there are none.
        let private sameContents (expected: string) (actual: string): bool =
            let normalized (value: string) = value.Replace("\r\n", "\n").TrimEnd ()

            normalized expected = normalized actual

        /// Splits `(path, contents)` pairs into those matching `rendered`, ignoring line endings and trailing whitespace, and the rest.
        let internal partitionSuperseded (rendered: string) (others: (string * string) list): string list * string list =
            let superseded, differing = others |> List.partition (snd >> sameContents rendered)

            superseded |> List.map fst, differing |> List.map fst

        let internal render (projects: seq<string>): string option =
            let topLevelDirectory (path: string) =
                match path.IndexOf '/' with
                | -1 -> None
                | index -> Some (path.Substring(0, index))

            let projectElement path = XElement(XName.Get "Project", XAttribute(XName.Get "Path", path))

            let folderElement name paths =
                XElement(
                    XName.Get "Folder",
                    XAttribute(XName.Get "Name", sprintf "/%s/" name),
                    paths |> List.sort |> List.map projectElement
                )

            let projects = projects |> Seq.distinct |> Seq.toList

            let folders =
                projects
                |> List.choose (fun path -> path |> topLevelDirectory |> Option.map (fun directory -> directory, path))
                |> List.groupBy fst
                |> List.sortBy fst
                |> List.map (fun (directory, pairs) -> folderElement directory (pairs |> List.map snd))

            let rootProjects =
                projects
                |> List.filter (topLevelDirectory >> Option.isNone)
                |> List.sort
                |> List.map projectElement

            match folders @ rootProjects with
            | [] -> None
            | children -> Some (XElement(XName.Get "Solution", children).ToString() + "\n")

        /// Writes `fileName` at `root` from `projects`, deleting other root solutions with the same contents and warning about the rest.
        let internal generate (root: string) (fileName: SolutionFileName) (projects: seq<string>): unit =
            let path = fileName |> SolutionFileName.value
            let rendered = projects |> Seq.map (relativeTo root) |> render
            let others = all () |> List.filter (fun other -> Path.GetFullPath other <> Path.GetFullPath path)

            let superseded, differing =
                match rendered with
                | Some content -> others |> List.map (fun other -> other, File.ReadAllText other) |> partitionSuperseded content
                | None -> [], others

            // Deleted before writing: on a case-insensitive filesystem a superseded name differing only in case is the target file itself.
            superseded
            |> List.iter (fun other ->
                File.Delete other
                Trace.tracefn "Deleted %s, superseded by %s" (relativeTo root other) path
            )

            match differing with
            | [] -> ()
            | others ->
                Trace.traceImportantfn
                    "%s already sits at the repo root alongside %s; keep only one committed, since Build fails on more than one solution."
                    (others |> List.map (relativeTo root) |> String.concat ", ")
                    path

            match rendered with
            | Some content ->
                File.WriteAllText(path, content)
                Trace.tracefn "Wrote %s" path
            | None -> Trace.tracefn "No projects to render into a solution; nothing written."

    [<RequireQualifiedAccess>]
    module Nuget =
        let push releaseDir organization token =
            let sourceName =
                organization
                |> Option.map (fun organization ->
                    let sourceName = "github"

                    Trace.tracefn "[Nuget] Add organization %A as a source" organization

                    runInRoot (Nuget AddSource) [
                        "--username"; organization
                        "--password"; token
                        "--store-password-in-clear-text"
                        "--name"; sourceName
                        sprintf "https://nuget.pkg.github.com/%s/index.json" organization
                    ]

                    sourceName
                )

            Trace.tracefn "[Nuget] Push packages"

            runInRoot (Nuget Push) [
                releaseDir </> "*.nupkg"
                sprintf "--source=%s" (sourceName |> Option.defaultValue "https://api.nuget.org/v3/index.json")
                sprintf "--api-key=%s" token
                "--skip-duplicate"
            ]

    [<AutoOpen>]
    module ProjectDefinition =
        [<RequireQualifiedAccess>]
        type VitestEnvironment =
            | Node
            | Jsdom

        [<RequireQualifiedAccess>]
        type TestSuite =
            | DotNet of project: string
            | Fable of FableTestSuite

        and FableTestSuite = {
            Path: string
            Environment: VitestEnvironment
            /// Vitest test files, relative to `Path`: the compiled entry module registering the suite's test list.
            Include: string list
        }

        [<RequireQualifiedAccess>]
        module TestSuite =
            let fableSuites: TestSuite list -> FableTestSuite list =
                List.choose (function
                    | TestSuite.Fable suite -> Some suite
                    | TestSuite.DotNet _ -> None
                )

        [<RequireQualifiedAccess>]
        type LibraryTests =
            /// A library without tests.
            | NoTests
            /// .NET test projects.
            | DotNet of IGlobbingPattern
            /// `tests/Shared` runs on .NET and, compiled by Fable, under Vitest; `tests/DotNet` holds what only runs on .NET.
            | Shared of SharedTests
            /// `tests/Fable` is a Vitest suite for a library that only exists under Fable.
            | Fable of FableTests

        /// Full paths of a shared library's test directories.
        and SharedTests = {
            SharedPath: string
            DotNetPath: string
        }

        /// Full path of a Fable-only library's test directory.
        and FableTests = {
            FablePath: string
        }

        [<RequireQualifiedAccess>]
        type internal LibraryTestsError =
            | NoTestProjects of includes: string list
            | MissingTestProjects of directories: string list

        [<RequireQualifiedAccess>]
        module internal LibraryTestsError =
            let format = function
                | LibraryTestsError.NoTestProjects includes ->
                    includes
                    |> String.concat ", "
                    |> sprintf "No test project matches %s; point the library's Tests at its test projects, or set LibraryTests.noTests."
                | LibraryTestsError.MissingTestProjects directories ->
                    directories
                    |> String.concat ", "
                    |> sprintf "No *.fsproj in %s; the library's Tests expect a test project there."

        [<RequireQualifiedAccess>]
        module LibraryTests =
            let noTests = LibraryTests.NoTests

            /// `tests/*.fsproj`.
            let dotNet = LibraryTests.DotNet (!! "tests/*.fsproj")

            let internal sharedAt (root: string) =
                LibraryTests.Shared {
                    SharedPath = Path.getFullName (root </> "tests" </> "Shared")
                    DotNetPath = Path.getFullName (root </> "tests" </> "DotNet")
                }

            /// `tests/Shared` and `tests/DotNet`.
            let shared = sharedAt "."

            let internal fableAt (root: string) =
                LibraryTests.Fable { FablePath = Path.getFullName (root </> "tests" </> "Fable") }

            /// `tests/Fable`.
            let fable = fableAt "."

            let private missingProjects (directories: string list) =
                directories
                |> List.filter (fun path -> !! (path </> "*.fsproj") |> Seq.isEmpty)
                |> function
                    | [] -> Ok ()
                    | missing -> Error (LibraryTestsError.MissingTestProjects missing)

            let internal validate = function
                | LibraryTests.NoTests -> Ok ()
                | LibraryTests.DotNet projects when projects |> Seq.isEmpty ->
                    Error (LibraryTestsError.NoTestProjects (projects.Includes |> List.ofSeq))
                | LibraryTests.DotNet _ -> Ok ()
                | LibraryTests.Shared tests -> missingProjects [ tests.SharedPath; tests.DotNetPath ]
                | LibraryTests.Fable tests -> missingProjects [ tests.FablePath ]

            /// Raises on an invalid configuration.
            let internal valid (tests: LibraryTests) =
                match validate tests with
                | Ok () -> tests
                | Error error -> error |> LibraryTestsError.format |> failwith

            let internal projects = function
                | LibraryTests.NoTests -> !! "tests/*.fsproj" -- "**/*.fsproj"
                | LibraryTests.DotNet projects -> projects
                | LibraryTests.Shared tests -> !! (tests.SharedPath </> "*.fsproj") ++ (tests.DotNetPath </> "*.fsproj")
                | LibraryTests.Fable tests -> !! (tests.FablePath </> "*.fsproj")

            let private fableSuite environment path =
                TestSuite.Fable {
                    Path = path
                    Environment = environment
                    Include = [ IO.Path.GetFileName (path: string) |> sprintf "output/%s.Tests.js" ]
                }

            let internal suites = function
                | LibraryTests.NoTests -> []
                | LibraryTests.DotNet projects -> projects |> Seq.map TestSuite.DotNet |> List.ofSeq
                | LibraryTests.Shared tests ->
                    [
                        TestSuite.DotNet tests.DotNetPath
                        fableSuite VitestEnvironment.Node tests.SharedPath
                    ]
                | LibraryTests.Fable tests -> [ fableSuite VitestEnvironment.Jsdom tests.FablePath ]

        type IProjectSources =
            abstract member Sources: IGlobbingPattern
            abstract member Tests: IGlobbingPattern

        type IProjectSources with
            /// Every source and test project.
            member this.Build: seq<string> = Seq.append this.Sources this.Tests |> Seq.distinct

        type ProjectDefinition =
            {
                Project: ProjectMeta
                Specs: ProjectSpec
            }

            with
                member this.Sources =
                    match this with
                    | { Specs = Library app } -> app :> IProjectSources
                    | { Specs = Executable app } -> app  :> IProjectSources
                    | { Specs = ConsoleApplication app } -> app :> IProjectSources
                    | { Specs = SAFEStackApplication app } -> app  :> IProjectSources

                member this.ChangeLog =
                    match this with
                    | { Specs = Library { Changelog = changeLog } } -> Some changeLog
                    | { Specs = Executable { Changelog = changeLog } }
                    | { Specs = ConsoleApplication { Changelog = changeLog } }
                    | { Specs = SAFEStackApplication { Changelog = changeLog } } -> changeLog

        and ProjectMeta = {
            Name: string
            Summary: string
            Git: Git option
        }

        and Git = {
            Commit: string
            Branch: string
            Repository: string option
        }

        and ProjectSpec =
            | Library of LibrarySpec
            | Executable of ExecutableSpec
            | ConsoleApplication of ConsoleApplicationSpec
            | SAFEStackApplication of SAFEStackApplicationSpec

            with
                member this.Type =
                    match this with
                    | Library _ -> "Library"
                    | Executable _ -> "Executable"
                    | ConsoleApplication _ -> "Console Application"
                    | SAFEStackApplication _ -> "SAFE-Stack Application"

                member this.IsSAFEStack =
                    match this with
                    | SAFEStackApplication _ -> true
                    | _ -> false

                member this.RuntimeConfiguration =
                    match this with
                    | ConsoleApplication spec ->
                        Some {
                            RuntimeTargets = spec.RuntimeTargets
                            RuntimeMode = spec.RuntimeMode
                        }
                    | Library _
                    | Executable _
                    | SAFEStackApplication _ -> None

                member this.TestSuites: TestSuite list =
                    match this with
                    | Library spec -> spec.Tests |> LibraryTests.valid |> LibraryTests.suites
                    | Executable { TestsSources = projects }
                    | ConsoleApplication { TestsSources = projects } -> projects |> Seq.map TestSuite.DotNet |> List.ofSeq
                    | SAFEStackApplication spec ->
                        [
                            TestSuite.DotNet spec.ServerTestsPath
                            TestSuite.Fable {
                                Path = spec.ClientTestsPath
                                Environment = VitestEnvironment.Jsdom
                                Include = [ "output/Client.Tests.js" ]
                            }
                        ]

                member this.FableTestSuites = this.TestSuites |> TestSuite.fableSuites

        and LibrarySpec =
            {
                Changelog: string
                ReleaseDir: string
                LibrarySources: IGlobbingPattern
                Tests: LibraryTests
                /// Organization (it is used for a custom github nuget source)
                Organization: string option
                /// Configuration for nuget api, to push packages into
                NugetApi: NugetApi
                /// Repository for custom nuget server, it will be triggered for a readme update
                NugetCustomServerRepository: string option
            }

            interface IProjectSources with
                member this.Sources = this.LibrarySources
                member this.Tests = this.Tests |> LibraryTests.valid |> LibraryTests.projects

        and [<RequireQualifiedAccess>] NugetApi =
            | NotUsed
            | AskForKey
            | Organization of name: string
            | KeyInEnvironment of string

        and ExecutableSpec =
            {
                Changelog: string option
                ReleaseDir: string
                ApplicationSources: IGlobbingPattern
                TestsSources: IGlobbingPattern
            }

            interface IProjectSources with
                member this.Sources = this.ApplicationSources
                member this.Tests = this.TestsSources

        and ConsoleApplicationSpec =
            {
                Changelog: string option
                ReleaseDir: string
                RuntimeTargets: RuntimeTarget list
                RuntimeMode: RuntimeMode
                PublishSingleFile: bool
                ReleaseSource: string
                ApplicationSources: IGlobbingPattern
                TestsSources: IGlobbingPattern
            }

            interface IProjectSources with
                member this.Sources = this.ApplicationSources
                member this.Tests = this.TestsSources

        and SAFEStackApplicationSpec =
            {
                Changelog: string option
                TemplateVersion: string

                SharedPath: string
                ServerPath: string
                ClientPath: string

                DeployPath: string

                SharedTestsPath: string
                ServerTestsPath: string
                ClientTestsPath: string

                ReleaseSources: IGlobbingPattern
                TestsSources: IGlobbingPattern
            }

            interface IProjectSources with
                member this.Sources = this.ReleaseSources
                member this.Tests = this.TestsSources

        [<RequireQualifiedAccess>]
        module Git =
            let init () =
                Some {
                    Commit = Information.getCurrentSHA1(".")
                    Branch = Information.getBranchName(".")
                    Repository = None
                }

        [<RequireQualifiedAccess>]
        module Spec =
            let library (tests: LibraryTests): ProjectSpec =
                let sources =
                    !! "./*.fsproj"
                    ++ "src/*.fsproj"
                    ++ "src/**/*.fsproj"

                Library {
                    Changelog = "CHANGELOG.md"
                    ReleaseDir = "release"
                    LibrarySources = sources
                    Tests = tests
                    Organization = None
                    NugetApi = NugetApi.NotUsed
                    NugetCustomServerRepository = None
                }

            let defaultExecutable: ProjectSpec =
                let release =
                    !! "./*.fsproj"
                    ++ "src/**/*.fsproj"

                Executable {
                    Changelog = if File.Exists "CHANGELOG.md" then Some "CHANGELOG.md" else None
                    ReleaseDir = "/app"

                    ApplicationSources = release
                    TestsSources = !! "tests/**/*.fsproj"
                }

            let defaultConsoleApplication runtimeTargets: ProjectSpec =
                let sources =
                    !! "./*.fsproj"
                    ++ "src/*.fsproj"
                    ++ "src/**/*.fsproj"

                ConsoleApplication {
                    Changelog = if File.Exists "CHANGELOG.md" then Some "CHANGELOG.md" else None
                    ReleaseDir = "./dist"
                    RuntimeTargets = runtimeTargets
                    RuntimeMode = RuntimeMode.Portable
                    PublishSingleFile = true

                    ApplicationSources = sources
                    ReleaseSource = sources |> Seq.head
                    TestsSources = !! "tests/*.fsproj"
                }

            let defaultSAFEStackApplication templateVersion: ProjectSpec =
                let release = !! "src/**/*.fsproj"

                SAFEStackApplication {
                    Changelog = if File.Exists "CHANGELOG.md" then Some "CHANGELOG.md" else None
                    TemplateVersion = templateVersion

                    SharedPath = Path.getFullName ("src" </> "Shared")
                    ServerPath = Path.getFullName ("src" </> "Server")
                    ClientPath = Path.getFullName ("src" </> "Client")

                    DeployPath = Path.getFullName "deploy"

                    SharedTestsPath = Path.getFullName ("tests" </> "Shared")
                    ServerTestsPath = Path.getFullName ("tests" </> "Server")
                    ClientTestsPath = Path.getFullName ("tests" </> "Client")

                    ReleaseSources = release
                    TestsSources = !! "tests/**/*.fsproj"
                }

            let mapLibrary f = function
                | Library spec -> f spec |> Library
                | spec -> spec

            let mapExecutable f = function
                | Executable spec -> f spec |> Executable
                | spec -> spec

            let mapConsoleApplication f = function
                | ConsoleApplication spec -> f spec |> ConsoleApplication
                | spec -> spec

            let mapSAFEStackApplication f = function
                | SAFEStackApplication spec -> f spec |> SAFEStackApplication
                | spec -> spec

    [<RequireQualifiedAccess>]
    module Http =
        open System.Net.Http
        open System.Net.Http.Headers

        let post (currentProject: string) token (url: string) (data: string) = async {
            use client = new HttpClient()

            let requestHeaders = client.DefaultRequestHeaders
            requestHeaders.Authorization <- new AuthenticationHeaderValue("Bearer", token)
            requestHeaders.Add("User-Agent", sprintf "Fake.Build/%s" currentProject)

            use request = new StringContent(data, Text.Encoding.UTF8)
            request.Headers.ContentType <- new MediaTypeHeaderValue("application/json")

            let! response = client.PostAsync(url, request) |> Async.AwaitTask
            response.EnsureSuccessStatusCode() |> ignore

            let headers =
                response.Headers :> seq<Collections.Generic.KeyValuePair<string, seq<string>>>
                |> Seq.append (
                    response.Content.Headers :> seq<Collections.Generic.KeyValuePair<string, seq<string>>>
                )
                |> Seq.map (fun kv -> kv.Key, kv.Value |> Seq.toList)
                |> Map.ofSeq

            use! stream = response.Content.ReadAsStreamAsync() |> Async.AwaitTask
            use reader = new StreamReader(stream)

            return headers, reader.ReadToEnd()
        }

    [<RequireQualifiedAccess>]
    module Github =
        [<AutoOpen>]
        module Types =
            type TriggerAction = {
                CurrentProject: string
                Token: string
                Organization: string
                Repository: string
                EventType: string
            }

        let triggerAction { CurrentProject = current; Token = token; Organization = org; Repository = repo; EventType = event } = async {
            let url =
                $"https://api.github.com/repos/{org}/{repo}/dispatches"
                |> tee (Trace.tracefn "Github.Trigger<%s>: %s" event)

            let data = sprintf @"{""event_type"":""%s"",""client_payload"":{""from"": ""%s""}}" event current
            let! _ = Http.post current token url data

            return ()
        }
