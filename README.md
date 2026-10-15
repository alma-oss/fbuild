# fbuild

`fbuild` is a versioned, distributable FAKE + Paket build infrastructure for F# projects.

This repository ships one deliverable: `Alma.Build` (`src/Alma.Build/`), the build
engine library plus the support files that consuming repositories vendor alongside it.

## How it works

A consuming repository owns a single build file, `build/Build.fs`, which declares a
`ProjectDefinition` (project metadata plus one `Spec` case) and passes it to
`Targets.init`. The engine derives the target graph from the spec:

- `Library`
- `Executable`
- `ConsoleApplication`
- `SAFEStackApplication`

Targets are then run through the bootstrap entry point:

```bash
./build.sh <Target>
```

Common targets (`Library` spec):

- `Build` (default when no target is provided)
- `Lint`
- `Tests`
- `WatchTests`
- `Release`
- `Publish`
- `Info`

`Tests` runs every test suite of the spec once, concurrently; `WatchTests` keeps them running in
watch mode. A spec with Fable tests also gets them run under Vitest; see [Fable tests](#fable-tests).
`InstallNpm`, ahead of `Build`, runs `npm install` when the spec has Fable tests or is a
`SAFEStackApplication`.

`Bootstrap` and `Solution` sit outside the chain — run them explicitly. `Solution`
generates `<Project.Name>.slnx` at the repo root from the project's declared source
and test projects, overwriting it on every run — like `Bootstrap`, rerun it whenever
the project's structure changes.

Arguments:

- `no-clean`: skips the `Clean` step.
- `no-lint`: skips the `Lint` step.

```bash
./build.sh Build no-lint
```

## Fable tests

Code that Fable compiles is tested under [Vitest](https://vitest.dev), alongside the Expecto tests
on .NET. Each Fable test suite is a project Fable compiles into its `output/` directory, whose entry
module Vitest then runs; `WatchTests` runs it in Vitest's watch mode. The suite's config,
`vitest.config.mts`, is one of the files `Bootstrap` writes
([step 4](#4-bootstrap-the-support-files)): commit it. `package.json` needs `vitest` in its
`devDependencies`, plus `jsdom` for a `SAFEStackApplication`.

A suite has one entry module, which calls `register` once on the suite's whole test list; every
other test module adds its tests to that list rather than registering its own:

| Suite | Entry | Vitest environment |
| --- | --- | --- |
| library `tests/Shared` | `Shared.Tests.fs` | node |
| library `tests/Fable` | `Fable.Tests.fs` | jsdom |
| SAFE-stack `tests/Client` | `Client.Tests.fs` | jsdom |

Write the tests with [`Alma.Testing`](https://github.com/alma-oss/ftesting), which keeps Expecto's
API: under Fable its `register` hands the list to Vitest, and on .NET it does nothing while every
other name is Expecto's, so a test shared by both sides needs no `#if`. A failing assertion points
at its `.fs` line. A test failed by `failwith` reports only its message, so use `failtest` in tests.

### Library

A library chooses its tests when it creates its spec in `build/Build.fs`; there is no default:

```fsharp
Spec.library LibraryTests.dotNetAndFable
|> Spec.mapLibrary (fun library -> { library with NugetApi = NugetApi.KeyInEnvironment "NUGET_API_KEY" })
```

| Value | Layout | Runs |
| --- | --- | --- |
| `LibraryTests.noTests` | none | nothing |
| `LibraryTests.dotNet` | `tests/*.fsproj`, or `LibraryTests.DotNet (!! "tests/*/*.fsproj")` | Expecto on .NET |
| `LibraryTests.dotNetAndFable` | `tests/Shared` and `tests/DotNet` | `Shared` on .NET and, compiled by Fable, under Vitest in node; `DotNet` on .NET |
| `LibraryTests.fable` | `tests/Fable` | Vitest in jsdom, for a library that only exists under Fable |

The build fails when the chosen layout lacks a test project. A library that compiles to both .NET and
Fable uses `shared`; its `DotNet` tests only gather the Shared list and add what cannot run under Fable,
such as FsCheck properties. Code that exists only under Fable belongs in a library of its own, tested
with `fable`.

```
package.json           # vitest as a dev dependency (and jsdom for LibraryTests.fable)
tests/
  Shared/              # tests of code that runs on both sides, one <Area>.fs per area; Shared.Tests.fs registers the list
  DotNet/              # Expecto executable: gathers the Shared list, adds .NET-only tests
```

A `fable` library has `tests/Fable` alone, `<Area>.fs` files and the entry `Fable.Tests.fs`, which
registers the list.

Expecto discovers `[<Tests>]` only in the entry assembly, so `tests/DotNet` references the Shared
project and marks its list:

```fsharp
// tests/DotNet, the Expecto entry point
open Expecto

[<Tests>]
let sharedTests = Shared.Tests.tests

[<EntryPoint>]
let main argv = Tests.runTestsInAssemblyWithCLIArgs [ Parallel ] argv
```

Moving an existing library onto the `shared` layout is covered step by step in
[docs/migration/fable-tests/SKILL.md](docs/migration/fable-tests/SKILL.md);
`bin/migrate-library-fable-tests` in this repository does its mechanical part.

### SAFE-stack application

The client tests in `tests/Client` run in Vitest's jsdom environment, so they can read
`import.meta.env` through Vite and render into a DOM:

| Tests | Written with | Runs on |
| --- | --- | --- |
| client logic and views | `Alma.Testing` | Vitest |
| shared code | `Alma.Testing` | Vitest, and Expecto on .NET |
| server | Expecto | .NET |

`Client.Tests.fs`, the SAFE template's runner, registers the client and shared tests; include the
shared tests in the server's Expecto entry too:

```fsharp
// tests/Client/Client.Tests.fs
let all = testList "All" [ Router.Tests.tests; Shared.Tests.tests ]

register all
```

```fsharp
// tests/Server, the Expecto entry point
[<Tests>]
let sharedTests = Shared.Tests.tests

[<EntryPoint>]
let main argv = Tests.runTestsInAssemblyWithCLIArgs [] argv
```

### Moving from Fable.Mocha

`Tests` stops while `tests/Client/paket.references` still lists Fable.Mocha: in Vitest's jsdom
environment it renders its results instead of registering tests. `Alma.Testing` keeps its API, so
the move is a find-and-replace. From the project root, `bin/migrate-safe-stack-tests` in this
repository does it: `open Fable.Mocha` (and the `#if FABLE_COMPILER` block around it) becomes
`open Alma.Testing`, `Mocha.runTests tests` becomes `register tests` (a runner not named
`Client.Tests.fs` is renamed to it), the package is swapped in `paket.references` and `paket.dependencies`, the page and
Vite config that served Fable.Mocha's tests in a browser are deleted, and the shared tests are
added to the server's Expecto entry point. It then runs `dotnet tool restore`, `dotnet paket
install`, the npm changes, `Bootstrap` and `Tests`, and checks the result, printing only
`migration-report.md`, which it keeps with its log in the `--work-dir` you pass it (create one outside the
repository with `mktemp -d`). Run it on a clean working tree; after a failure, or after doing what it asked for
first, rerun it with `--rerun`. The procedure, for libraries too, is the skill in
[docs/migration/fable-tests/SKILL.md](docs/migration/fable-tests/SKILL.md).

Alma.Testing needs Vitest 5, which needs Vite ^6.4 or later. `bin/upgrade-vite` moves a project onto Vitest 5
and a SAFE-stack client onto Vite 8 with @vitejs/plugin-react 6, renames the Vite options Vite 8 replaced, and
compares the client bundle with the one before; `bin/migrate-safe-stack-tests --upgrade-vite` does the same
during the move from Fable.Mocha.

## Prerequisites

- .NET SDK with `net10.0` support.
- `bash`.
- `git` (required by build metadata initialization).
- `npm` and Node, when the project has Fable tests or is a SAFE-stack application.

## Adoption

The engine is a normal NuGet package plus a handful of support files that live in the
consuming repo; the engine's `Bootstrap` target deploys those files, so only the paket
pin and the build project are written by hand.

A repository on the hand-copied FAKE build files moves over with `bin/migrate-to-alma-build`
from this repository, run inside it: it pins `Alma.Build ~> 4.0`, removes the local engine
modules, gives the library spec its tests, runs `Bootstrap` and `Solution`, and runs the tests.

### 1. Pin the engine

`paket.dependencies` at the repo root:

```paket
group Build
    source https://api.nuget.org/v3/index.json

    nuget Alma.Build <current_version>
```

`build/paket.references`:

```paket
group Build
    Alma.Build
```

`.config/dotnet-tools.json` holds a seed, enough to restore Paket and reach the engine.
`Bootstrap` rewrites it from the engine's own pins in step 4, so the versions here
only have to work once:

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "paket": { "version": "10.3.1", "commands": [ "paket" ] }
  }
}
```

### 2. Add the build project

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

The `Paket.Restore.targets` import makes a plain `dotnet build`/`dotnet run` trigger
Paket's restore.

`build/Build.fs` is the only build file the repository owns and edits.

### 3. Restore

Generate `paket.lock` once:

```bash
dotnet tool restore
dotnet paket install   # writes paket.lock — commit it
```

### 4. Bootstrap the support files

The files are bundled inside the engine assembly; its `Bootstrap` target deploys them
into the repo, preserving their directory structure:

```bash
dotnet run --project ./build/build.fsproj -- Bootstrap
```

| File                        | Purpose                                                |
| --------------------------- | ------------------------------------------------------ |
| `build.sh`                  | entry point: restores tools + packages, runs the build |
| `.config/dotnet-tools.json` | the repo's local .NET tools, rendered per spec         |
| `.editorconfig`             | formatting rules the engine's `Lint` target assumes    |
| `fsharplint.json`           | lint configuration                                     |
| `build/README.md`           | consumer-facing quick reference                        |
| `Directory.Build.props`     | only when the spec selects a runtime — carries it into each project |
| `<suite>/vitest.config.mts` | one per Fable test suite — `tests/Client` for a `SAFEStackApplication`, `tests/Shared` or `tests/Fable` for a library with Fable tests ([Fable tests](#fable-tests)) |

`Bootstrap` overwrites existing copies and marks `build.sh` executable. The files are
version-locked to the engine and some follow from the spec, so rerun `Bootstrap` after a
version bump (how engine-side changes to lint rules, formatting, or the entry point reach
the repo) and after changing the spec. To keep a hand edit of one of the files, restore it
from git after `Bootstrap` (`git checkout -- <file>`), and fold in whatever the run changed.

`Directory.Build.props` is deployed only when `RuntimeMode` is not `Portable`. `dotnet build`
rejects a solution that carries a runtime identifier and no project can restate one given on the
command line, so the engine passes the runtime under its own property name and this file assigns
it to `RuntimeIdentifier` inside each project — where your own `Condition="'$(RuntimeIdentifier)'
== '…'"` items keep working. Commit it; while it is missing the build stops and tells you to run
`Bootstrap`. Like the other files it is overwritten, so fold your own MSBuild properties back in
after a version bump.

`.config/dotnet-tools.json` is rendered from the project spec instead of bundled:

| Tool                | Specs                  |
| ------------------- | ---------------------- |
| `paket`             | all                    |
| `dotnet-fsharplint` | all                    |
| `fable`             | `SAFEStackApplication` |
| `femto`             | `SAFEStackApplication` |

It is overwritten like the rest, so a tool added by hand is lost on the next run.

### 5. Build

From here on the entry point is enough:

```bash
./build.sh
```

### Console application runtime settings

`Spec.defaultConsoleApplication` defaults to portable `Build`, `Run`, and `Watch`
targets and publishes one self-contained single-file artifact per supplied runtime target.
Use `Spec.mapConsoleApplication` when the local build should target a runtime or when
the release should keep its files unbundled:

```fsharp
Specs =
    Spec.defaultConsoleApplication [ Linux; Windows ]
    |> Spec.mapConsoleApplication (fun spec ->
        { spec with
            RuntimeMode = RuntimeMode.AutoDetect
            PublishSingleFile = false
        }
    )
```

`RuntimeMode.AutoDetect` selects the RID reported by the running .NET runtime;
`RuntimeMode.Specific runtimeTarget` selects the given target. Both pass the resolved RID
as `-r <runtimeIdentifier>` to `Build`, `Tests`, `Run`, and `Watch`, including their
Mirrord variants. A `Specific` target must occur in `RuntimeTargets`; `AutoDetect` is
independent of the release matrix. `Release` always publishes every target in
`RuntimeTargets`; `PublishSingleFile = false` keeps the published files separate.

Use `Custom "linux-musl-arm64"` for a RID without a predefined `RuntimeTarget` case.

## Updating

1. Bump the version in `paket.dependencies`.
2. `dotnet paket install`.
3. `./build.sh Bootstrap` to redeploy the support files.
4. If the engine's `Spec`/`Targets` surface changed, adjust `build/Build.fs` per the
   release notes in `CHANGELOG.md`.
5. Run `./build.sh` and commit `paket.lock` with the rest.

## Further reading

- [`docs/specs/fbuild/spec.md`](docs/specs/fbuild/spec.md): architecture and design reference.
- `build/README.md`: consumer-facing quick reference shipped with the engine.
- [`CONTRIBUTING.md`](CONTRIBUTING.md): working on the engine itself.
