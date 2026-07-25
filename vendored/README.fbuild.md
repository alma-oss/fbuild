# fbuild

This repository uses the vendored Alma.Build build infrastructure.

## Entry points

- `./build.sh <Target>` restores local tools and packages, then runs `build/Build.fs`.
- `dotnet run --project ./build/build.fsproj -- <Target>` runs the build entrypoint directly.

## Common options

- `no-clean` disables cleaning directories in the first step. This is useful on CI when intermediate outputs need to remain available.
- `no-lint` skips the `Lint` step entirely.

## Updating vendored files

`build.sh`, `README.fbuild.md`, and the other vendored support files are copied from the `content/` directory of the pinned `Alma.Build` package. Re-copy them after bumping the pinned version.