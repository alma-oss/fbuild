# Spec: fbuild — Versioned, Distributable F# Build Infrastructure

> Spec and architecture reference for the repository. Describes the current
> state; §12 tracks what is unresolved.

## 1. Objective

Ship a shared **FAKE + Paket build engine** for the company's F# repositories
that is versioned and distributable — so that build infrastructure improvements
propagate through a package version bump instead of copy-pasting build logic
between repos.

**Users:** F# repository authors inside the organization (target scale ~50–100
repos; onboarding starts around 10).

**Success looks like:**

- A repo gets a working build by pinning one package and running the engine's
  `Bootstrap` target, which deploys a small, fixed set of support files.
- An existing repo pulls an engine improvement by bumping one version and
  rerunning `Bootstrap`.
- `Build.fs` is the only per-project build file an author owns and edits.
- Breaking engine changes are visible and migratable, not silent.

### Deliverable

| Artifact | Source | Packaged as |
| --- | --- | --- |
| **`Alma.Build`** | `src/Alma.Build/` | NuGet library; the support files are embedded resources in the assembly |

Versioned off a single source of truth: `Version` in
`src/Alma.Build/Alma.Build.fsproj`.

## 2. Tech stack

- **.NET SDK `net10.0`** — all projects target it.
- **F#** — engine and self-host build.
- **FAKE** (`Fake.Core.Target`, `Fake.DotNet.Cli`, `Fake.IO.FileSystem`,
  `Fake.IO.Zip`, `Fake.Core.UserInput`, `Fake.DotNet.AssemblyInfoFile`,
  `Fake.Core.ReleaseNotes`, `Fake.Tools.Git`) — the target graph. Consumed via
  Paket in the main group: the engine ships FAKE onward as a package dependency,
  so it is the library's own dependency and not build-only tooling.
- **Paket `10.3.1`** — mandatory dependency manager for every build project;
  restored from `.config/dotnet-tools.json`, which the engine pins and renders.
- **`dotnet-fsharplint 0.26.10`** — the `Lint` target.
- **`System.Text.Json`** — renders `.config/dotnet-tools.json`. Part of the
  `net10.0` shared framework, so no package reference.
- **Expecto `~> 10.2`** — both test suites (`tests/unit`, `tests/integration`),
  through Paket's `Tests` group.
- **`FSharp.Core ~> 10.0`** — main group, and listed in the engine's
  `paket.references` so the packed nuspec declares it as
  `[<locked version>, 11.0.0)`. The locked version is the one the engine assembly
  binds to, and a consumer's `Build` group cannot resolve below it. Without the
  declaration the nuspec only carries FAKE's `>= 8.0.400`; a consumer whose lock
  already holds an older FSharp.Core keeps it across `paket install` and fails at
  startup with `FileNotFoundException` on `FSharp.Core`.
- **`Microsoft.Build.* >= 18.0 < 18.10`** — main group and the engine's
  `paket.references`, so consumers resolve the last `net10.0`-targeting MSBuild
  packages (§12).
- **bash** and **git** — required at build time (`build.sh`, and `Git.init`
  shells out to `git rev-parse`).
- **Node/npm** — required by the SAFE-stack project type, and therefore by the
  integration tests' `safe` fixture.
- **RTK** (optional) — when `RTK_ACTIVE` is set the engine routes commands
  through `rtk` and compacts their output; see §5.5. Absent, output is
  byte-identical to a direct spawn.

## 3. Commands

Repository-root commands:

```bash
# Default build target (self-host)
dotnet run --project ./build/build.fsproj -- Build

# Any target
dotnet run --project ./build/build.fsproj -- <Target>

# Skip clean / skip lint
dotnet run --project ./build/build.fsproj -- Build no-clean no-lint

# Standard wrapper: restores tools + paket, then runs the build
./build.sh <Target>

# Either test suite on its own, without the target graph
dotnet run --project tests/unit/unit.fsproj --
dotnet run --project tests/integration/integration.fsproj --
```

A target may be given bare or after `-t`; both `./build.sh Release no-clean`
and `./build.sh -t Release no-clean` run `Release`. Everything after the target
is a build argument.

