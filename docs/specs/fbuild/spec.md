# Spec: fbuild — Versioned, Distributable F# Build Infrastructure

> Status: **Implemented** at engine version `2.0.0`.
> This document is the spec and the architecture reference for the repository.
> It describes the current state — not history. Section 12 tracks what is
> unresolved.

## 1. Objective

Ship a shared **FAKE + Paket build engine** for the company's F# repositories
that is versioned and distributable — so that build infrastructure improvements
propagate through a package version bump instead of copy-pasting build logic
between repos.

**Users:** F# repository authors inside the organization (target scale ~50–100
repos; onboarding starts around 10).

**Success looks like:**

- A repo gets a working build by pinning one package and vendoring a small,
  fixed set of support files.
- An existing repo pulls an engine improvement by bumping one version and
  re-copying the vendored support files.
- `Build.fs` is the only per-project build file an author owns and edits.
- Breaking engine changes are visible and migratable, not silent.

### Deliverable

| Artifact | Source | Packaged as |
| --- | --- | --- |
| **`Alma.Build`** | `src/Alma.Build/` | NuGet library + packaged content assets |

Versioned off a single source of truth: `Version` in
`src/Alma.Build/Alma.Build.fsproj`.

## 2. Tech stack

- **.NET SDK `net10.0`** — all projects target it.
- **F#** — engine and self-host build.
- **FAKE** (`Fake.Core.Target`, `Fake.DotNet.Cli`, `Fake.IO.FileSystem`,
  `Fake.IO.Zip`, `Fake.Core.UserInput`, `Fake.DotNet.AssemblyInfoFile`,
  `Fake.Core.ReleaseNotes`, `Fake.Tools.Git`) — the target graph. Consumed via
  Paket (`src/Alma.Build/paket.references`, group `Build`).
- **Paket `10.3.1`** — mandatory dependency manager for every build project;
  restored from `.config/dotnet-tools.json`.
- **`dotnet-fsharplint 0.26.10`** — the `Lint` target.
- **Expecto `10.2.1`** — both test suites (`tests/unit`, `tests/integration`).
  The test projects use `PackageReference`, not Paket, and pin
  `FSharp.Core 10.1.300` with `DisableImplicitFSharpCoreReference` so the SDK's
  implicit lower version does not shadow the one the engine assembly was
  compiled against.
- **bash** and **git** — required at build time (`build.sh`, and `Git.init`
  shells out to `git rev-parse`).
- **Node/npm** — required by the SAFE-stack project type, and therefore by the
  integration matrix's `safe` fixture.
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

# Repack the engine into local-feed/ and validate the packaged path.
# Run this after editing src/Alma.Build/.
./bootstrap.sh Build

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
`Clean`, `AssemblyInfo`, `Build`, `Lint`, `Tests`, `Release`, `Publish`,
`ZipRelease`, `Run`, `Watch`, `RunMirrord`, `WatchMirrord`.

| Spec | Chain |
| --- | --- |
| `Library` | `Clean → AssemblyInfo → Build → Lint → Tests → Release → Publish` |
| `Executable` | `Clean → AssemblyInfo → Build → Lint → Tests → Release ⟺ Watch ⟺ Run …` |
| `ConsoleApplication` | `Clean → AssemblyInfo → Build → Lint → Tests → Release → ZipRelease` |
| `SAFEStackApplication` | `SafeClean → AssemblyInfo → InstallClient → Build → Lint → Tests → Bundle`; plus `Run`/`RunMirrord`/`WatchTests` |

Build arguments: `no-clean` skips `Clean`; `no-lint` skips `Lint`. Default
target when none is given is `Build`, for every spec case.

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
paket.dependencies      # group Build → nuget Alma.Build <version>
paket.lock              # authoritative version pin
bootstrap.sh            # repack engine → local-feed, paket restore, run build
build.sh                # symlink to vendored/build.sh — also this repo's entry point
fsharplint.json         # symlink to vendored/fsharplint.json
.editorconfig           # symlink to vendored/.editorconfig
README.fbuild.md        # symlink to vendored/README.fbuild.md

vendored/               # VENDORED ASSETS — packed as content/, symlinked to the
                        # root because this repo is its own first consumer
  build.sh              #   entry point: restores tools + packages, runs the build
  fsharplint.json       #   lint configuration
  .editorconfig         #   formatting rules the Lint target assumes
  README.fbuild.md      #   consumer-facing quick reference

