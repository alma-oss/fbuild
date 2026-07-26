# AGENTS

Guidance for coding agents working in this repository.

## Mission

Make minimal, safe changes to the `Alma.Build` engine package while preserving behavior for
consuming repositories, which vendor the engine and its support files by hand.

Prefer small diffs, clear validation, and no unrelated refactors.

## Where to work

- Engine: `src/Alma.Build/`
- Self-host entrypoint: `build/Build.fs`
- Vendored consumer assets: `vendored/` (`build.sh`, `README.fbuild.md`, `fsharplint.json`, `.editorconfig`), symlinked into the repo root

## Ground rules

- Do not edit vendored package contents in `packages/`.
- Do not commit generated build outputs from `bin/`, `obj/`, or `release/` unless explicitly asked.
- Keep public API changes intentional; if changing `Spec` or target behavior, align docs in the same change — consumers hand-edit their own `Build.fs`.
- `vendored/.editorconfig`, `vendored/fsharplint.json`, `vendored/build.sh`, and `vendored/README.fbuild.md` are the support files and the single source packed into the engine package. Edit them there; the root entries are symlinks, not copies.
- Preserve existing style in F# files.

## Build and test commands

Use repository-root commands:

```bash
# Default build target
dotnet run --project ./build/build.fsproj -- Build

# Repack engine and validate package-based path
./bootstrap.sh Build

# Unit tests only; the `Tests` target runs the integration matrix too
dotnet run --project tests/unit/unit.fsproj --
```

Target notes:

- Default target is `Build`.
- Main `Library` flow is: `Clean -> AssemblyInfo -> Build -> Lint -> Tests -> Release -> Publish`.
- The integration matrix drives a full target graph per fixture, so it takes minutes and needs `npm` and the NuGet feeds. It deletes its temp fixture copy per scenario; run with `FBUILD_KEEP_TEMP=1` to keep the copies when debugging a failure.

## Validation checklist

After changes, run what is relevant:

1. Engine or target logic changed:
   - `./bootstrap.sh Build`
2. Packaging/versioning changed:
   - verify `src/Alma.Build/Alma.Build.fsproj` `Version` and `CHANGELOG.md` consistency.

If a project/package was renamed and pack/restore behaves oddly, clear stale artifacts in the renamed project `obj/` and `bin/` before re-running validation.

## Common change patterns

- Add a target behavior:
  - update `src/Alma.Build/Targets.fs`
  - update docs (`README.md`, `docs/vendoring.md`, `docs/specs/fbuild/spec.md`) if behavior is user-visible
- Change a vendored asset:
  - update the file in `vendored/` (`build.sh`, `README.fbuild.md`, `fsharplint.json`, `.editorconfig`) and note it in `CHANGELOG.md` — consumers re-copy these on a version bump

## Documentation expectations

When behavior changes, keep docs in sync in the same PR:

- repository overview and commands: `README.md`
- adoption steps for consuming repositories: `docs/vendoring.md`
- spec, architecture, and distribution details: `docs/specs/fbuild/spec.md`
- consumer quick reference: `README.fbuild.md`

## SDD artifacts

`docs/specs/fbuild/spec.md` is this repo's durable spec. Transient plans/checklists go
under `docs/tasks/<work-slug>/` and are deleted at reconciliation.