Consumer-repo command surface is one script:

```bash
./build.sh <Target>
```

### Target graph

Targets are defined in `src/Alma.Build/Targets.fs`. Shared targets: `Info`,
`Bootstrap`, `Solution`, `Clean`, `AssemblyInfo`, `Build`, `Lint`, `Tests`,
`Release`, `Publish`, `ZipRelease`, `Run`, `Watch`, `RunMirrord`, `WatchMirrord`.

`Bootstrap` sits outside every chain: run explicitly, it extracts the support
files bundled in the engine assembly into the working directory and writes the
files that follow from the project spec (§5.4).

`Solution` also sits outside every chain: run explicitly, it generates the
repo's `.slnx` from the project's declared sources, overwriting it on every
run (§5.6).

| Spec | Chain |
| --- | --- |
| `Library` | `Clean → AssemblyInfo → Build → Lint → Tests → Release → Publish` |
| `Executable` | `Clean → AssemblyInfo → Build → Lint → Tests → Release ⟺ Watch ⟺ Run …` |
| `ConsoleApplication` | `Clean → AssemblyInfo → Build → Lint → Tests → Release → ZipRelease` |
| `SAFEStackApplication` | `SafeClean → AssemblyInfo → InstallClient → Build → Lint → Tests → Bundle`; plus `Run`/`RunMirrord`/`WatchTests` |

Build arguments: `no-clean` skips `Clean`; `no-lint` skips `Lint`. Default
target when none is given is `Build`, for every spec case.

`AssemblyInfo` and `Build` work from `Sources.Build`, which covers the
consumer's own projects; `build.sh` has already built `build/build.fsproj`
before any target runs, so the harness is not one of them. `Lint` adds
`build/*.fsproj` back on top of that set.

`Build` prefers a solution in the repository root — `.slnx` over `.sln` — and
hands MSBuild that one file; with no solution there it runs once per directory
in `Sources.Build`. A configured runtime reaches the projects the same way in
either case (§5.3).

`Lint` runs one `fsharplint` job per project concurrently and prints each job's
output as a single block once every job has finished (`GroupedByJob`, §5.5).
The SAFE targets pair a server job with a client job and run them with live
interleaved output (`Bundle`, `Run`, `RunMirrord`, `Tests`, `WatchTests`).
Every other target is serial.

## 4. Project structure

### This repository

```
CHANGELOG.md            # release notes; parsed by the AssemblyInfo target
CONTRIBUTING.md         # working on the engine itself
fbuild.slnx             # solution: src/Alma.Build, build, both test projects
paket.dependencies      # main group → the engine's own dependencies; Tests group → Expecto
paket.lock              # authoritative version pin
build.sh                # symlink to bootstrap/build.sh — also this repo's entry point
fsharplint.json         # symlink to bootstrap/fsharplint.json
.editorconfig           # symlink to bootstrap/.editorconfig

bootstrap/              # BOOTSTRAP ASSETS — embedded into the engine assembly,
                        # symlinked into the repo because it is its own first consumer
  build.sh              #   entry point: restores tools + packages, runs the build
  fsharplint.json       #   lint configuration
  .editorconfig         #   formatting rules the Lint target assumes
  build/README.md       #   consumer-facing quick reference (deploys to build/)

src/Alma.Build/         # ENGINE — packed as the Alma.Build NuGet
  RtkFilter.fs          #   internal — per-command output filters (§5.5)
  Command.fs            #   internal — command vocabulary, RTK transport,
                        #   serial + parallel runners, failure-log tee
  Utils.fs              #   ProjectDefinition / Spec / Git / RuntimeTarget model,
                        #   Args, Solution, Nuget, Http, Github helpers
  DotnetTools.fs        #   tool pins + `.config/dotnet-tools.json` rendering (§5.4)
  Bootstrap.fs          #   internal — bundled-resource extraction, the deployed
                        #   file table, and the Bootstrap deployment (§5.4)
  Targets.fs            #   the FAKE target graph for every project type
  runtime/              #   embedded, deployed per spec (§5.4)
    Directory.Build.props #  carries the RID into each project (§5.3)
  Alma.Build.fsproj     #   Version + metadata; embeds bootstrap/** as resources
  paket.references      #   FSharp.Core, FAKE, the Microsoft.Build.* cap — the packed nuspec's deps

build/                  # SELF-HOST runner
  Build.fs              #   Library spec over src/Alma.Build
  build.fsproj          #   ProjectReference to src/Alma.Build
  paket.references      #   FSharp.Core (main group)
  README.md             #   symlink to bootstrap/build/README.md

tests/
  unit/                 # Expecto; reaches the internal modules via InternalsVisibleTo
  integration/          # Expecto; one end-to-end scenario per spec case, plus one
                        # that consumes the engine as a package
    fixtures/           #   a consumer repo per case, Build.fs included, Bootstrap files excluded

.github/workflows/      # pr-check, tests, publish
release/                # packed artifacts (git-ignored)
docs/                   # this document and design notes
```

