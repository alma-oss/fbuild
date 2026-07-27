# fbuild

`fbuild` is a versioned, distributable FAKE + Paket build infrastructure for F# projects.

This repository ships one deliverable: **`Alma.Build`** (`src/Alma.Build/`) — the build
engine library plus the support files that consuming repositories vendor alongside it.

Current engine version: `2.0.0` (`Version` in `src/Alma.Build/Alma.Build.fsproj`).

## What you get

A consuming repository owns a single build file, `build/Build.fs`, which declares a
`ProjectDefinition` — project metadata plus one `Spec` case — and passes it to
`Targets.init`. The engine derives the target graph from the spec:

- `Library`
- `Executable`
- `ConsoleApplication`
- `SAFEStackApplication`

Targets are then run through the vendored entry point:

```bash
./build.sh <Target>
```

Common targets (`Library` spec):

- `Build` (default when no target is provided)
- `Lint`
- `Tests`
- `Release`
- `Publish`
- `Info`

Arguments:

- `no-clean`: skips the `Clean` step.
- `no-lint`: skips the `Lint` step.

```bash
./build.sh Build no-lint
```

## Prerequisites

- .NET SDK with `net10.0` support.
- `bash`.
- `git` (required by build metadata initialization).

## Adoption

Everything here is a manual copy — the engine is a normal NuGet package plus a handful of
support files that live in the consuming repo.

### 1. Pin the engine

`paket.dependencies` at the repo root:

```paket
group Build
    source https://api.nuget.org/v3/index.json

    nuget Alma.Build 2.0.0
```

`build/paket.references`:

```paket
group Build
    Alma.Build
```

`.config/dotnet-tools.json` — Paket is mandatory; `dotnet-fsharplint` backs the `Lint`
target:

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "paket": { "version": "10.3.1", "commands": [ "paket" ] },
    "dotnet-fsharplint": { "version": "0.26.10", "commands": [ "dotnet-fsharplint" ] }
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

The `Paket.Restore.targets` import is what makes a plain `dotnet build`/`dotnet run`
trigger Paket's restore, so no separate bootstrap step is needed once the files are in
place.

`build/Build.fs` is the only build file the repository owns and edits.

### 3. Copy the support files

They ship inside the package under `content/`, at
`~/.nuget/packages/alma.build/<version>/content/` (or `$NUGET_PACKAGES`). Copy each to the
repo root:

| File               | Purpose                                                |
| ------------------ | ------------------------------------------------------ |
| `build.sh`         | entry point: restores tools + packages, runs the build |
| `.editorconfig`    | formatting rules the engine's `Lint` target assumes    |
| `fsharplint.json`  | lint configuration                                     |
| `README.fbuild.md` | consumer-facing quick reference                        |

`build.sh` needs to be executable:

```bash
chmod +x build.sh
```

These files are version-locked to the engine. Re-copy them whenever the pinned
`Alma.Build` version changes — that is how engine-side changes to lint rules, formatting,
or the entry point reach the repo.

### 4. Restore and build

`build.sh` restores tools and packages on every run, but its `paket restore` needs a
`paket.lock` that does not exist yet. Generate it once:

```bash
dotnet tool restore
dotnet paket install   # writes paket.lock — commit it
```

From then on the entry point is enough:

```bash
./build.sh Build
```

## Updating

1. Bump the version in `paket.dependencies`.
2. `dotnet paket install`.
3. Re-copy the support files from the new package's `content/` directory (step 3).
4. If the engine's `Spec`/`Targets` surface changed, adjust `build/Build.fs` per the
   release notes in `CHANGELOG.md`.
5. Run `./build.sh` and commit `paket.lock` with the rest.

## Further reading

- [`docs/specs/fbuild/spec.md`](docs/specs/fbuild/spec.md) — architecture and design reference.
- `README.fbuild.md` — consumer-facing quick reference shipped with the engine.
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — working on the engine itself.
