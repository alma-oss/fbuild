namespace Alma.Build

module Targets =
    open System
    open System.IO

    open Fake.Core
    open Fake.DotNet
    open Fake.IO
    open Fake.IO.FileSystemOperators
    open Fake.IO.Globbing.Operators
    open Fake.Core.TargetOperators

    open Command
    open Utils
    open Bootstrap
    open Github.Types

    [<RequireQualifiedAccess>]
    module internal CommandArguments =
        let takesRuntimeIdentifier = function
            | Dotnet (Build | Tests | Run | WatchRun) -> true
            | _ -> false

        let withRuntimeIdentifier runtimeIdentifier command commandArguments =
            match runtimeIdentifier with
            | Some runtimeIdentifier when takesRuntimeIdentifier command ->
                commandArguments @ [ RuntimeProps.argument runtimeIdentifier ]
            | _ -> commandArguments

        let mirrordExecArguments runtimeIdentifier command additionalArguments =
            let executable, commandArguments = Command.render command

            [ "exec"; "--config-file"; ".mirrord/mirrord.json"; "--"; executable ]
            @ withRuntimeIdentifier runtimeIdentifier command (commandArguments @ additionalArguments)

    [<RequireQualifiedAccess>]
    module internal ConsoleRelease =
        let publishArguments publishSingleFile releaseDir releaseSource runtimeTarget =
            let runtimeIdentifier = RuntimeTarget.value runtimeTarget

            [
                "-c"; "Release"
                sprintf "/p:PublishSingleFile=%b" publishSingleFile
                // These publishes run in parallel and `obj/project.assets.json` holds one
                // runtime at a time, so each needs an intermediate directory of its own.
                sprintf "/p:BaseIntermediateOutputPath=obj/%s/" runtimeIdentifier
                "-o"; sprintf "%s/%s" releaseDir runtimeIdentifier
                "--self-contained"
                "-r"; runtimeIdentifier
                releaseSource
            ]

    [<RequireQualifiedAccess>]
    module internal TestAssembly =
        let private declaredName =
            Text.RegularExpressions.Regex @"<AssemblyName>\s*([^<$]+?)\s*</AssemblyName>"

        /// The literal `<AssemblyName>` a project file sets, or the file's name without its extension,
        /// which is what MSBuild names the assembly otherwise.
        let name (projectPath: string) (projectXml: string) =
            match declaredName.Match projectXml with
            | declared when declared.Success -> declared.Groups[1].Value
            | _ -> Path.GetFileNameWithoutExtension projectPath

    [<RequireQualifiedAccess>]
    module internal FableSuites =
        /// Whether the suite still references Fable.Mocha, whose browser check makes it render its
        /// results instead of registering tests in Vitest's jsdom environment.
        let usesFableMocha suitePath =
            let references = suitePath </> "paket.references"

            File.Exists references
            && File.ReadAllLines references |> Array.exists (fun line -> line.Trim () = "Fable.Mocha")

        let npmPrerequisites (suites: FableTestSuite list) =
            match suites with
            | [] -> []
            | suites ->
                [
                    "vitest"
                    if suites |> List.exists (fun suite -> suite.Environment = VitestEnvironment.Jsdom) then "jsdom"
                ]

        /// `Build` has restored every project before a suite runs. `fable --run` forces `NODE_ENV=production` on the
        /// command it runs, and `env` resets it: under `production`, Vite reports a production build.
        let fableArguments (vitestArguments: string list) =
            [ "-o"; "output"; "-s"; "--noRestore"; "--run"; "env"; "NODE_ENV=test"; "npx"; "vitest" ] @ vitestArguments

    [<RequireQualifiedAccess>]
    module internal DotNetSuite =
        let testsArguments project = [ "--no-build"; "--project"; project ]

        /// A suite names its project by file or by directory.
        let watchDirectory project =
            if Directory.Exists project then project else Path.getDirectory project

    // --------------------------------------------------------------------------------------------------------
    // 2. Targets for FAKE
    // --------------------------------------------------------------------------------------------------------

    [<RequireQualifiedAccess>]
    module SafeStackTargets =
        let init safe =
            Target.create "SafeClean" (fun _ ->
                Shell.cleanDir safe.DeployPath
                // Delete *.fs.js files created by Fable
                run (Dotnet Fable) [ "clean"; "--yes" ] safe.ClientPath
                // Output compiled against other Fable or package versions can import modules the current ones lack
                Shell.deleteDir (safe.ClientPath </> "output")
            )

            Target.create "Bundle" (fun _ ->
                [
                    toJob (JobName "server") (Dotnet Publish) [ "-c"; "Release"; "-o"; safe.DeployPath ] safe.ServerPath
                    toJob (JobName "client") (Dotnet Fable) [ "-o"; "output"; "-s"; "--run"; "npx"; "vite"; "build" ] safe.ClientPath
                ]
                |> runParallel
            )

            Target.create "Run" (fun _ ->
                run (Dotnet Build) [] safe.SharedPath
                [
                    toJob (JobName "server") (Dotnet WatchRun) [] safe.ServerPath
                    toJob (JobName "client") (Dotnet FableWatch) [ "-o"; "output"; "-s"; "--run"; "npx"; "vite" ] safe.ClientPath
                ]
                |> runParallel
            )

            Target.create "RunMirrord" (fun _ ->
                run (Dotnet Build) [] safe.SharedPath
                Environment.setEnvironVar "RUN_IN" "mirrord"
                [
                    toJob (JobName "server") Mirrord [ "exec"; "--config-file"; "../../.mirrord/mirrord.json"; "--"; "dotnet"; "watch"; "run" ] safe.ServerPath
                    toJob (JobName "client") (Dotnet FableWatch) [ "-o"; "output"; "-s"; "--run"; "npx"; "vite" ] safe.ClientPath
                ]
                |> runParallel
            )

            [
                "SafeClean"
                    ==> "Clean"

                "SafeClean"
                    ==> "AssemblyInfo"
                    ==> "InstallNpm"
                    ==> "Build"
                    ==> "Lint"
                    ==> "Tests"
                    ==> "Bundle"

                "Lint"
                    ==> "WatchTests"

                "Build"
                    ==> "Run" <=> "RunMirrord"
            ]

    let init (definition: ProjectDefinition) =
        Target.initEnvironment ()

        let testSuites = definition.Specs.TestSuites
        let fableSuites = testSuites |> TestSuite.fableSuites

        let buildRuntimeIdentifier =
            match definition.Specs.RuntimeConfiguration with
            | None -> None
            | Some configuration ->
                match RuntimeConfiguration.resolve configuration with
                | Ok runtimeIdentifier -> runtimeIdentifier
                | Error error -> error |> RuntimeConfigurationError.format |> failwith

        let requireRuntimeProps command =
            match buildRuntimeIdentifier with
            | Some _ when CommandArguments.takesRuntimeIdentifier command && not (File.exists RuntimeProps.path) ->
                failwithf "%s is missing. Building for a specific runtime needs it. Run the Bootstrap target first." RuntimeProps.path
            | _ -> ()

        let run command arguments directory =
            requireRuntimeProps command

            Command.run command (CommandArguments.withRuntimeIdentifier buildRuntimeIdentifier command arguments) directory

        let runInRoot command arguments = run command arguments "."

        let runMirrord command arguments =
            runInRoot Mirrord (CommandArguments.mirrordExecArguments buildRuntimeIdentifier command arguments)

        Target.create "Info" (fun _ ->
            let separator sign = Trace.traceFAKE "%s" (String.replicate 69 sign)

            Trace.traceHeader "Project info"
            Trace.tracefn "Project: %s" definition.Project.Name
            Trace.tracefn "Summary: %s" definition.Project.Summary
            Trace.tracefn "Type: %s" definition.Specs.Type

            separator "-"

            Trace.tracefn "Git.branch: %s" (definition.Project.Git |> Option.map (fun git -> git.Branch) |> Option.defaultValue "unknown")
            Trace.tracefn "Git.commit: %s" (definition.Project.Git |> Option.map (fun git -> git.Commit) |> Option.defaultValue "unknown")
            Trace.tracefn "Git.repository: %s" (definition.Project.Git |> Option.bind (fun git -> git.Repository) |> Option.defaultValue "unknown")

            separator "-"

            Trace.tracefn "BuildNumber: %s" ("BUILD_NUMBER" |> envVar |> Option.defaultValue "-")

            match definition with
            | { Specs = SAFEStackApplication { TemplateVersion = templateVersion } } ->
                Trace.tracefn "SafeTemplateVersion: %s" templateVersion
            | _ -> ()

            separator "="
        )

        Target.create "Bootstrap" (fun _ -> Bootstrap.deploy definition.Specs)

        Target.create "Solution" (fun _ ->
            match definition.Project.Name |> SolutionFileName.create with
            | Ok fileName -> definition.Sources.Build |> Solution.generate (IO.Directory.GetCurrentDirectory ()) fileName
            | Error error -> error |> SolutionFileNameError.format |> failwith
        )

        Target.create "Clean" <| skipOn "no-clean" (fun _ ->
            !! "./**/bin/Release"
            ++ "./**/bin/Debug"
            ++ "./**/obj"
            ++ "./**/.ionide"
            -- "./bin/console"
            -- "./build/**"
            |> Shell.cleanDirs
        )

        Target.create "AssemblyInfo" (fun _ ->
            let testAssemblies =
                definition.Sources.Tests
                |> Seq.map (fun project -> TestAssembly.name project (File.ReadAllText project))
                |> Seq.distinct
                |> List.ofSeq

            let getAssemblyInfoAttributes projectName =
                let now = DateTime.Now

                let release =
                    definition.ChangeLog
                    |> Option.bind (fun changeLog ->
                        try ReleaseNotes.parse (File.ReadAllLines changeLog |> Seq.filter ((<>) "## Unreleased")) |> Some
                        with _ -> None
                    )

                let gitValue fallbackEnvironmentVariableNames initialValue =
                    initialValue
                    |> String.replace "NoBranch" ""
                    |> stringToOption
                    |> Option.bindNone (fun _ -> fallbackEnvironmentVariableNames |> List.tryPick envVar)
                    |> Option.defaultValue "unknown"

                [
                    AssemblyInfo.Title projectName
                    AssemblyInfo.Product definition.Project.Name
                    AssemblyInfo.Description definition.Project.Summary

                    match release with
                    | Some release ->
                        AssemblyInfo.Version release.AssemblyVersion
                        AssemblyInfo.FileVersion release.AssemblyVersion
                    | _ ->
                        AssemblyInfo.Version "1.0"
                        AssemblyInfo.FileVersion "1.0"

                    for testAssembly in testAssemblies do
                        AssemblyInfo.InternalsVisibleTo testAssembly

                    match definition.Project.Git with
                    | None ->
                        AssemblyInfo.Metadata("gitbranch", null |> gitValue [ "GIT_BRANCH"; "branch" ])
                        AssemblyInfo.Metadata("gitcommit", null |> gitValue [ "GIT_COMMIT"; "commit" ])
                    | Some git ->
                        AssemblyInfo.Metadata("gitbranch", git.Branch |> gitValue [ "GIT_BRANCH"; "branch" ])
                        AssemblyInfo.Metadata("gitcommit", git.Commit |> gitValue [ "GIT_COMMIT"; "commit" ])

                    AssemblyInfo.Metadata("createdAt", now.ToString("yyyy-MM-dd HH:mm:ss"))
                    AssemblyInfo.Metadata("buildNumber", "BUILD_NUMBER" |> envVar |> Option.defaultValue "-")

                    match definition with
                    | { Specs = SAFEStackApplication { TemplateVersion = templateVersion } } ->
                        AssemblyInfo.Metadata("SafeTemplateVersion", templateVersion)
                    | _ -> ()
                ]

            let getProjectDetails (projectPath: string) =
                let projectName = IO.Path.GetFileNameWithoutExtension(projectPath)
                (
                    projectPath,
                    projectName,
                    IO.Path.GetDirectoryName(projectPath),
                    (getAssemblyInfoAttributes projectName)
                )

            // AssemblyInfo.fs is a compile input, so a rewrite makes MSBuild recompile the whole
            // project. `createdAt` alone would move on every run, which is why it is excluded from
            // the comparison — the timestamp records when the assembly info last changed, not when
            // the last build ran.
            let createUnlessUnchanged path attributes =
                let candidate = IO.Path.GetTempFileName ()

                try
                    AssemblyInfoFile.createFSharp candidate attributes

                    let significantLines file =
                        File.read file
                        |> Seq.filter (fun (line: string) -> line.Contains "createdAt" |> not)
                        |> List.ofSeq

                    if not (File.exists path) || significantLines candidate <> significantLines path then
                        Shell.copyFile path candidate
                finally
                    File.delete candidate

            definition.Sources.Build
            |> Seq.map getProjectDetails
            |> Seq.iter (fun (_, _, folderName, attributes) ->
                createUnlessUnchanged (folderName </> "AssemblyInfo.fs") attributes
            )
        )

        Target.create "Build" (fun _ ->
            match Solution.current () with
            | Ok (Some solution) -> run (Dotnet Build) [ solution ] "."
            | Ok None -> definition.Sources.Build |> Seq.iter (Path.getDirectory >> run (Dotnet Build) [])
            | Error error -> error |> SolutionPickError.format |> failwith
        )

        Target.create "Lint" <| skipOn "no-lint" (fun _ ->
            Seq.append definition.Sources.Build (!! "build/*.fsproj")
            |> Seq.map (fun fsproj -> toJob (JobName fsproj) (Dotnet Lint) [ fsproj ] ".")
            |> runParallelWith GroupedByJob
        )

        Target.create "InstallNpm" (fun _ ->
            if not fableSuites.IsEmpty then
                runInRoot (Npm Version) []
                runInRoot (Npm Install) []
            else
                Trace.tracefn "No Fable test suite or SAFE-stack client; nothing to install with npm."
        )

        let prepareFableSuites () =
            fableSuites |> List.iter (fun suite ->
                let config = VitestConfig.path suite

                if not (File.exists config) then
                    failwithf "%s is missing. The tests in %s run under it. Run the Bootstrap target first." config suite.Path

                if FableSuites.usesFableMocha suite.Path then
                    failwithf
                        "%s still references Fable.Mocha. Move the tests to Alma.Testing (see Alma.Build's README, \"Fable tests\")."
                        (suite.Path </> "paket.references")
            )

            let prerequisites = FableSuites.npmPrerequisites fableSuites

            prerequisites |> List.iter (fun prerequisite ->
                runOr (Npm Ls) [ "--depth=0"; prerequisite ] "." (fun _ ->
                    failwithf "Missing npm prerequisites, run `npm install --save-dev %s`" (String.concat " " prerequisites)
                )
            )

            fableSuites |> List.iter (fun suite -> Shell.deleteDir (suite.Path </> "output"))

        let runTestSuites watch =
            let job (name: string) command arguments directory =
                requireRuntimeProps command

                toJob
                    (JobName (Solution.relativeTo (Directory.GetCurrentDirectory ()) name))
                    command
                    (CommandArguments.withRuntimeIdentifier buildRuntimeIdentifier command arguments)
                    directory

            match testSuites with
            | [] -> Trace.tracefn "There are no tests yet."
            | suites ->
                prepareFableSuites ()

                suites
                |> List.map (function
                    | TestSuite.DotNet project when watch -> job project (Dotnet WatchRun) [] (DotNetSuite.watchDirectory project)
                    | TestSuite.DotNet project -> job project (Dotnet Tests) (DotNetSuite.testsArguments project) "."
                    | TestSuite.Fable suite when watch -> job suite.Path (Dotnet FableWatch) (FableSuites.fableArguments []) suite.Path
                    | TestSuite.Fable suite -> job suite.Path (Dotnet Fable) (FableSuites.fableArguments [ "run" ]) suite.Path
                )
                |> runParallel

        Target.create "Tests" (fun _ -> runTestSuites false)
        Target.create "WatchTests" (fun _ -> runTestSuites true)

        if not definition.Specs.IsSAFEStack then

            let zipRelease releaseDir runtimeTargets =
                if releaseDir </> "zipCompiled" |> File.exists
                then
                    Trace.tracefn "\nZipping released files in %s ..." releaseDir
                    run (Command.Raw (releaseDir </> "zipCompiled")) [] "."

                Trace.tracefn "\nZip compiled files"
                runtimeTargets
                |> List.iter (RuntimeTarget.value >> fun runtimeIdentifier ->
                    Trace.tracefn " -> zipping %s ..." runtimeIdentifier
                    let zipFile = sprintf "%s.zip" runtimeIdentifier
                    IO.File.Delete zipFile
                    Zip.zip releaseDir (releaseDir </> zipFile) !!(releaseDir </> runtimeIdentifier </> "*")
                )

            Target.create "Release" (fun _ ->
                match definition with
                | { Specs = Library { ReleaseDir = releaseDir; NugetApi = nugetApi } } ->
                    match "src" </> definition.Project.Name with
                    | releaseSource when releaseSource |> Directory.Exists ->
                        run (Dotnet Pack) [] releaseSource
                    | _ ->
                        // A root holding both a project and a solution file is ambiguous to MSBuild,
                        // so the project is named whenever exactly one sits there.
                        match !! "./*.fsproj" |> List.ofSeq with
                        | [ project ] -> runInRoot (Dotnet Pack) [ project ]
                        | _ -> runInRoot (Dotnet Pack) []

                    Directory.ensure releaseDir

                    !! "**/bin/**/*.nupkg"
                    |> Seq.iter (Shell.moveFile releaseDir)

                | { Specs = ConsoleApplication { RuntimeTargets = runtimeTargets; ReleaseSource = releaseSource; ReleaseDir = releaseDir; PublishSingleFile = publishSingleFile } } ->
                    let releaseDir = Path.getFullName releaseDir

                    Trace.tracefn "\nClean previous releases"
                    runtimeTargets
                    |> Seq.collect (RuntimeTarget.value >> fun runtimeIdentifier ->
                        Trace.tracefn " - %s" runtimeIdentifier
                        !! (releaseDir </> runtimeIdentifier)
                    )
                    |> Shell.cleanDirs

                    Trace.tracefn "\nClean previous zipped releases"
                    !! (releaseDir </> "*.zip")
                    ++ (releaseDir </> "*.tar.gz")
                    |> Seq.map (tee (Trace.tracefn " - %s"))
                    |> Seq.iter File.delete

                    Trace.tracefn "\nPublish current release"

                    runtimeTargets
                    |> List.map (fun runtimeTarget ->
                        toJob
                            (JobName (RuntimeTarget.value runtimeTarget))
                            (Dotnet Publish)
                            (ConsoleRelease.publishArguments publishSingleFile releaseDir releaseSource runtimeTarget)
                            "."
                    )
                    |> runParallelWith GroupedByJob

                    runtimeTargets |> zipRelease releaseDir

                | { Specs = Executable { ReleaseDir = releaseDir } } ->
                    runInRoot (Dotnet Publish) [ "-c"; "Release"; "-o"; releaseDir ]

                | { Specs = SAFEStackApplication _ } -> failwithf "For releasing SAFE-Stack Application, use \"bundle\" target instead."
            )

            Target.create "ZipRelease" (fun _ ->
                match definition with
                | { Specs = ConsoleApplication { RuntimeTargets = runtimeTargets; ReleaseDir = releaseDir } } ->
                    runtimeTargets |> zipRelease releaseDir
                | _ -> ()
            )

            Target.create "Publish" (fun _ ->
                match definition with
                | { Specs = Library { NugetApi = NugetApi.NotUsed } } -> Trace.traceHeader "NugetApi is not used"

                | { Specs = Library { ReleaseDir = releaseDir; NugetApi = NugetApi.Organization organization; NugetCustomServerRepository = nugetServer } } ->
                    Trace.traceHeader "Pushing to organization nuget server"

                    envVar "PRIVATE_FEED_PASS"
                    |> Option.requireSome "Environment variable PRIVATE_FEED_PASS is not set."
                    |> Nuget.push releaseDir (Some organization)

                    match envVar "NUGET_SERVER_TOKEN", envVar "NUGET_SERVER_REPOSITORY" |> Option.orElse nugetServer with
                    | Some token, Some repository ->
                        Trace.tracefn "Trigger: Update %s readme" repository

                        Github.triggerAction {
                            EventType = "update-readme"
                            CurrentProject = definition.Project.Name
                            Token = token
                            Organization = envVar "NUGET_SERVER_ORGANIZATION" |> Option.defaultValue organization
                            Repository = repository
                        }
                        |> Async.RunSynchronously

                    | _ -> ()

                | { Specs = Library { ReleaseDir = releaseDir; NugetApi = NugetApi.AskForKey; Organization = organization }} ->
                    Trace.traceHeader "Pushing to public nuget server"

                    match UserInput.getUserInput "Are you sure - is it tagged yet? [y|n]: " with
                    | "y" | "yes" ->
                        match UserInput.getUserPassword "Nuget ApiKey: " with
                        | null | "" -> failwithf "You have to provide an api key for nuget."
                        | apiKey -> Nuget.push releaseDir organization apiKey
                    | _ -> ()

                | { Specs = Library { ReleaseDir = releaseDir; NugetApi = NugetApi.KeyInEnvironment name; Organization = organization }} ->
                    Trace.traceHeader "Pushing to nuget server"

                    envVar name
                    |> Option.requireSome (sprintf "Nuget API key not set in environment variable %s." name)
                    |> Nuget.push releaseDir organization

                | _ -> ()
            )

            Target.create "Watch" (fun _ ->
                runInRoot (Dotnet WatchRun) []
            )

            Target.create "WatchMirrord" (fun _ ->
                Environment.setEnvironVar "RUN_IN" "mirrord"
                runMirrord (Dotnet WatchRun) []
            )

            Target.create "Run" (fun _ ->
                runInRoot (Dotnet Run) []
            )

            Target.create "RunMirrord" (fun _ ->
                Environment.setEnvironVar "RUN_IN" "mirrord"
                runMirrord (Dotnet Run) []
            )

        // --------------------------------------------------------------------------------------------------------
        // 3. FAKE targets hierarchy
        // --------------------------------------------------------------------------------------------------------

        match definition with
        | { Specs = Library _ } ->
            [
                "Clean"
                    ==> "AssemblyInfo"
                    ==> "InstallNpm"
                    ==> "Build"
                    ==> "Lint"
                    ==> "Tests"
                    ==> "Release"
                    ==> "Publish"

                "Lint"
                    ==> "WatchTests"
            ]

        | { Specs = ConsoleApplication _ } ->
            [
                "Clean"
                    ==> "AssemblyInfo"
                    ==> "InstallNpm"
                    ==> "Build"
                    ==> "Lint"
                    ==> "Tests"
                    ==> "Release"
                    ==> "ZipRelease"

                "Lint"
                    ==> "WatchTests"

                "Build"
                    ==> "Watch" <=> "WatchMirrord" <=> "Run" <=> "RunMirrord"
            ]

        | { Specs = Executable _ } ->
            [
                "Clean"
                    ==> "AssemblyInfo"
                    ==> "InstallNpm"
                    ==> "Build"
                    ==> "Lint"
                    ==> "Tests"
                    ==> "Release" <=> "Watch" <=> "WatchMirrord" <=> "Run" <=> "RunMirrord"

                "Lint"
                    ==> "WatchTests"
            ]

        | { Specs = SAFEStackApplication safe } ->
            SafeStackTargets.init safe

        |> ignore
