module Alma.Build.Tests.Integration.Utils

open System
open System.IO
open System.Diagnostics

// ---- Repo layout ----

let root =
    let rec find (dir: string) =
        if File.Exists (Path.Combine (dir, "fbuild.slnx")) then dir
        else find (Directory.GetParent(dir).FullName)

    find AppDomain.CurrentDomain.BaseDirectory

/// Fixtures reference the engine straight from source, so the integration tests exercise whatever is
/// checked out — no repack step between an engine edit and the test seeing it.
let engineProject = Path.Combine (root, "src", "Alma.Build", "Alma.Build.fsproj")

let fixtures = Path.Combine (root, "tests", "integration", "fixtures")

type Fixture =
    | Library
    | LibraryRoot
    | Executable
    | Console
    | Safe

[<RequireQualifiedAccess>]
module Fixture =
    let directory = function
        | Library -> "library"
        | LibraryRoot -> "library-root"
        | Executable -> "executable"
        | Console -> "console"
        | Safe -> "safe"

// ---- Process execution ----

let execWith (env: (string * string) list) (workDir: string) (exe: string) (args: string) =
    let psi = ProcessStartInfo (exe, args)
    psi.WorkingDirectory <- workDir
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false

    for name, value in env do
        psi.Environment[name] <- value

    use p = Process.Start psi
    let stdout = p.StandardOutput.ReadToEnd ()
    let stderr = p.StandardError.ReadToEnd ()
    p.WaitForExit ()
    p.ExitCode, stdout, stderr

let exec (workDir: string) (exe: string) (args: string) = execWith [] workDir exe args

let execWithOk env workDir exe args =
    let code, out, err = execWith env workDir exe args

    if code <> 0 then
        failwith $"Command failed ({code}):\n  {exe} {args}\nstdout:\n{out}\nstderr:\n{err}"

let execOk workDir exe args = execWithOk [] workDir exe args

// ---- Fixture preparation ----

/// Build artifacts a previous local run may have left in the checked-in fixture. Copying them
/// into the working copy would let a stale `bin/` satisfy a target that should have rebuilt it.
let private ignored =
    set [ "bin"; "obj"; "node_modules"; "deploy"; "output"; "release"; "dist"; "app"; ".git" ]

[<TailCall>]
let rec private copyAll pending =
    match pending with
    | [] -> ()
    | (source: string, target: string) :: rest ->
        Directory.CreateDirectory target |> ignore

        for file in Directory.GetFiles source do
            File.Copy (file, Path.Combine (target, Path.GetFileName file), true)

        let children =
            Directory.GetDirectories source
            |> Seq.map (fun dir -> dir, Path.GetFileName dir)
            |> Seq.filter (snd >> ignored.Contains >> not)
            |> Seq.map (fun (dir, name) -> dir, Path.Combine (target, name))
            |> List.ofSeq

        copyAll (children @ rest)

let private copyInto (source: string) (target: string) = copyAll [ source, target ]

/// Intermediate and output paths for the engine built through a fixture's project reference,
/// isolated to that fixture copy: the scenarios run in parallel over one engine project.
let private engineBuildPaths fixtureDirectory =
    let separator = string Path.DirectorySeparatorChar
    Path.Combine (fixtureDirectory, "build", ".engine", "obj") + separator,
    Path.Combine (fixtureDirectory, "build", ".engine", "bin") + separator

/// A build project referencing the engine from source. It restores without Paket, so the SDK's
/// implicit FSharp.Core is disabled here by hand to let the engine's own version flow through the
/// project reference.
let private buildFsproj fixtureDirectory =
    let engineIntermediate, engineOutput = engineBuildPaths fixtureDirectory

    $"""<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <NoWarn>NU1510</NoWarn>
        <DisableImplicitFSharpCoreReference>true</DisableImplicitFSharpCoreReference>
    </PropertyGroup>
    <ItemGroup>
        <Compile Include="Build.fs" />
    </ItemGroup>
    <ItemGroup>
        <ProjectReference Include="{engineProject}">
            <AdditionalProperties>BaseIntermediateOutputPath={engineIntermediate};BaseOutputPath={engineOutput}</AdditionalProperties>
        </ProjectReference>
    </ItemGroup>
</Project>
"""

