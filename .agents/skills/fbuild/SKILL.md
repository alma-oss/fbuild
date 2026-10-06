---
name: fbuild
description: Use whenever generating or reviewing F# code that configures a repository build with the Alma.Build FAKE + Paket engine — a `build/Build.fs` calling `Args.init`, `Targets.init`, `Args.run` with a `ProjectDefinition` (`ProjectMeta`, `Git.init`), a `ProjectSpec` from `Spec.defaultLibrary`, `Spec.defaultExecutable`, `Spec.defaultConsoleApplication`, `Spec.defaultSAFEStackApplication`, overridden through `Spec.mapLibrary` / `mapExecutable` / `mapConsoleApplication` / `mapSAFEStackApplication`. Trigger also on `NugetApi` (`KeyInEnvironment`, `Organization`, `AskForKey`), `RuntimeTarget`, `RuntimeMode` (`Portable`, `AutoDetect`, `Specific`), `PublishSingleFile`, `TestsSources`, the `Bootstrap` / `Solution` / `Release` / `Publish` / `Bundle` targets, `./build.sh`, `no-clean` / `no-lint`, `Directory.Build.props`, `.config/dotnet-tools.json`, or the alma-oss/fbuild repo.
---

# F-Build

Library: [alma-oss/fbuild](https://github.com/alma-oss/fbuild)
NuGet: `Alma.Build`

## Purpose

`Alma.Build` is a versioned FAKE + Paket build engine for F# repositories. A consuming repo pins the package in a Paket `Build` group, owns a single `build/Build.fs` that declares a `ProjectDefinition` (project metadata plus one `ProjectSpec` case), and runs targets through `./build.sh <Target>`. The engine derives the target graph from the spec case and ships its support files (`build.sh`, `.editorconfig`, `fsharplint.json`, `build/README.md`, a rendered `.config/dotnet-tools.json`, and, for a runtime-specific console app, `Directory.Build.props`) as embedded resources that the `Bootstrap` target writes into the repo.

## When to Use

- Writing or reviewing a repo's `build/Build.fs`.
- Choosing a project shape: NuGet library, deployable executable, multi-runtime console app, or SAFE-stack app.
- Configuring NuGet publishing, runtime targets, single-file publishing, or which projects count as sources/tests.
- Adopting the engine in a new repo or bumping its version (`paket.dependencies`, `build/build.fsproj`, `Bootstrap`).
- Diagnosing why a target was skipped, failed up front, or picked the wrong project/solution.

## When NOT to Use

- Application or library runtime code — the engine is build-time only and lives in the Paket `Build` group.
- Repos that do not use Paket: Paket is mandatory, there is no Paket-free variant.
- Running arbitrary commands from `Build.fs` through the engine's runner — the `Command` module is `internal`; use FAKE APIs directly.
- Changing the deployed support files in a consumer repo — `Bootstrap` overwrites them on the next run.

## Main Concepts

- `ProjectDefinition` — `{ Project: ProjectMeta; Specs: ProjectSpec }`, the single value passed to `Targets.init`.
- `ProjectMeta` — `{ Name: string; Summary: string; Git: Git option }`. `Name` drives the library pack directory (`src/<Name>`) and the generated `<Name>.slnx`.
- `Git` / `Git.init ()` — `{ Commit; Branch; Repository }` read from the working copy (`Repository = None`); feeds `Info` and the `AssemblyInfo` git metadata.
- `ProjectSpec` — `Library` | `Executable` | `ConsoleApplication` | `SAFEStackApplication`, each wrapping its own spec record (`LibrarySpec`, `ExecutableSpec`, `ConsoleApplicationSpec`, `SAFEStackApplicationSpec`).
- `Spec.default*` / `Spec.map*` — default constructor per case and a mapper that rewrites that case's record and leaves other cases untouched.
- `IProjectSources` — every spec record exposes `Sources` and `Tests` globs; `Build` is their distinct union and is what `AssemblyInfo`, `Build`, `Lint` and `Solution` work on.
- `NugetApi` — `NotUsed` | `AskForKey` | `Organization of name` | `KeyInEnvironment of envVarName`; drives the library `Publish` target.
- `RuntimeTarget` — `OSX` | `OSXArm64` | `Windows` | `Linux` | `ArmLinux` | `AlpineLinux` | `RaspberryPiHassioAddon` | `Custom of rid`; `RuntimeTarget.value` gives the RID.
- `RuntimeMode` — `Portable` | `AutoDetect` | `Specific of RuntimeTarget`; console-only, selects the RID for local `Build`/`Tests`/`Run`/`Watch`.
- `Targets.init` — registers every FAKE target and the dependency chain for the spec case.
- `Args.init` / `Args.run` — set up the FAKE context, then run the requested target (default `Build`) and return the process exit code.
- `Bootstrap` / `Solution` targets — standalone (outside every chain): deploy support files; generate `<Project.Name>.slnx`.

## Related Libraries

- FAKE (`Fake.Core.Target`, `Fake.IO.FileSystem`, `Fake.DotNet.*`, `Fake.Tools.Git`) — arrives transitively with `Alma.Build`; supplies the glob operators (`!!`, `++`, `--`) used in specs and the `Target` API for any extra target.
- Paket `10.3.1` and `dotnet-fsharplint 0.26.10` — pinned by the engine in the rendered `.config/dotnet-tools.json`; `fable`/`femto` added for SAFE.
- `Alma.ApplicationStatus` — reads the `gitbranch` / `gitcommit` metadata that the `AssemblyInfo` target writes into `AssemblyVersionInformation`.
- Expecto — test projects run by the `Tests` target are executables (`dotnet run`), the Expecto shape.

## Keywords for Search

Alma.Build, fbuild, FAKE, Paket, build.sh, Build.fs, build.fsproj, ProjectDefinition, ProjectMeta, Git.init, ProjectSpec, Library, Executable, ConsoleApplication, SAFEStackApplication, Spec.defaultLibrary, Spec.defaultExecutable, Spec.defaultConsoleApplication, Spec.defaultSAFEStackApplication, Spec.mapLibrary, Spec.mapConsoleApplication, LibrarySpec, TestsSources, LibrarySources, ReleaseDir, ReleaseSource, NugetApi, KeyInEnvironment, AskForKey, Organization, PRIVATE_FEED_PASS, RuntimeTarget, RuntimeMode, AutoDetect, PublishSingleFile, Directory.Build.props, AlmaBuildRuntimeIdentifier, Targets.init, Args.init, Args.run, Bootstrap, Solution, slnx, Release, Publish, ZipRelease, Bundle, Lint, fsharplint, AssemblyInfo, CHANGELOG, no-clean, no-lint, RTK_ACTIVE, dotnet-tools.json, F#

## Reference Files

- For target graphs, spec defaults, publishing, runtime selection, and recommended `Build.fs` composition, read `references/preferred-patterns.md`.
- For known pitfalls, README statements the code contradicts, and incorrect assumptions, read `references/anti-patterns.md`.
- For worked `Build.fs` and adoption examples ordered by increasing complexity, read `references/examples.md`.