src/Alma.Build/         # ENGINE — packed as the Alma.Build NuGet
  RtkFilter.fs          #   internal — per-command output filters (§5.5)
  Commands.fs           #   internal — command vocabulary, RTK transport,
                        #   serial + parallel runners, failure-log tee
  Utils.fs              #   ProjectDefinition / Spec / Git / RuntimeId model,
                        #   Args, Solution, Nuget, Http, Github helpers
  Targets.fs            #   the FAKE target graph for every project type
  Alma.Build.fsproj     #   Version + metadata; packs the vendored files as content/
  paket.references      #   FAKE dependencies (group Build)

build/                  # SELF-HOST runner; mirrors a consumer's build/ directory
  Build.fs              #   Library spec over src/Alma.Build
  build.fsproj          #   imports ..\.paket\Paket.Restore.targets
  paket.references      #   Alma.Build

tests/
  unit/                 # Expecto; reaches the internal modules via InternalsVisibleTo
  integration/          # Expecto; one end-to-end scenario per spec case
    fixtures/           #   a complete consumer repo per case, Build.fs included

.github/workflows/      # pr-check, tests, publish
local-feed/             # local NuGet feed for dev/test
release/                # packed artifacts (git-ignored)
docs/                   # this document and design notes
```

Git-ignored: `bin/`, `obj/`, `release/`, `local-feed/`, `.paket/`, `packages/`, `paket-files/`,
`.fake/`, `.ionide/`, and generated `AssemblyInfo.fs`.

### A consuming repository

```
.config/dotnet-tools.json   # paket + dotnet-fsharplint
paket.dependencies     # feed sources + group Build → Alma.Build <version>
paket.lock             # pinned versions
build.sh               # entry point (vendored)
fsharplint.json        # vendored
.editorconfig          # vendored
README.fbuild.md       # vendored
build/
  build.fsproj         # imports Paket.Restore.targets
  paket.references     # Alma.Build
  Build.fs             # author-owned
src/ …                 # application code
```

Adoption steps are in `README.md`.

## 5. Architecture

### 5.1 Distribution model

The engine is a plain NuGet package; everything a repo needs beyond it is a
small set of files committed to that repo. There is no bootstrapper: the
repository author writes `paket.dependencies`, `build/paket.references`,
`.config/dotnet-tools.json`, and a `build.fsproj` that imports
`Paket.Restore.targets`, then copies the packaged assets to the repo root. From
then on `build.sh` does the rest on every run:

```bash
dotnet tool restore
dotnet tool run paket restore
FAKE_DETAILED_ERRORS=true dotnet run --project ./build/build.fsproj -- "$@"
```

Because `build.fsproj` imports `Paket.Restore.targets`, even a bare
`dotnet build`/`dotnet run` triggers Paket's MSBuild-integrated restore. Paket
is mandatory across all build projects — there is no Paket-free variant.

### 5.2 Engine package — `Alma.Build`

- Compiled engine (`RtkFilter.fs`, `Commands.fs`, `Utils.fs`, `Targets.fs`).
- **Also ships the non-compiled assets** under `content/` (`build.sh`,
  `README.fbuild.md`, `fsharplint.json`, `.editorconfig`). `IncludeContentInPack`
  plus `NoDefaultExcludes` (so dotfiles pack). Consumers copy them out of the
  restored package directory, which makes them **version-locked to the engine**:
  a version bump is what delivers a new lint config or entry point.
- Semver, single source: `Version` in `src/Alma.Build/Alma.Build.fsproj`.
- Escape hatches: override targets from `Build.fs` via `Spec.map*`, or vendor
  the engine source outright.

### 5.3 The project model

`ProjectDefinition = { Project: ProjectMeta; Specs: ProjectSpec }`.

`ProjectSpec` cases and their constructors:

| Case | Default constructor | Key fields |
| --- | --- | --- |
| `Library` | `Spec.defaultLibrary` | `ReleaseDir`, `NugetApi`, `Organization`, `NugetCustomServerRepository` |
| `Executable` | `Spec.defaultExecutable` | `ReleaseDir = ./app` |
| `ConsoleApplication` | `Spec.defaultConsoleApplication runtimeIds` | `RuntimeIds`, `ReleaseSource`, `ReleaseDir = ./dist` |
| `SAFEStackApplication` | `Spec.defaultSAFEStackApplication templateVersion` | Shared/Server/Client + test paths, `DeployPath` |

Every case implements `IProjectSources` (`Sources` / `Tests` / `All` globs) and
each has a `Spec.map*` function for overriding fields from `Build.fs`.

`NugetApi = NotUsed | AskForKey | Organization of name | KeyInEnvironment of
envVarName` drives the `Publish` target. `RuntimeId = OSX | Windows | Linux |
ArmLinux | AlpineLinux | RaspberryPiHassioAddon | Other of string`.

### 5.4 Vendored support files

`build.sh`, `README.fbuild.md`, `fsharplint.json`, and `.editorconfig` live in
`vendored/`, ship in the package's `content/` directory, and are copied into the
consuming repo root by hand. They are committed there, so a repo can diverge
locally; the cost of diverging is that the next version bump has to be
reconciled manually.

This repository consumes them the same way, but through root symlinks instead of
copies — the repo is its own first consumer, so the root and the packed payload
cannot drift.

### 5.5 Command execution and output filtering

No target shells out directly. Every external process goes through the
`Commands` module as a typed `Command`:

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

## 6. Workflows

**New repo:** follow the adoption section in `README.md` — pin the package, add `build/`, copy
the assets, `./build.sh`.

**Update (pull model — each repo on its own schedule):** bump the pin in
`paket.dependencies` → `paket install` → re-copy the vendored assets from the
new package → if the engine API changed, the author edits `Build.fs` per the
release notes → CI runs the build → merge when green.

**Override / fork:** override a target from `Build.fs` via `Spec.map*`, or
vendor the engine source.

**Engine development in this repo:** the self-host build consumes `Alma.Build`
from `local-feed/`, so after editing engine source run `./bootstrap.sh Build` to
repack and validate the *packaged* path, which is what consumers actually get.
Then run the suites that cover what changed (§8) — the integration matrix builds
the engine from source, so it and the bootstrap gate check different things.

**Release:** bump `Version` in `Alma.Build.fsproj` and `CHANGELOG.md`, merge,
then push a `X.Y.Z` tag — `publish.yaml` packs and pushes from there.

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
        AllSources =
            sources
            ++ "tests/*.fsproj"
            ++ "build/*.fsproj"
        Organization = None
        NugetApi = NugetApi.NotUsed
        NugetCustomServerRepository = None
    }
```

