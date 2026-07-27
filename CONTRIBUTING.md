# Contributing to fbuild

Working on the `Alma.Build` engine itself. For consuming the engine — targets, adoption,
updating — see [`README.md`](README.md).

## Repository layout

- `src/Alma.Build/`: core build engine (`Targets`, `Spec`, helpers) and package targets.
- `vendored/`: support files vendored into consuming repositories and packed into the engine package (`build.sh`, `README.fbuild.md`, `fsharplint.json`, `.editorconfig`). The repo root carries symlinks to them, because this repo is its own first consumer.
- `build/`: self-host build entrypoint used to build this repo.
- `docs/specs/fbuild/spec.md`: architecture and distribution design reference.

## Prerequisites

- .NET SDK with `net10.0` support.
- `bash`.
- `git` (required by build metadata initialization).

## Build this repository

### Fresh clone

The self-host build resolves `Alma.Build` from `local-feed/`, which is generated, not
committed. Run bootstrap once so the feed exists:

```bash
./bootstrap.sh Build
```

### Fast path

Afterwards, run the self-host build directly:

```bash
dotnet run --project ./build/build.fsproj -- Build
```

### After engine changes

Engine edits are invisible to the build until the package is repacked. Run bootstrap to
repack and validate:

```bash
./bootstrap.sh Build
```

## Targets

Run targets through the self-host entrypoint:

```bash
dotnet run --project ./build/build.fsproj -- <Target>
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

## Versioning and releases

- `src/Alma.Build/Alma.Build.fsproj` carries the package `Version` and metadata.
- `CHANGELOG.md` drives release metadata used by assembly info generation.
- `Release` packs `src/Alma.Build` to `release/`.

## Documentation

When behavior changes, keep docs in sync in the same change:

- consumer overview, targets, and adoption steps: `README.md`
- spec, architecture, and distribution details: `docs/specs/fbuild/spec.md`
- consumer quick reference: `README.fbuild.md`