Git-ignored: `bin/`, `obj/`, `release/`, `.paket/`, `packages/`, `paket-files/`,
`.fake/`, `.ionide/`, and generated `AssemblyInfo.fs`.

### A consuming repository

```
.config/dotnet-tools.json   # rendered by Bootstrap, per spec
paket.dependencies     # feed sources + group Build → Alma.Build <version>
paket.lock             # pinned versions
build.sh               # entry point (deployed by Bootstrap)
fsharplint.json        # deployed by Bootstrap
.editorconfig          # deployed by Bootstrap
Directory.Build.props  # deployed by Bootstrap, only when the spec selects a runtime
build/
  build.fsproj         # imports Paket.Restore.targets
  paket.references     # Alma.Build
  Build.fs             # author-owned
  README.md            # deployed by Bootstrap
src/ …                 # application code
```

Adoption steps are in `README.md`.

## 5. Architecture

### 5.1 Distribution model

The engine is a plain NuGet package; everything a repo needs beyond it is a
small set of files committed to that repo. The repository author writes
`paket.dependencies`, `build/paket.references`, a seed
`.config/dotnet-tools.json` holding Paket, and a `build.fsproj` that imports
`Paket.Restore.targets`, then runs the engine's `Bootstrap` target
(`dotnet run --project ./build/build.fsproj -- Bootstrap`), which deploys the
support files. From then on `build.sh` does the rest on every run:

```bash
dotnet tool restore
dotnet tool run paket restore
FAKE_DETAILED_ERRORS=true dotnet run --project ./build/build.fsproj -- "$@"
```

Because `build.fsproj` imports `Paket.Restore.targets`, even a bare
`dotnet build`/`dotnet run` triggers Paket's MSBuild-integrated restore. Paket
is mandatory across all build projects — there is no Paket-free variant.

### 5.2 Engine package — `Alma.Build`

- Compiled engine (`RtkFilter.fs`, `Command.fs`, `Utils.fs`, `DotnetTools.fs`,
  `Bootstrap.fs`, `Targets.fs`).
- **Also carries the non-compiled assets** as embedded resources — the fsproj
  globs `bootstrap/**` with logical names that keep the relative path. The
  `Bootstrap` target extracts them into the consuming repo, which makes them
  **version-locked to the engine**: a version bump plus a `Bootstrap` run is
  what delivers a new lint config or entry point.
- Semver, single source: `Version` in `src/Alma.Build/Alma.Build.fsproj`.
- Distributed through public nuget.org — the production channel for every
  consuming repo.
- Escape hatches: override targets from `Build.fs` via `Spec.map*`, or vendor
  the engine source outright.

### 5.3 The project model

`ProjectDefinition = { Project: ProjectMeta; Specs: ProjectSpec }`.

`ProjectSpec` cases and their constructors:

| Case | Default constructor | Key fields |
| --- | --- | --- |
| `Library` | `Spec.defaultLibrary` | `ReleaseDir`, `NugetApi`, `Organization`, `NugetCustomServerRepository` |
| `Executable` | `Spec.defaultExecutable` | `ReleaseDir = ./app` |
| `ConsoleApplication` | `Spec.defaultConsoleApplication runtimeTargets` | `RuntimeTargets`, `RuntimeMode = Portable`, `PublishSingleFile = true`, `ReleaseSource`, `ReleaseDir = ./dist` |
| `SAFEStackApplication` | `Spec.defaultSAFEStackApplication templateVersion` | Shared/Server/Client + test paths, `DeployPath` |