/// The restores below — the one `dotnet pack` runs for itself included — populate the machine-wide
/// NuGet package cache, which the scenarios would otherwise write concurrently.
let private restoreLock = obj ()

/// Copies a checked-in fixture to a throwaway directory and completes it into a runnable consumer
/// repo: the `build.fsproj` carrying the absolute engine path and isolated output paths, a git repo,
/// which the engine's `Git.init` requires because it shells out to `git rev-parse HEAD`, and the
/// support files, deployed by the engine's own `Bootstrap` target.
///
/// `Bootstrap` runs before `dotnet tool restore` because it renders the tools manifest the restore
/// reads. Lint then runs under the shipped ruleset rather than fsharplint's defaults, and the tool
/// versions under test are the ones the engine pins.
let private prepare fixture =
    let fixtureName = Fixture.directory fixture

    let dir = Path.Combine (Path.GetTempPath (), $"fbuild-it-{fixtureName}-{Guid.NewGuid():N}")
    copyInto (Path.Combine (fixtures, fixtureName)) dir

    File.WriteAllText (Path.Combine (dir, "build", "build.fsproj"), buildFsproj dir)

    let engineIntermediate, engineOutput = engineBuildPaths dir

    lock restoreLock (fun () ->
        execOk root "dotnet" $"restore \"{engineProject}\" /p:BaseIntermediateOutputPath=\"{engineIntermediate}\" /p:BaseOutputPath=\"{engineOutput}\"")

    execOk dir "git" "init"
    execOk dir "git" "-c user.email=test@example.com -c user.name=Test -c commit.gpgsign=false commit --allow-empty -m init"
    execOk dir "dotnet" "run --project build/build.fsproj -- Bootstrap"
    lock restoreLock (fun () -> execOk dir "dotnet" "tool restore")

    dir

// ---- Packaged engine ----

/// Version the packaged case stamps on the engine it builds. It must be one nuget.org does not
/// carry: the consumer needs nuget.org in its sources for the engine's own dependencies, and a
/// version present on both feeds could resolve to the published package instead of this build.
let private packagedVersion = "99.0.0-integration"

/// The consumer-side `build/build.fsproj` from the adoption steps in `README.md`: no reference to
/// the engine at all, the package arrives through `build/paket.references`.
let private packagedBuildFsproj =
    """<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <IsPackable>false</IsPackable>
        <NoWarn>NU1510</NoWarn>
    </PropertyGroup>
    <ItemGroup>
        <Compile Include="Build.fs" />
    </ItemGroup>
    <Import Project="..\.paket\Paket.Restore.targets" />
</Project>
"""

/// The seed manifest from the adoption steps in `README.md`, holding only what it takes to reach
/// the engine: Paket has to be restorable before the package it resolves can render the real one.
let private packagedTools =
    """{
    "version": 1,
    "isRoot": true,
    "tools": {
        "paket": { "version": "10.3.1", "commands": [ "paket" ] }
    }
}
"""

let private packagedDependencies (feed: string) =
    $"""group Build
    source https://api.nuget.org/v3/index.json
    source {feed}

    nuget Alma.Build {packagedVersion}
"""

/// A `paket.lock` an adopting repository already carries: a `Build` group resolved before the engine
/// was added, holding the FSharp.Core its FAKE packages settled on back then. `paket install` keeps a
/// locked version wherever the new dependency's constraints allow, so the engine's package has to
/// declare the FSharp.Core it binds to or the consumer loads the stale one and fails at startup.
let private packagedStaleLock =
    """GROUP Build
STORAGE: NONE
NUGET
  remote: https://api.nuget.org/v3/index.json
    FSharp.Core (9.0.202)
"""

/// Packs the engine into `feed` under `packagedVersion`, keeping its intermediate and output paths
/// inside the throwaway directory: the scenarios run in parallel and build the engine project from
/// source, so packing through the shared `src/Alma.Build/obj` would race with them.
let private packEngine (dir: string) (feed: string) =
    let separator = Path.DirectorySeparatorChar
    let intermediate = Path.Combine (dir, "engine-obj")
    let output = Path.Combine (dir, "engine-bin")

    let args =
        $"pack \"{engineProject}\" -c Release -o \"{feed}\" /p:Version={packagedVersion} "
        + $"/p:BaseIntermediateOutputPath=\"{intermediate}{separator}\" "
        + $"/p:BaseOutputPath=\"{output}{separator}\""

    lock restoreLock (fun () -> execOk root "dotnet" args)

