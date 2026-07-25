# fbuild

`fbuild` is a versioned, distributable FAKE + Paket build infrastructure for F# projects.

This repository ships one deliverable: **`Alma.Build`** (`src/Alma.Build/`) — the build
engine library plus the support files that consuming repositories vendor alongside it.

Consuming repositories adopt the engine by hand: pin the `Alma.Build` package and copy the
vendored support files into the repo. See [`docs/vendoring.md`](docs/vendoring.md).

Current engine version: `2.0.0` (`Version` in `src/Alma.Build/Alma.Build.fsproj`).

## Repository layout

- `src/Alma.Build/`: core build engine (`Targets`, `Spec`, helpers) and package targets.
- Repo root: support files vendored into consuming repositories and packed into the engine package (`build.sh`, `README.fbuild.md`, `fsharplint.json`, `.editorconfig`).
- `build/`: self-host build entrypoint used to build this repo.
- `docs/vendoring.md`: how to adopt the engine in a consuming repository.
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

## Common targets

Run targets through the self-host entrypoint:

```bash
dotnet run --project ./build/build.fsproj -- <Target>
```

Common targets in this repository (`Library` spec):

- `Build` (default when no target is provided)
- `Lint`
- `Tests`
- `Release`
- `Publish`
- `Info`

Useful arguments:

- `no-clean`: skips the `Clean` step.
- `no-lint`: skips failing on lint errors.

Example:

```bash
dotnet run --project ./build/build.fsproj -- Build no-lint
```

## Versioning and releases

- `src/Alma.Build/Alma.Build.fsproj` carries the package `Version` and metadata.
- `CHANGELOG.md` drives release metadata used by assembly info generation.
- `Release` packs `src/Alma.Build` to `release/`.

## Further reading

- [`docs/vendoring.md`](docs/vendoring.md) — adopting the engine in a repository.
- [`docs/specs/fbuild/spec.md`](docs/specs/fbuild/spec.md) — architecture and design reference.
- `README.fbuild.md` — consumer-facing quick reference shipped with the engine.