Every case implements `IProjectSources` (`Sources` / `Tests` / `Build` globs) and
each has a `Spec.map*` function for overriding fields from `Build.fs`.

`NugetApi = NotUsed | AskForKey | Organization of name | KeyInEnvironment of
envVarName` drives the `Publish` target. The consumer-facing `RuntimeTarget` is
`OSX | OSXArm64 | Windows | Linux | ArmLinux | AlpineLinux |
RaspberryPiHassioAddon | Custom of string`. The engine resolves targets to its internal
`RuntimeIdentifier` string wrapper before comparison or command construction.

`ConsoleApplication.RuntimeMode` controls the runtime supplied to local `Build`, `Tests`,
`Run`, and `Watch` targets, including their Mirrord variants: `Portable` supplies none,
`AutoDetect` uses the RID reported by the running .NET runtime, and `Specific runtimeTarget`
supplies the given target. Both `AutoDetect` and `Specific` must resolve to a RID listed in
`RuntimeTargets`, or resolution fails with `UnsupportedRuntime`; only `Portable` skips this
check. `ProjectSpec.RuntimeConfiguration` exposes this capability
without making target initialization depend on a particular project-spec case.

A resolved runtime reaches MSBuild as `-p:AlmaBuildRuntimeIdentifier`, which the deployed
`Directory.Build.props` assigns to `RuntimeIdentifier` inside each project. `RuntimeIdentifier`
itself cannot go on the command line: a solution build fails
`_CheckForSolutionLevelRuntimeIdentifier` (`NETSDK1134`) whenever it is set and the check has no
opt-out, and a command-line value — `-r` or `-p:RuntimeIdentifier` alike — is a global property
no project can restate. MSBuild imports the props file into every project below it and not into
the metaproject, so the solution carries no RID while every project of it does, and
`$(RuntimeIdentifier)` keeps its ordinary meaning where projects read it: a consumer's own
`Condition="'$(RuntimeIdentifier)' == '…'"` items still fire. `Build` uses the one spelling
whether it hands MSBuild a solution or the projects of `Sources.Build`.

`withRuntimeIdentifier` appends the property to `Build`, `Tests`, `Run`, and `WatchRun` only;
`takesRuntimeIdentifier` is the predicate behind that and behind the guard: the property is
inert until the props file translates it, so a build that resolved a RID and finds no
`Directory.Build.props` fails up front, naming the `Bootstrap` target.

The `Pack` and `Publish` commands behind `Release` and `Publish` never carry a configured RID: a
console `Release` passes its own `-r` per runtime target, project-scoped, where `-r` is legal.
`Release` publishes every `RuntimeTargets` entry in parallel, each into its own intermediate
directory so the targets do not overwrite one another's `obj/project.assets.json`.
`PublishSingleFile` controls the `PublishSingleFile` MSBuild property for those self-contained
releases.

### 5.4 Bootstrap support files

`build.sh`, `fsharplint.json`, `.editorconfig`, and `build/README.md` live in
`bootstrap/` and are embedded into the engine assembly with their directory
structure. The `Bootstrap` target takes the bundled resources under that prefix
(`BundledResource`) and writes each into the consuming repo at its relative
path, overwriting what is there and marking `*.sh` executable. The files are
committed to the consumer, so a repo can diverge locally; the cost of diverging
is that the next `Bootstrap` run overwrites the local edit.

This repository consumes them through symlinks instead of copies — the repo is
its own first consumer, so the checkout and the bundled payload cannot drift.

`Bootstrap` then walks `deployedFiles`, the files whose presence or contents
follow from the project spec rather than from the `bootstrap/` glob. Each entry
is a `DeployedFile` — a path plus a `ProjectSpec -> DeployedContents option`,
where `None` keeps the file out of a repo that has no use for it and
`DeployedContents` says whether the bytes are `Bundled` (streamed from a
resource) or `Rendered` (text computed from the spec).

