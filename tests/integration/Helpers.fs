module Alma.Build.Tests.Integration.Helpers

open System
open System.IO
open System.Diagnostics

// ---- Repo layout ----

let root =
    let rec find (dir: string) =
        if File.Exists (Path.Combine (dir, "fbuild.slnx")) then dir
        else find (Directory.GetParent(dir).FullName)

    find AppDomain.CurrentDomain.BaseDirectory

/// Fixtures reference the engine straight from source, so the matrix exercises whatever is
/// checked out — no repack step between an engine edit and the test seeing it.
let engineProject = Path.Combine (root, "src", "Alma.Build", "Alma.Build.fsproj")

let fixtures = Path.Combine (root, "tests", "integration", "fixtures")

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

let rec private copyInto (source: string) (target: string) =
    Directory.CreateDirectory target |> ignore

    for file in Directory.GetFiles source do
        File.Copy (file, Path.Combine (target, Path.GetFileName file), true)

    for dir in Directory.GetDirectories source do
        let name = Path.GetFileName dir
        if not (ignored.Contains name) then copyInto dir (Path.Combine (target, name))

/// The version paket resolved for the engine. The generated project below sits outside the
/// repository, so it cannot reach `paket.lock` through a `paket.references` of its own and has to
/// carry the version literally — reading it here keeps the lock the only place it is written down.
let private fsharpCoreVersion =
    let lockFile = Path.Combine (root, "paket.lock")

    // A resolved package sits at four spaces; the six-space lines under it are the version
    // constraints its dependents asked for, which are ranges rather than a version.
    File.ReadAllLines lockFile
    |> Seq.tryPick (fun line ->
        let matched = Text.RegularExpressions.Regex.Match (line, @"^ {4}FSharp\.Core \(([^)]+)\)")
        if matched.Success then Some matched.Groups[1].Value else None)
    |> Option.defaultWith (fun () -> failwith $"No resolved FSharp.Core version found in {lockFile}")

/// A build project that references the engine from source and pins FSharp.Core to the engine's
/// paket-resolved version — the SDK's implicit lower version would otherwise shadow it and the
/// engine assembly would fail to load at runtime.
let private buildFsproj =
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
        <ProjectReference Include="{engineProject}" />
    </ItemGroup>
    <ItemGroup>
        <PackageReference Include="FSharp.Core" Version="{fsharpCoreVersion}" />
    </ItemGroup>
</Project>
"""

/// The engine packs these as `content/` and `Bootstrap` deploys them to consumer repo roots. Taking them
/// from `bootstrap/` — the same copy that gets packed — is what makes the matrix lint fixture
/// sources under the real shipped ruleset instead of fsharplint's defaults.
let private bootstrapAssets = [ "fsharplint.json"; ".editorconfig" ]

/// Copies a checked-in fixture to a throwaway directory and completes it into a runnable consumer
/// repo: the bootstrap assets, the `build.fsproj` carrying the absolute engine path, and a git repo,
/// which the engine's `Git.init` requires because it shells out to `git rev-parse HEAD`.
let private prepare (fixture: string) =
    let dir = Path.Combine (Path.GetTempPath (), $"fbuild-it-{fixture}-{Guid.NewGuid():N}")
    copyInto (Path.Combine (fixtures, fixture)) dir

    for asset in bootstrapAssets do
        File.Copy (Path.Combine (root, "bootstrap", asset), Path.Combine (dir, asset), true)

    File.WriteAllText (Path.Combine (dir, "build", "build.fsproj"), buildFsproj)

    execOk dir "git" "init"
    execOk dir "git" "-c user.email=test@example.com -c user.name=Test -c commit.gpgsign=false commit --allow-empty -m init"
    execOk dir "dotnet" "tool restore"

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

let private packagedTools =
    """{
    "version": 1,
    "isRoot": true,
    "tools": {
        "paket": { "version": "10.3.1", "commands": [ "paket" ] },
        "dotnet-fsharplint": { "version": "0.26.10", "commands": [ "dotnet-fsharplint" ] }
    }
}
"""

let private packagedDependencies (feed: string) =
    $"""group Build
    source https://api.nuget.org/v3/index.json
    source {feed}

    nuget Alma.Build {packagedVersion}
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

    execOk root "dotnet" args

/// Completes a fixture copy into a consumer that resolves the engine as a NuGet package, following
/// the adoption steps in `README.md`: paket files pinning the engine, the consumer `build.fsproj`,
/// `paket install` to write `paket.lock` and `.paket/Paket.Restore.targets`, and the support files
/// deployed by the packaged engine's own `Bootstrap` target instead of copied from this repo.
///
/// Restores run against a NuGet cache inside the throwaway directory. The engine version does not
/// move between packs, so a cache shared with the machine would hand back an earlier build of it.
let private preparePackaged (fixture: string) =
    let dir = Path.Combine (Path.GetTempPath (), $"fbuild-it-packaged-{fixture}-{Guid.NewGuid():N}")
    copyInto (Path.Combine (fixtures, fixture)) dir

    let feed = Path.Combine (dir, "feed")
    let cache = Path.Combine (dir, "nuget-cache")
    let env = [ "NUGET_PACKAGES", cache ]

    packEngine dir feed

    File.WriteAllText (Path.Combine (dir, "paket.dependencies"), packagedDependencies feed)
    File.WriteAllText (Path.Combine (dir, "build", "paket.references"), "group Build\n    Alma.Build\n")
    File.WriteAllText (Path.Combine (dir, "build", "build.fsproj"), packagedBuildFsproj)
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

/// Runs `target` through the `build.sh` the packaged engine's `Bootstrap` target deployed —
/// invoked directly, so the executable bit Bootstrap sets is part of what is asserted — then
/// hands the consumer directory to `assertArtifacts`.
let withPackagedFixture (fixture: string) (target: string) (assertArtifacts: string -> unit) =
    let dir, env = preparePackaged fixture

    try
        execWithOk env dir (Path.Combine (dir, "build.sh")) target
        assertArtifacts dir
    finally
        cleanup dir

/// Runs `body` against a freshly prepared fixture copy, deleting the copy afterwards whether
/// `body` succeeds or throws. Set `FBUILD_KEEP_TEMP` to keep it for inspection.
let withFixture (fixture: string) (body: string -> unit) =
    let dir = prepare fixture

    try
        body dir
    finally
        cleanup dir
