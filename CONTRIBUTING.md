# Contributing to fbuild

Working on the `Alma.Build` engine itself. For consuming the engine — targets, adoption,
updating — see [`README.md`](README.md).

## Repository layout

- `src/Alma.Build/`: core build engine (`Targets`, `Spec`, helpers) and package targets.
- `bootstrap/`: support files bundled into the engine assembly as embedded resources and deployed into consuming repositories by the `Bootstrap` target (`build.sh`, `fsharplint.json`, `.editorconfig`, `build/README.md`). The repo carries symlinks to them, because this repo is its own first consumer.
- `build/`: self-host build entrypoint used to build this repo.
- `docs/specs/fbuild/spec.md`: architecture and distribution design reference.

## Prerequisites

- .NET SDK with `net10.0` support.
- `bash`.
- `git` (required by build metadata initialization).

## Build this repository

Run the entry point from the repository root:

```bash
./build.sh
```

`build.sh` restores the local tools and Paket packages — generating
`.paket/Paket.Restore.targets`, which every project file imports — before building.

`build/build.fsproj` references `src/Alma.Build/Alma.Build.fsproj` as a project, so engine
edits take effect on the next run — there is nothing to repack.

### FSharp.Core

FSharp.Core sits in the main group of `paket.dependencies` as `~> 10.0` and in
`src/Alma.Build/paket.references`. The reference is what puts it in the packed nuspec, as
`>= <locked version> and < 11.0.0`, and the locked version is the one the engine assembly binds to.
Drop it and the nuspec carries only FAKE's own `FSharp.Core >= 8.0.400`: a consumer whose
`Build` group already holds an older FSharp.Core keeps it across `paket install`, and the engine
fails to load (`FileNotFoundException` on `FSharp.Core, Version=10.1.0.0`). The packaged
integration scenario seeds exactly such a lock and fails without the reference.

Consumer build projects resolve through Paket, which disables the SDK's implicit FSharp.Core
reference, so the version a consumer runs is whatever its `Build` group locks — never below the
nuspec floor.

## Targets

Run targets through the entry point:

```bash
./build.sh <Target>
```

This repository uses the `Library` spec; its targets and arguments are listed in
[`README.md`](README.md).

For `ConsoleApplication`, `RuntimeMode.AutoDetect` and `RuntimeMode.Specific` add the
selected runtime ID to local `Build`, `Tests`, `Run`, `Watch`, and Mirrord commands. The
`Specific` target must be included in `RuntimeTargets`; `AutoDetect` is independent of the
release matrix. Releases publish all configured targets, with `PublishSingleFile` controlling
whether each runtime's output is bundled.

## Tests

The `Tests` target runs both suites. To run one directly:

```bash
dotnet run --project tests/unit/unit.fsproj --
dotnet run --project tests/integration/integration.fsproj --
```

The integration tests are slow: each case copies a fixture from `tests/integration/fixtures/`
to a temp directory and drives a target graph through it, so it needs `npm` and the NuGet feeds.
Each copy gives the source-referenced engine isolated `bin`/`obj` paths, allowing cases based on
the same fixture to run concurrently. Each copy is deleted when its case finishes; set
`FBUILD_KEEP_TEMP` to keep it for inspection, at the cost of a full build tree per case left in `$TMPDIR`:

```bash
FBUILD_KEEP_TEMP=1 dotnet run --project tests/integration/integration.fsproj --
```

Scenarios build the engine from source, except one that packs it, installs it into a throwaway
consumer through Paket, deploys the support files with `Bootstrap`, and drives the build with
the deployed `build.sh`. Run that one before a release goes out — the only coverage of the
nuspec dependencies and the bundled support files:

```bash
dotnet run --project tests/integration/integration.fsproj -- --filter-test-case "packaged engine"
```

Unit tests cover runtime selection and command argument propagation. The console integration
scenario publishes Linux x64 and both macOS architectures and checks their unbundled archives.

## When the engine will not build

The self-host build runs on the engine it is compiling, so engine source that does not compile
takes `./build.sh` down with it. Work on the engine with plain `dotnet` commands until it
compiles again — none of them go through the engine:

```bash
# only needed when .paket/Paket.Restore.targets is not yet generated (fresh clone)
dotnet tool restore && dotnet tool run paket restore

dotnet build src/Alma.Build/Alma.Build.fsproj              # compile-fix loop
dotnet run --project tests/unit/unit.fsproj --             # unit suite
dotnet fsharplint lint src/Alma.Build/Alma.Build.fsproj    # lint
dotnet pack src/Alma.Build/Alma.Build.fsproj -c Release -o release   # release artifact
```

To get a working `./build.sh` back immediately, `git stash` or `git revert` the engine source.

## Versioning and releases

- Bump `Version` in `src/Alma.Build/Alma.Build.fsproj` and describe the change in
  `CHANGELOG.md` — the changelog drives release metadata used by assembly info generation.
- Run `./build.sh Release` to pack `src/Alma.Build` into `release/`.

## Documentation

When behavior changes, keep docs in sync in the same change:

- consumer overview, targets, and adoption steps: `README.md`
- spec, architecture, and distribution details: `docs/specs/fbuild/spec.md`
- consumer quick reference: `build/README.md`