Two files are deployed this way. `.config/dotnet-tools.json` is `Rendered` from
`DotnetTools.tools` for every spec: all of them get `paket`, which `build.sh`
restores before any target runs, and `dotnet-fsharplint`, which backs `Lint`;
`SAFEStackApplication` also gets `fable`, which `SafeClean`, `Bundle`, `Run` and
`WatchTests` invoke, and `femto`, which syncs the npm side of the SAFE template.
`Directory.Build.props` is `Bundled`, and only for a spec whose `RuntimeMode`
resolves to a runtime at all — every mode but `Portable` (§5.3). Like the
bundled files both are overwritten, so a tool or a property added by hand is
lost on the next run.

`DotnetTools` and `ToolsManifest` live in `src/Alma.Build/DotnetTools.fs`, which
holds the version pins. The manifest is serialised from a DTO by
`System.Text.Json` under `JsonNamingPolicy.CamelCase`, the casing the dotnet CLI
expects for `version`, `isRoot` and `tools`. The tools are a `Dictionary`, so
each tool name stays a verbatim key — the naming policy leaves dictionary keys
alone, and `dotnet-fsharplint` is not a valid property name. Insertion order is
preserved, so the manifest lists the tools in the order `DotnetTools.tools`
declares them.

`Bootstrap` finishes by printing a reminder to run the `Solution` target
(§5.6), since a fresh consumer would otherwise not know that target exists.

### 5.5 Command execution and output filtering

No target shells out directly. Every external process goes through the
`Command` module as a typed `Command`:

```fsharp
type Command =
    | Dotnet of DotnetCommand   // Build Restore Lint Tests Pack Publish
                                // Fable FableWatch Run WatchRun
    | Nuget of NugetCommand     // Push AddSource
    | Npm of NpmCommand         // Install Version
    | Mirrord
    | Raw of cmd: string
```

`Command.render` maps a case to an executable plus its verb; `run` executes one
serially and raises on a non-zero exit code, `toJob` + `runParallelWith`
executes several concurrently. A parallel run sequences output either `Live`
(interleaved as the processes write, the only option when a job never exits) or
`GroupedByJob` (captured, then printed one block per job in job order — every
job must terminate).

**RTK integration.** `RTK_ACTIVE` in the environment switches the whole layer
on. Each command then picks a transport (`rtk err`, `rtk test`, or a direct
spawn) and, where one exists, a `RtkFilter.Filter` that reduces the captured
output: MSBuild diagnostics deduped and made repo-relative for
`build`/`restore`/`pack`/`publish`, one compact `file:line:col FLxxxx message`
per warning for `fsharplint`, a `compiled N/M in Xms` tally for Fable. A filter
that keeps nothing on a clean exit collapses to a single `<command>: ok` line;
a *failing* run whose error the filter's whitelist did not match falls back to
the raw output, so nothing is ever summarised away when it matters. Where a
failing run did have output suppressed, the full stdout is teed to a rotated
log under `$TMPDIR/fake-rtk-tee/` and a `[full output: <path>]` hint appended.
`CompactTrace` separately mutes FAKE's own dependency-graph and running-order
chatter.

When `RTK_ACTIVE` is unset, every command resolves to a direct spawn with no
filter, so output is byte-identical to running the tool by hand. `Args.run`
returns exit code 1 on a failed build either way.

### 5.6 Solution file generation

`Solution.current` (§3, the `Build` target) only *chooses among* `.slnx`/`.sln`
files that already exist. The `Solution` target complements it by *generating*
`<Project.Name>.slnx`, so a fresh consumer gets a working IDE solution without
hand-authoring it.

- Runs standalone, outside every target chain, the same way `Bootstrap` does.
- Always writes `<Project.Name>.slnx` at the repo root, overwriting whatever is
  there — like `Bootstrap`'s support files, it is meant to be rerun whenever the
  project's structure changes, and the rewritten file is committed.
