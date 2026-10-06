# Examples

Worked examples ordered by increasing complexity. Each `Build.fs` is a complete
`build/Build.fs`; placeholder names are neutral (`Acme.Widgets`, `acme`).

## Basic — library with defaults

`Spec.defaultLibrary` packs `src/<Project.Name>`, runs tests from `tests/*.fsproj`,
and publishes nothing.

```fsharp
open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "Acme.Widgets"
            Summary = "Widget primitives."
            Git = Git.init ()
        }
        Specs = Spec.defaultLibrary
    }

    args |> Args.run
```

```bash
./build.sh                    # Build (Clean → AssemblyInfo → Build)
./build.sh Tests no-lint      # ... → Build → Tests, Lint skipped
./build.sh -t Release         # ... → Tests → Release, .nupkg in release/
```

## Realistic — library published to nuget.org

Nested test projects and a key read from the environment at `Publish` time.

```fsharp
open Fake.IO.Globbing.Operators

open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "Acme.Widgets"
            Summary = "Widget primitives."
            Git = Git.init ()
        }
        Specs =
            Spec.defaultLibrary
            |> Spec.mapLibrary (fun library ->
                { library with
                    NugetApi = NugetApi.KeyInEnvironment "NUGET_API_KEY"
                    TestsSources = !! "tests/*/*.fsproj"
                }
            )
    }

    args |> Args.run
```

```bash
NUGET_API_KEY=... ./build.sh Publish   # whole chain, then dotnet nuget push release/*.nupkg
```

## Console application with runtimes

Three release runtimes; local builds use the host RID, which must be one of them.
Releases stay unbundled.

```fsharp
open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "acme-cli"
            Summary = "Acme command line tool."
            Git = Git.init ()
        }
        Specs =
            Spec.defaultConsoleApplication [ Linux; OSX; OSXArm64 ]
            |> Spec.mapConsoleApplication (fun spec ->
                { spec with
                    RuntimeMode = RuntimeMode.AutoDetect
                    PublishSingleFile = false
                    ReleaseSource = "src/AcmeCli/AcmeCli.fsproj"
                }
            )
    }

    args |> Args.run
```

```bash
./build.sh Bootstrap   # deploys Directory.Build.props, required by any mode but Portable
./build.sh Run         # Build → Run with -p:AlmaBuildRuntimeIdentifier=<host rid>
./build.sh Release     # dist/linux-x64/, dist/osx-x64/, dist/osx-arm64/, each zipped to dist/<rid>.zip
```

`RuntimeMode.Specific Linux` pins the local RID instead; `Custom "linux-musl-arm64"` adds a
RID with no named case.

## Executable

A deployable app whose release lands in `app/` inside the repo.

```fsharp
open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "acme-api"
            Summary = "Acme HTTP API."
            Git = Git.init ()
        }
        Specs = Spec.defaultExecutable |> Spec.mapExecutable (fun spec -> { spec with ReleaseDir = "app" })
    }

    args |> Args.run
```

```bash
./build.sh Watch      # full chain through Tests, then dotnet watch run
./build.sh Release    # dotnet publish -c Release -o app
```

## SAFE stack

Expects `src/{Shared,Server,Client}` and `tests/{Shared,Server,Client}`; the argument is the
SAFE template version recorded in `Info` and assembly metadata.

```fsharp
open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "acme-portal"
            Summary = "Acme customer portal."
            Git = Git.init ()
        }
        Specs = Spec.defaultSAFEStackApplication "5.0.3"
    }

    args |> Args.run
```

```bash
./build.sh Run       # server dotnet watch run + client fable watch / vite, interleaved
./build.sh Bundle    # server published to deploy/, client bundle in deploy/public/
```

## Publishing to an organization feed

`NugetApi.Organization` pushes to GitHub Packages of `acme` with `PRIVATE_FEED_PASS`.
With `NUGET_SERVER_TOKEN` set, it then dispatches `update-readme` to `acme/nuget-server`.

```fsharp
open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "Acme.Widgets"
            Summary = "Widget primitives."
            Git = Git.init ()
        }
        Specs =
            Spec.defaultLibrary
            |> Spec.mapLibrary (fun library ->
                { library with
                    NugetApi = NugetApi.Organization "acme"
                    NugetCustomServerRepository = Some "nuget-server"
                }
            )
    }

    args |> Args.run
```

## Integration — build metadata in the application

`AssemblyInfo` writes `AssemblyInfo.fs` next to each project; compile it ahead of the code
that reads it.

```xml
<ItemGroup>
    <Compile Include="AssemblyInfo.fs" />
    <Compile Include="Status.fs" />
</ItemGroup>
```

The generated module is `internal` and sits in namespace `System`, so read it from the same
assembly with `open System`.

```fsharp
open System
open Alma.ApplicationStatus

let assemblyInformation =
    { new ApplicationStatusFeature.IAssemblyInformation with
        member __.GitBranch = GitBranch AssemblyVersionInformation.AssemblyMetadata_gitbranch
        member __.GitCommit = GitCommit AssemblyVersionInformation.AssemblyMetadata_gitcommit
        member __.GitRepository = GitRepository.empty }
```

In CI on a detached HEAD, export `GIT_BRANCH` (or `branch`) so `gitbranch` is not `unknown`;
`BUILD_NUMBER` fills `buildNumber`.

## Extra target

Plain FAKE between `Targets.init` and `Args.run`; the engine's command runner is internal.

```fsharp
open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet

open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "Acme.Widgets"
            Summary = "Widget primitives."
            Git = Git.init ()
        }
        Specs = Spec.defaultLibrary
    }

    Target.create "Docs" (fun _ ->
        let result = DotNet.exec id "fsdocs" "build --clean"
        if not result.OK then failwithf "fsdocs failed: %A" result.Errors
    )

    "Build" ==> "Docs" |> ignore

    args |> Args.run
```

## Adoption and updating

Hand-written files; `Bootstrap` deploys the rest.

`paket.dependencies`:

```paket
group Build
    source https://api.nuget.org/v3/index.json

    nuget Alma.Build 3.0.0
```

`build/paket.references`:

```paket
group Build
    Alma.Build
```

`build/build.fsproj`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
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
```

Seed `.config/dotnet-tools.json` holds only `paket` (`10.3.1`). Then:

```bash
dotnet tool restore
dotnet paket install                                    # writes paket.lock — commit it
dotnet run --project ./build/build.fsproj -- Bootstrap  # deploys build.sh and the rest
./build.sh Solution                                     # writes <Project.Name>.slnx
./build.sh
```

Updating: bump the pin, `dotnet paket install`, `./build.sh Bootstrap`, adjust `Build.fs` per
`CHANGELOG.md` entries marked `[**BC**]`, `./build.sh`, commit `paket.lock` with the deployed files.

## Test project the `Tests` target can run

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
    </PropertyGroup>
    <ItemGroup>
        <Compile Include="Tests.fs" />
    </ItemGroup>
</Project>
```

```fsharp
module Acme.Widgets.Tests

open Expecto

[<Tests>]
let tests =
    testList "Widgets" [
        testCase "should pass when the widget is valid" <| fun _ ->
            Expect.equal 1 1 "a valid widget passes"
    ]

[<EntryPoint>]
let main args =
    runTestsInAssemblyWithCLIArgs [] args
```

Expecto comes from the repo's own Paket group (`paket.references`); the engine runs the
project with `dotnet run --no-build --project tests/Tests.fsproj`.
