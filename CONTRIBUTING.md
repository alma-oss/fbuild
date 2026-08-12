# Contributing to fbuild

Working on the `Alma.Build` engine itself. For consuming the engine — targets, adoption,
updating — see [`README.md`](README.md).

## Repository layout

- `src/Alma.Build/`: core build engine (`Targets`, `Spec`, helpers) and package targets.
- `vendored/`: support files bundled into the engine assembly as embedded resources and deployed into consuming repositories by the `Bootstrap` target (`build.sh`, `fsharplint.json`, `.editorconfig`, `build/README.md`). The repo carries symlinks to them, because this repo is its own first consumer.
- `build/`: self-host build entrypoint used to build this repo.
- `docs/specs/fbuild/spec.md`: architecture and distribution design reference.

## Prerequisites

- .NET SDK with `net10.0` support.
- `bash`.
- `git` (required by build metadata initialization).

## Build this repository

`build/build.fsproj` references `src/Alma.Build/Alma.Build.fsproj` as a project, so the
self-host build always runs whatever engine source is checked out. A fresh clone needs no
preparation step:

```bash
./build.sh
```

Engine edits take effect on the next run — there is nothing to repack.

### FSharp.Core

`build/`, `tests/unit/`, and `tests/integration/` reference the engine as a project, so they must
resolve the same FSharp.Core the engine compiled against — an older one shadows it and the engine
assembly fails to load at runtime. Each takes it through its own `paket.references`, which leaves
`paket.lock` as the only place the version appears. Do not reintroduce a literal version pin.

The consumer project the integration harness writes into a temp directory is the exception: it
sits outside the repository and cannot resolve through Paket, so `Helpers.fs` reads the version
out of `paket.lock` when generating it.

## Targets

Run targets through the entry point:

```bash
./build.sh <Target>
```

This repository uses the `Library` spec; its targets and arguments are listed in
[`README.md`](README.md).

## Tests

The `Tests` target runs both suites. To run one directly:

```bash
dotnet run --project tests/unit/unit.fsproj --
dotnet run --project tests/integration/integration.fsproj --
```

The integration matrix is slow: each scenario copies a fixture from `tests/integration/fixtures/`
to a temp directory and drives a full target graph through it, so it needs `npm` and the NuGet
feeds. The copy is deleted when the scenario finishes; set `FBUILD_KEEP_TEMP` to keep it for
inspection, at the cost of a full build tree per scenario left in `$TMPDIR`.

```bash
FBUILD_KEEP_TEMP=1 dotnet run --project tests/integration/integration.fsproj --
```

The matrix runs each scenario against the engine built from source. One scenario instead packs
the engine, installs it into a throwaway consumer through Paket, deploys the support files with
its `Bootstrap` target, and drives the build with the `build.sh` it deployed — that is what
covers the nuspec dependencies and the bundled support files before a release goes out.

## When the engine will not build

The self-host build runs on the engine it is compiling, so engine source that does not compile
takes `./build.sh` down with it. Nothing needed to recover goes through the engine:

```bash
# only needed when .paket/Paket.Restore.targets is not yet generated (fresh clone)
dotnet tool restore && dotnet tool run paket restore

dotnet build src/Alma.Build/Alma.Build.fsproj              # compile-fix loop
dotnet run --project tests/unit/unit.fsproj --             # unit suite
dotnet fsharplint lint src/Alma.Build/Alma.Build.fsproj    # lint
dotnet pack src/Alma.Build/Alma.Build.fsproj -c Release -o release   # release artifact
```

`git stash` or `git revert` on the engine source restores a working `./build.sh` outright.

## Versioning and releases

- `src/Alma.Build/Alma.Build.fsproj` carries the package `Version` and metadata.
- `CHANGELOG.md` drives release metadata used by assembly info generation.
- `Release` packs `src/Alma.Build` to `release/`.

## Documentation

When behavior changes, keep docs in sync in the same change:

- consumer overview, targets, and adoption steps: `README.md`
- spec, architecture, and distribution details: `docs/specs/fbuild/spec.md`
- consumer quick reference: `build/README.md`
