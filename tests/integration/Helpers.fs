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

let exec (workDir: string) (exe: string) (args: string) =
    let psi = ProcessStartInfo (exe, args)
    psi.WorkingDirectory <- workDir
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    use p = Process.Start psi
    let stdout = p.StandardOutput.ReadToEnd ()
    let stderr = p.StandardError.ReadToEnd ()
    p.WaitForExit ()
    p.ExitCode, stdout, stderr

let execOk workDir exe args =
    let code, out, err = exec workDir exe args

    if code <> 0 then
        failwith $"Command failed ({code}):\n  {exe} {args}\nstdout:\n{out}\nstderr:\n{err}"

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
        <PackageReference Include="FSharp.Core" Version="10.1.300" />
    </ItemGroup>
</Project>
"""

/// The engine packs these as `content/` and a consumer vendors them to its repo root. Taking them
/// from `vendored/` — the same copy that gets packed — is what makes the matrix lint fixture
/// sources under the real shipped ruleset instead of fsharplint's defaults.
let private vendoredAssets = [ "fsharplint.json"; ".editorconfig" ]

/// Copies a checked-in fixture to a throwaway directory and completes it into a runnable consumer
/// repo: the vendored assets, the `build.fsproj` carrying the absolute engine path, and a git repo,
/// which the engine's `Git.init` requires because it shells out to `git rev-parse HEAD`.
let private prepare (fixture: string) =
    let dir = Path.Combine (Path.GetTempPath (), $"fbuild-it-{fixture}-{Guid.NewGuid():N}")
    copyInto (Path.Combine (fixtures, fixture)) dir

    for asset in vendoredAssets do
        File.Copy (Path.Combine (root, "vendored", asset), Path.Combine (dir, asset), true)

    File.WriteAllText (Path.Combine (dir, "build", "build.fsproj"), buildFsproj)

    execOk dir "git" "init"
    execOk dir "git" "-c user.email=test@example.com -c user.name=Test -c commit.gpgsign=false commit --allow-empty -m init"
    execOk dir "dotnet" "tool restore"

    dir

let private keepTemp =
    Environment.GetEnvironmentVariable "FBUILD_KEEP_TEMP" |> String.IsNullOrEmpty |> not

/// Runs `body` against a freshly prepared fixture copy, deleting the copy afterwards whether
/// `body` succeeds or throws. Set `FBUILD_KEEP_TEMP` to keep it for inspection.
let withFixture (fixture: string) (body: string -> unit) =
    let dir = prepare fixture

    try
        body dir
    finally
        if keepTemp then printfn $"FBUILD_KEEP_TEMP set, kept fixture copy: {dir}"
        else Directory.Delete (dir, true)