- Collects every project the engine already knows about — the current
  `ProjectDefinition`'s `Sources.Build` — and groups them into one
  `<Folder Name="/<top-level-dir>/">` per distinct top-level path segment
  among those projects, each folder's `<Project>` entries sorted by path. A
  project that sits directly at the repo root, with no directory component,
  is listed unwrapped, outside any folder. The internal `Solution.relativeTo`
  does the path normalization — forward slashes on every OS, and a `../`
  segment for a project outside the root — and the internal `Solution.render`
  the grouping; both are pure, so the shape is unit-tested with no filesystem.
- `build/build.fsproj` is therefore absent from the generated solution, for the
  reason it is absent from `Sources.Build` (§3): `build.sh` has already built
  the harness before any target runs, so listing it would make every
  `dotnet build <solution>` recompile it. The hand-written `fbuild.slnx` does
  carry it; a generated solution is not expected to match it project for
  project.
- An empty `Sources.Build` renders no file, and the target traces why, rather
  than overwriting a real solution with an empty `<Solution>`.
- Writes the result to `<Project.Name>.slnx` at the repo root, matching the
  naming convention this engine's own consumers already use by hand
  (`fbuild.slnx`, `TucConsole.slnx`, `localities-adminConsole.slnx`), rather
  than a fixed generic filename.
- If `Solution.current` (§3) finds a *different* file already at the repo
  root — a differently named `.slnx`, or any `.sln` — the target warns before
  writing: two solution files leaves `Build`'s pick between them unspecified,
  so at most one should stay committed.

Out of scope: preserving manual `<Folder>` reshuffling or other hand edits
across a regeneration — a rerun always replaces the file's content wholesale.

## 6. Workflows

**New repo:** follow the adoption section in `README.md` — pin the package, add `build/`, run
`Bootstrap`, `./build.sh`.

**Update (pull model — each repo on its own schedule):** bump the pin in
`paket.dependencies` → `paket install` → `./build.sh Bootstrap` to redeploy the
bootstrap assets → if the engine API changed, the author edits `Build.fs` per the
release notes → CI runs the build → merge when green.

**Override / fork:** override a target from `Build.fs` via `Spec.map*`, or
vendor the engine source.

**Engine development in this repo:** `build/build.fsproj` references the engine
project directly, so engine edits reach the self-host build on the next run with
nothing to repack. Then run the suites that cover what changed (§8).

**Release:** bump `Version` in `Alma.Build.fsproj` and `CHANGELOG.md`, merge,
then push a `X.Y.Z` tag — `publish.yaml` packs and pushes to nuget.org from
there.

## 7. Code style

Match the surrounding file. The engine is FAKE-idiomatic F#.

Formatting is enforced by `.editorconfig` (4-space indent, Stroustrup multiline
brackets, `fsharp_max_record_width = 60`, space before uppercase invocation)
and by `fsharplint.json` via the `Lint` target.

Representative engine style — records + globbing patterns, pipeline-first,
pattern matching on the spec case:

```fsharp
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
        BuildSources = sources ++ "tests/*.fsproj"
        Organization = None
        NugetApi = NugetApi.NotUsed
        NugetCustomServerRepository = None
    }
```

Conventions in force:

- `[<RequireQualifiedAccess>]` on helper modules (`Args`, `Option`, `Solution`,
  `Nuget`, `Spec`, `Git`, `RuntimeTarget`, `NugetApi`, `Http`, `Github`,
  `Command`, `Rtk`, `CompactTrace`, `Filter`, `JobName`, `ExitCode`,
  `CapturedOutput`).
- Companion `default*` / `map*` pairs per spec case; adding a case means adding
  both.
- Single-case wrappers (`JobName`, `ExitCode`, `CapturedOutput`, `OutputLine`)
  around the values the runner and filters pass around, each with a companion
  module exposing `value`.
- `///` doc comments only where the signature can't carry the fact (units,
  preconditions, side effects). Section dividers use the long `// ---- …` rule.
- `orFail`/`failwith` are used deliberately in the build engine, where a failure
  must abort the build and no caller can handle an error.
- Compile order is explicit in the `.fsproj` and is part of the design:
  `RtkFilter → Command → Utils → DotnetTools → Bootstrap → Targets`. `RtkFilter`,
  `Command`, and most of `Bootstrap` are
  `internal`; the unit test assembly reaches them through
  `InternalsVisibleTo("Alma.Build.Tests")` on the engine project.