Conventions in force:

- `[<RequireQualifiedAccess>]` on helper modules (`Args`, `Option`, `Solution`,
  `Nuget`, `Spec`, `Git`, `RuntimeId`, `NugetApi`, `Http`, `Github`,
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
  `RtkFilter → Commands → Utils → Targets`. `RtkFilter` and `Commands` are
  `internal`; the unit test assembly reaches them through
  `InternalsVisibleTo("Alma.Build.Tests")` on the engine project.

## 8. Testing strategy

Two Expecto suites, both run by the `Tests` target (this repo's `TestsSources`
is `tests/*/*.fsproj` — one level only, so the matrix's fixture projects are
not mistaken for tests).

**Unit — `tests/unit/`.** Covers the pure parts the rest of the engine is built
on: the output filters (`RtkFilterTests`), the command vocabulary, transport
selection, tee decision and trace compaction (`CommandTests`), and the small
`Utils` helpers (`UtilsTests`). Fast, spawns no processes.

**Integration — `tests/integration/`.** One scenario per `ProjectSpec` case.
Each copies the checked-in fixture repo for that case out of
`tests/integration/fixtures/` into a throwaway directory, completes it into a
runnable consumer repo (vendored `fsharplint.json` + `.editorconfig` taken from
this repo's root, a generated `build.fsproj`, `git init`), drives its terminal
release target through the engine's own entry point, and asserts the artifacts:

| Fixture | Target | Asserted |
| --- | --- | --- |
| `library` | `Release` | a `.nupkg` in `release/` |
| `executable` | `Release` | `app/test.executable.dll` |
| `console` | `Release` | `dist/linux-x64.zip` |
| `safe` | `Bundle` | a server `.dll` in `deploy/`, an `.html` in `deploy/public/` |

Landing on the terminal target pulls the whole chain (`AssemblyInfo`, `Build`,
`Lint`, `Tests`) with it. Fixtures reference the engine **from source**, so the
matrix exercises whatever is checked out with no repack step in between. Set
`FBUILD_KEEP_TEMP` to keep a scenario's directory for inspection.

**The packaging gate:** `./bootstrap.sh Build` — repacks the engine into
`local-feed/`, restores it, and runs the self-host build against the *packaged*
engine. The matrix builds the engine from source, so packaging is covered only
here; run bootstrap on any change to `src/Alma.Build/` or its assets.

**CI** (`.github/workflows/`):

- `tests.yaml` — on a pull request, `./bootstrap.sh -t Lint` plus the unit
  suite. The matrix is minutes of work and needs npm and the NuGet feeds, so it
  runs on a nightly cron instead, as `./bootstrap.sh -t Tests`. `build.sh`
  cannot be used in either case: this repo builds itself, and `local-feed/` is
  git-ignored, so Paket cannot resolve `Alma.Build` until bootstrap has packed
  it from source.
- `pr-check.yaml` — blocks fixup commits, runs ShellCheck.
- `publish.yaml` — on a `X.Y.Z` tag, `./bootstrap.sh -t publish` with
  `NUGET_API_KEY` in the environment.

**Coverage gap:** the matrix runs on `ubuntu-latest` only, and asserts that
artifacts exist rather than what is in them.

## 9. Boundaries

**Always:**

- Keep `Alma.Build.fsproj` `Version`, `CHANGELOG.md`, and
  `paket.dependencies` consistent in the same change.
- Run `./bootstrap.sh Build` after changing engine source.
- Run the integration matrix after changing `Targets.fs` — the unit suite does
  not execute a single target.
- Edit the vendored assets at the repo root; they are the single source packed
  into the package.
- Keep user-visible behavior changes in sync with `README.md`, `CONTRIBUTING.md`,
  this document, and `README.fbuild.md`.

**Ask first:**

- Breaking the `Spec` / `Targets` public surface — consumers hand-edit
  `Build.fs`, so it needs a major bump and a migration note.
- Adding a dependency to the engine — it lands in every consuming repo.
- Changing which files ship as packaged content — every consuming repo has to
  re-copy or delete them by hand.

**Never:**

- Edit vendored contents under `packages/`, `paket-files/`, or `.paket/`.
- Commit `bin/`, `obj/`, `release/`, or generated `AssemblyInfo.fs`.
- Commit real feed credentials or API keys — pushes read
  `PRIVATE_FEED_PASS` / a named env var at build time.

## 10. Success criteria

- [x] `dotnet run --project ./build/build.fsproj -- Build` builds this repo.
- [x] `./bootstrap.sh Build` repacks the engine and the self-host build passes
      against the packaged engine.
- [x] A repo following the adoption steps in `README.md` builds with `./build.sh`.
- [x] Every spec case is covered by an automated end-to-end check — the nightly
      integration matrix.
- [ ] A feed consuming repos can resolve `Alma.Build` from is wired up and
      reachable from CI and dev machines.
- [ ] `Publish` has been exercised against that feed. The self-host build is
      configured for it (`NugetApi.KeyInEnvironment "NUGET_API_KEY"`, pushed by
      `publish.yaml` on a version tag), but no tag has been cut yet.

## 11. Roadmap

1. **Phase 0 (done)** — package the engine, self-host it, document vendoring,
   cover every spec case with an end-to-end matrix.
2. **Phase 1 (~10 repos)** — publish to a feed, onboard a few representative
   repos per project type, fix friction found on real code.
3. **Phase 2** — automate adoption and updates so the vendored files are not
   copied by hand.
4. **Phase 3 (50–100 repos)** — Renovate for pull-based bumps, a migration
   catalog, monitoring.

## 12. Open questions and risks

- **No feed carries the package yet.** `publish.yaml` is wired to push to
  nuget.org on a version tag, but no version has been published, so
  `paket.dependencies` still resolves `Alma.Build` from `local-feed/` — dev and
  test only. Whether the production channel stays public nuget.org or moves to
  an internal feed, and the auth model if it moves, is unresolved.
- **Manual vendoring has no drift detection.** Nothing records which version a
  repo's `build.sh` or `fsharplint.json` came from, so a locally edited asset is
  silently overwritten — or silently kept stale — on the next bump.
- **The matrix is nightly, not per-PR.** A target-graph regression lands green
  and is caught up to a day later, on a build nobody is watching.
- **Filters are output-shape-coupled.** `RtkFilter` parses MSBuild, fsharplint
  and Fable output by regex, so a tool changing its formatting degrades the
  filter silently. The fallback (a failing run with an unmatched error prints
  raw) bounds the damage to noise, not to a hidden failure.
- **Engine API stability** — `Build.fs` is author-owned and hand-edited on
  breaking changes, so the `Targets`/`Spec` surface must stay small and stable,
  backed by clear release notes.