/// Completes a fixture copy into a consumer that resolves the engine as a NuGet package, following
/// the adoption steps in `README.md`: paket files pinning the engine, the consumer `build.fsproj`,
/// `paket install` to write `paket.lock` and `.paket/Paket.Restore.targets`, and the support files
/// deployed by the packaged engine's own `Bootstrap` target instead of copied from this repo.
///
/// Restores run against a NuGet cache inside the throwaway directory. The engine version does not
/// move between packs, so a cache shared with the machine would hand back an earlier build of it.
let private preparePackaged fixture =
    let fixtureName = Fixture.directory fixture
    let dir = Path.Combine (Path.GetTempPath (), $"fbuild-it-packaged-{fixtureName}-{Guid.NewGuid():N}")
    copyInto (Path.Combine (fixtures, fixtureName)) dir

    let feed = Path.Combine (dir, "feed")
    let cache = Path.Combine (dir, "nuget-cache")
    let env = [ "NUGET_PACKAGES", cache ]

    packEngine dir feed

    File.WriteAllText (Path.Combine (dir, "paket.dependencies"), packagedDependencies feed)
    File.WriteAllText (Path.Combine (dir, "paket.lock"), packagedStaleLock)
    File.WriteAllText (Path.Combine (dir, "build", "paket.references"), "group Build\n    Alma.Build\n")
    File.WriteAllText (Path.Combine (dir, "build", "build.fsproj"), packagedBuildFsproj)

    Directory.CreateDirectory (Path.Combine (dir, ".config")) |> ignore
    File.WriteAllText (Path.Combine (dir, ".config", "dotnet-tools.json"), packagedTools)

    execOk dir "git" "init"
    execOk dir "git" "-c user.email=test@example.com -c user.name=Test -c commit.gpgsign=false commit --allow-empty -m init"
    execWithOk env dir "dotnet" "tool restore"
    execWithOk env dir "dotnet" "tool run paket install"
    execWithOk env dir "dotnet" "run --project build/build.fsproj -- Bootstrap"

    dir, env

let private keepTemp =
    Environment.GetEnvironmentVariable "FBUILD_KEEP_TEMP" |> String.IsNullOrEmpty |> not

let private cleanup (dir: string) =
    if keepTemp then printfn $"FBUILD_KEEP_TEMP set, kept fixture copy: {dir}"
    else Directory.Delete (dir, true)

/// Runs a target against a freshly prepared source-engine fixture, handing the copy to `edit`
/// before it and the directory to the assertion callback after it. Set `FBUILD_KEEP_TEMP` to keep
/// the copy for inspection.
let withEditedFixture fixture (edit: string -> unit) (target: string) (assertArtifacts: string -> unit) =
    let dir = prepare fixture

    try
        edit dir
        execOk dir "dotnet" $"run --project build/build.fsproj -- {target}"
        assertArtifacts dir
    finally
        cleanup dir

/// Runs a target against a freshly prepared source-engine fixture and hands its directory to the
/// assertion callback.
let withFixture fixture (target: string) (assertArtifacts: string -> unit) =
    withEditedFixture fixture ignore target assertArtifacts

/// Runs a target twice against a freshly prepared source-engine fixture, so `assertArtifacts` can
/// verify a target that generates a file on its first run leaves it alone on the second.
let withFixtureRunTwice fixture (target: string) (assertArtifacts: string -> unit) =
    let dir = prepare fixture

    try
        execOk dir "dotnet" $"run --project build/build.fsproj -- {target}"
        execOk dir "dotnet" $"run --project build/build.fsproj -- {target}"
        assertArtifacts dir
    finally
        cleanup dir

/// Runs a target through the package-installed engine and its deployed build entrypoint.
let withPackagedFixture fixture (target: string) (assertArtifacts: string -> unit) =
    let dir, env = preparePackaged fixture

    try
        execWithOk env dir (Path.Combine (dir, "build.sh")) target
        assertArtifacts dir
    finally
        cleanup dir
