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
- **bash** and **git** — required at build time (`build.sh`, and `Git.init`
  shells out to `git rev-parse`).
- **Node/npm** — required only for the SAFE-stack project type.

## 3. Commands

Repository-root commands:

```bash
# Default build target (self-host)
dotnet run --project ./build/build.fsproj -- Build

# Any target
dotnet run --project ./build/build.fsproj -- <Target>

# Skip clean / don't fail on lint errors
dotnet run --project ./build/build.fsproj -- Build no-clean no-lint

# Repack the engine into local-feed/ and validate the packaged path.
# Run this after editing src/Alma.Build/.
./bootstrap.sh Build

# Standard wrapper: restores tools + paket, then runs the build
./build.sh <Target>
```

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

Build arguments: `no-clean` skips `Clean`; `no-lint` skips failing on lint
errors. Default target when none is given is `Build` (SAFE's own
`runOrDefault` helper defaults to `Run`).

## 4. Project structure

### This repository

```
CHANGELOG.md            # release notes; parsed by the AssemblyInfo target
fbuild.slnx             # solution: src/Alma.Build, build
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
  Utils.fs              #   ProjectDefinition / Spec / Git / RuntimeId model,
                        #   Args, Dotnet, Nuget, Http, Github helpers
  SafeBuildHelpers.fs   #   internal — SAFE process helpers, parallel proc runner
  Targets.fs            #   the FAKE target graph for every project type
  Alma.Build.fsproj     #   Version + metadata; packs the vendored files as content/
  paket.references      #   FAKE dependencies (group Build)

build/                  # SELF-HOST runner; mirrors a consumer's build/ directory
  Build.fs              #   Library spec over src/Alma.Build
  build.fsproj          #   imports ..\.paket\Paket.Restore.targets
  paket.references      #   Alma.Build

local-feed/             # local NuGet feed for dev/test
release/                # packed artifacts (git-ignored)
docs/                   # vendoring guide and this document
```

Git-ignored: `bin/`, `obj/`, `release/`, `local-feed/`, `.paket/`, `packages/`, `paket-files/`,
`.fake/`, `.ionide/`, and generated `AssemblyInfo.fs`.

### A consuming repository

```
.config/dotnet-tools.json   # paket + dotnet-fsharplint
NuGet.config           # feed
paket.dependencies     # group Build → Alma.Build <version>
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

Adoption steps are in `docs/vendoring.md`.

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

- Compiled engine (`Utils.fs`, `SafeBuildHelpers.fs`, `Targets.fs`).
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

## 6. Workflows

**New repo:** follow `docs/vendoring.md` — pin the package, add `build/`, copy
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

- `[<RequireQualifiedAccess>]` on helper modules (`Args`, `Dotnet`, `Nuget`,
  `Spec`, `Git`, `RuntimeId`, `NugetApi`, `Http`, `Github`).
- Companion `default*` / `map*` pairs per spec case; adding a case means adding
  both.
- `///` doc comments only where the signature can't carry the fact (units,
  preconditions, side effects). Section dividers use the long `// ---- …` rule.
- `orFail`/`failwith` are used deliberately in the build engine, where a failure
  must abort the build and no caller can handle an error.
- Compile order is explicit in the `.fsproj` and is part of the design:
  `Utils → SafeBuildHelpers → Targets`.

## 8. Testing strategy

There is no automated test suite. The engine is almost entirely process
orchestration, so the meaningful assertion is "a repo of type X actually
builds", and the only repo available to assert that on is this one.

**The gate:** `./bootstrap.sh Build` — repacks the engine into `local-feed/`,
restores it, and runs the self-host build against the *packaged* engine.
Run it on any change to `src/Alma.Build/` or its assets.

**Coverage gap:** only the `Library` path is exercised this way. The other three
spec cases are validated by hand in a consuming repository.

## 9. Boundaries

**Always:**

- Keep `Alma.Build.fsproj` `Version`, `CHANGELOG.md`, and
  `paket.dependencies` consistent in the same change.
- Run `./bootstrap.sh Build` after changing engine source.
- Edit the vendored assets at the repo root; they are the single source packed
  into the package.
- Keep user-visible behavior changes in sync with `README.md`,
  `docs/vendoring.md`, this document, and `README.fbuild.md`.

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
- [x] A repo following `docs/vendoring.md` builds with `./build.sh`.
- [ ] A production internal feed is wired up and reachable from CI and dev
      machines.
- [ ] `Publish` has been exercised against a real feed (this repo currently
      builds with `NugetApi.NotUsed`).
- [ ] Every spec case is covered by an automated end-to-end check.

## 11. Roadmap

1. **Phase 0 (done)** — package the engine, self-host it, document vendoring.
2. **Phase 1 (~10 repos)** — publish to the internal feed, onboard a few
   representative repos per project type, fix friction found on real code.
3. **Phase 2** — automate adoption and updates so the vendored files are not
   copied by hand.
4. **Phase 3 (50–100 repos)** — Renovate for pull-based bumps, a migration
   catalog, monitoring.

## 12. Open questions and risks

- **No production feed.** `paket.dependencies` resolves `Alma.Build` from
  `nuget.org` and `local-feed/` only; the production feed and its auth model
  (token/env) are unresolved. `local-feed/` covers dev/test only.
- **Manual vendoring has no drift detection.** Nothing records which version a
  repo's `build.sh` or `fsharplint.json` came from, so a locally edited asset is
  silently overwritten — or silently kept stale — on the next bump.
- **No automated matrix.** Only `Library` is exercised, by this repo's own build;
  the other three spec cases regress silently.
- **Engine API stability** — `Build.fs` is author-owned and hand-edited on
  breaking changes, so the `Targets`/`Spec` surface must stay small and stable,
  backed by clear release notes.
