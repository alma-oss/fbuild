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
            | RaspberryPiHassioAddon -> "alpine.3.16-arm64"
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
    module Solution =
        /// `.slnx` wins over `.sln`; `None` when the candidates hold neither.
        let pick (candidates: seq<string>): string option =
            let firstWith extension =
                candidates
                |> Seq.filter (fun candidate -> IO.Path.GetExtension(candidate).ToLowerInvariant () = extension)
                |> Seq.tryHead

            firstWith ".slnx" |> Option.orElse (firstWith ".sln")

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
        type IProjectSources =
            abstract member Sources: IGlobbingPattern
            abstract member Tests: IGlobbingPattern
            abstract member All: IGlobbingPattern

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

        and LibrarySpec =
            {
                Changelog: string
                ReleaseDir: string
                LibrarySources: IGlobbingPattern
                TestsSources: IGlobbingPattern
                AllSources: IGlobbingPattern
                /// Organization (it is used for a custom github nuget source)
                Organization: string option
                /// Configuration for nuget api, to push packages into
                NugetApi: NugetApi
                /// Repository for custom nuget server, it will be triggered for a readme update
                NugetCustomServerRepository: string option
            }

            interface IProjectSources with
                member this.Sources = this.LibrarySources
                member this.Tests = this.TestsSources
                member this.All = this.AllSources

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
                AllSources: IGlobbingPattern
            }

            interface IProjectSources with
                member this.Sources = this.ApplicationSources
                member this.Tests = this.TestsSources
                member this.All = this.AllSources

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
                AllSources: IGlobbingPattern
            }

            interface IProjectSources with
                member this.Sources = this.ApplicationSources
                member this.Tests = this.TestsSources
                member this.All = this.AllSources

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
                AllSources: IGlobbingPattern
            }

            interface IProjectSources with
                member this.Sources = this.ReleaseSources
                member this.Tests = this.TestsSources
                member this.All = this.AllSources

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
            let defaultLibrary: ProjectSpec =
                let sources =
                    !! "./*.fsproj"
                    ++ "src/*.fsproj"
                    ++ "src/**/*.fsproj"

                Library {
                    Changelog = "CHANGELOG.md"
                    ReleaseDir = "release"
                    LibrarySources = sources
                    TestsSources = !! "tests/*.fsproj"
                    AllSources =
                        sources
                        ++ "tests/*.fsproj"
                        ++ "build/*.fsproj"
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
                    AllSources =
                        release
                        ++ "tests/**/*.fsproj"
                        ++ "build/*.fsproj"
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
                    AllSources =
                        sources
                        ++ "tests/*.fsproj"
                        ++ "build/*.fsproj"
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
                    AllSources = release ++ "tests/**/*.fsproj"
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