## 8. Testing strategy

Two Expecto suites, both run by the `Tests` target (this repo's `TestsSources`
is `tests/*/*.fsproj` — one level only, so integration fixture projects are
not mistaken for tests).

**Unit — `tests/unit/`.** Covers the pure parts the rest of the engine is built
on: the output filters (`RtkFilterTests`), the command vocabulary, transport
selection, tee decision and trace compaction (`CommandTests`), runtime resolution
and solution rendering (`UtilsTests`), the tool pins and manifest rendering
(`DotnetToolsTests`), the
deployed-file table (`BootstrapTests`), and runtime command construction
(`TargetsTests`). Fast, spawns no processes.

**Integration — `tests/integration/`.** One or more independent cases exercise each
`ProjectSpec` shape. Each case copies its checked-in fixture repo out of
`tests/integration/fixtures/` into a throwaway directory, completes it into a
runnable consumer repo (a generated `build.fsproj`, `git init`, then the engine's
own `Bootstrap` target for the support files), drives the requested target
through the engine's own entry point, and asserts its behavior. `Bootstrap` runs
before `dotnet tool restore`, since it renders the manifest the restore reads, so
no fixture carries a `.config/dotnet-tools.json` — or a `Directory.Build.props` —
of its own; both arrive from the target under test. A case can hand the prepared
copy to an edit callback before the target runs (`withEditedFixture`), which is how
one fixture covers both shapes a target branches on. Generated project references
give every fixture copy isolated engine `bin`/`obj` paths, so cases can run concurrently:

| Fixture | Target | Asserted |
| --- | --- | --- |
| `library` | `Release` | a `.nupkg` in `release/` |
| `library-root` | `Release` | a `.nupkg` in `release/` when the project shares the root with a solution |
| `executable` | `Release` | `app/test.executable.dll` |
| `console` | `Release` | Linux x64 and macOS x64/arm64 archives, each holding the single-file executable |
| `console` | `Build` | every project of the solution built under its runtime, nothing in the runtime-agnostic output |
| `console`, solution deleted | `Build` | the same, with `Build` fanning out per project directory |
| `console` | `Bootstrap` | `Directory.Build.props` deployed |
| `library` | `Bootstrap` | no `Directory.Build.props` deployed |
| `library` | `Solution` | generated `.slnx` lists `src/` and `tests/`, not `build/` |
| `library`, stale `.slnx` | `Solution` | stale `<Project.Name>.slnx` overwritten with fresh content |
| `library-root` | `Solution` | differently named checked-in solution left alone; own `.slnx` generated alongside it |
| `library` | `Solution` run twice | second run regenerates identical content, no second file |
| `safe` | `Bundle` | a server `.dll` in `deploy/`, an `.html` in `deploy/public/` |

Landing on the terminal target pulls the whole chain (`AssemblyInfo`, `Build`,
`Lint`, `Tests`) with it. Fixtures reference the engine **from source**, so the
integration tests exercise whatever is checked out with no repack step in between. Set
`FBUILD_KEEP_TEMP` to keep a scenario's directory for inspection.

**The packaging gate:** the `should release a library through the packaged engine` case. It packs the
engine under a version nuget.org does not carry, installs it into a throwaway
consumer through Paket, deploys the support files with the packaged engine's
`Bootstrap` target, and drives `Release` through the `build.sh` it deployed —
invoked directly, so the executable bit `Bootstrap` sets is asserted too.
Every other scenario builds the engine from source, so the nuspec dependency set
is covered only here. Its seed `.config/dotnet-tools.json` holds Paket alone,
following the adoption steps: Paket has to be restorable before the package it
resolves can render the real manifest. Its restores use a NuGet cache
inside the throwaway directory: the engine version does not move between packs,
and a shared cache would hand back an earlier build of it.

**CI** (`.github/workflows/`):

- `tests.yaml` — on a pull request, `./build.sh -t Lint` plus the unit suite.
  Integration tests take minutes and need npm and the NuGet feeds, so they run on
  a nightly cron instead, as `./build.sh -t Tests`.
- `pr-check.yaml` — blocks fixup commits, runs ShellCheck.
- `publish.yaml` — on a `X.Y.Z` tag, `./build.sh -t publish` with
  `NUGET_API_KEY` in the environment.

**Coverage gap:** CI runs integration tests on `ubuntu-latest` only. Console tests
cross-publish macOS artifacts, but hosted macOS execution depends on local runs.

## 9. Boundaries

**Always:**

- Keep `Alma.Build.fsproj` `Version`, `CHANGELOG.md`, and
  `paket.dependencies` consistent in the same change.
- Run the integration tests after changing `Targets.fs` or `Bootstrap.fs` — the
  unit suite does not execute a single target.
- Run the `packaged engine` filtered case after changing packaging, the
  package `Version`, or the bootstrap assets.
- Edit the bootstrap assets under `bootstrap/`; they are the single source
  bundled into the engine assembly, and the repo's copies are symlinks to them.
- Keep user-visible behavior changes in sync with `README.md`, `CONTRIBUTING.md`,
  this document, and `build/README.md`.

**Ask first:**

- Breaking the `Spec` / `Targets` public surface — consumers hand-edit
  `Build.fs`, so it needs a major bump and a migration note.
- Adding a dependency to the engine — it lands in every consuming repo.
- Changing which files `Bootstrap` deploys — a removed file lingers in every
  consuming repo until deleted by hand.
- Changing `Solution`'s folder-grouping rule or generated filename — every
  consumer that has not yet run it gets whatever the rule says at the time.

**Never:**

- Edit bootstrap contents under `packages/`, `paket-files/`, or `.paket/`.
- Commit `bin/`, `obj/`, `release/`, or generated `AssemblyInfo.fs`.
- Commit real feed credentials or API keys — pushes read
  `PRIVATE_FEED_PASS` / a named env var at build time.

## 10. Success criteria

- [x] `./build.sh` builds this repo from a fresh clone.
- [x] The packaged engine is installed and driven end to end by an automated
      scenario.
- [x] A repo following the adoption steps in `README.md` builds with `./build.sh`.
- [x] Every spec case is covered by an automated end-to-end check — the nightly
      integration tests.
- [x] Consuming repos can resolve `Alma.Build` — it is published to nuget.org,
      reachable from CI and dev machines alike.
- [x] `Publish` has been exercised against that feed.

## 11. Roadmap

1. **Phase 1 (~10 repos)** — onboard a few representative repos per project
   type, fix friction found on real code.
2. **Phase 2 (50–100 repos)** — Renovate for pull-based bumps, a migration
   catalog, monitoring.

## 12. Open questions and risks

- **Vendoring has no drift detection.** Nothing records which version a repo's
  `build.sh` or `fsharplint.json` came from, and `Bootstrap` overwrites
  unconditionally, so a locally edited asset is silently clobbered on the next
  run — visible only in the repo's own diff.
- **Integration tests are nightly, not per-PR.** A target-graph regression lands green
  and is caught up to a day later, on a build nobody is watching.
- **Filters are output-shape-coupled.** `RtkFilter` parses MSBuild, fsharplint
  and Fable output by regex, so a tool changing its formatting degrades the
  filter silently. The fallback (a failing run with an unmatched error prints
  raw) bounds the damage to noise, not to a hidden failure.
- **Engine API stability** — `Build.fs` is author-owned and hand-edited on
  breaking changes, so the `Targets`/`Spec` surface must stay small and stable,
  backed by clear release notes.
- **`Microsoft.Build.*` capped below 18.10.** `Fake.DotNet.MSBuild` asks for
  `Microsoft.Build.Framework`, `Microsoft.Build.Utilities.Core`, and
  `Microsoft.NET.StringTools` at `>= 17.5`; 18.10 moved them to `net11.0`, and a
  `net10.0` project resolving them warns on every build. The engine caps all
  three at `>= 18.0 < 18.10` in its `paket.references`, so the cap travels
  through the nuspec to every consumer's build project. Lift it when the engine
  targets `net11.0`.