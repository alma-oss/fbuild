# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased
- [**BC**] Replace `Spec.defaultLibrary` with `Spec.library`, which takes the library's tests, and remove the library `TestsSources` field
    - `LibraryTests.dotNet` for tests in `tests/*.fsproj`
    - `LibraryTests.noTests`
    - `LibraryTests.dotNetAndFable` for `tests/Shared` and `tests/DotNet`
    - `LibraryTests.fable` for `tests/Fable` alone, in a library that only exists under Fable
    - `LibraryTests.DotNet (<glob>)` for a custom glob
- [**BC**] Run SAFE-stack client tests under Vitest: move them from Fable.Mocha to `Alma.Testing` with `bin/migrate-safe-stack-tests` in the engine repository, replace `mocha` with `vitest` and `jsdom` in your `devDependencies`, then run `Bootstrap` and commit `tests/Client/vitest.config.mts`
- [**BC**] Rename `InstallClient` target to `InstallNpm`
- [**BC**] Grant test projects access to a project's internal code by their own assembly name instead of a fixed `tests`; a test project outside the spec's tests loses that access
- Add Vitest tests of a library's Fable-compiled code, `tests/Shared` in node or `tests/Fable` in jsdom; migrate with `bin/migrate-library-fable-tests` in the engine repository, see `docs/migration/fable-tests/SKILL.md`
- Allow `WatchTests` and `Tests` for every project type
- Run a project's test suites concurrently in `Tests`
- Show a failing test's full output
- `bin/migrate-safe-stack-tests` and `bin/migrate-library-fable-tests` install Vitest 5, which Alma.Testing 1.0.0-preview.4 needs; a SAFE-stack application on Vite older than 6.4 passes `--upgrade-vite` to the former
- `bin/migrate-safe-stack-tests` and `bin/migrate-library-fable-tests` stop on Expecto.FsCheck 10 or FsCheck 2 until the tests move to Expecto.FsCheck 11 and FsCheck 3, see `docs/migration/fable-tests/references/fscheck.md`
- Add `bin/upgrade-vite` in the engine repository, moving a project onto Vitest 5 and a SAFE-stack client onto Vite 8 and `@vitejs/plugin-react` 6
- Add `bin/migrate-to-alma-build` in the engine repository, moving a repository from the hand-copied FAKE build onto `Alma.Build` 4
- Print each test suite's output in `Tests` as one block as soon as that suite finishes

## 3.1.0 - 2026-10-14
- Fix `RaspberryPiHassioAddon` to use real framework target `linux-musl-arm64`
- `Solution` drops whitespace and characters that are not safe in a file name from the project name, so `My Project` generates `MyProject.slnx`. A project name with nothing usable left fails the target. Another solution at the repo root whose contents match the generated one is deleted, so rerunning `Solution` migrates an old-named file by itself; commit the rename. A solution with different contents is kept and warned about, as before.
- Downgrade `fable` tool to 5.0.0 for SAFE-stack projects
  - With `Fable.Remoting.Client`, 5.4.0 sends records the server cannot read and 5.13.0 fails to compile the client. Rerun `Bootstrap` and commit `.config/dotnet-tools.json`.
- SAFE-stack `Tests` now runs the Fable.Mocha client tests in `tests/Client` alongside the server tests, and a failing client test fails the build. Add `mocha` to the `devDependencies` of your `package.json`.

## 3.0.0 - 2026-09-25
- `FSharp.Core` is now declared as a dependency of the packed engine. A consumer whose `Build` group lock already held an older FSharp.Core kept it across `paket install`, and the engine failed to load at startup.
- `Microsoft.Build.Framework`, `Microsoft.Build.Utilities.Core`, and `Microsoft.NET.StringTools` are capped below 18.10, the release that moved them to `net11.0`. The cap ships through the nuspec, so consumer build projects stop warning about an unsupported target framework.
- `Bootstrap` now renders `.config/dotnet-tools.json` from the project spec.
- The tools list excludes `fake-cli` as it's no longer needed for the build process.
- `.config/dotnet-tools.json` is rendered with `System.Text.Json`.
- Fixed the build in a repository that has both a solution and a configured runtime. Such a repo now also receives a `Directory.Build.props` — run `Bootstrap` and commit it when upgrading, and fold any MSBuild properties of your own back into it. While it is missing the build stops up front and tells you to run `Bootstrap`.
- [**BC**] removed the `AllSources` spec field: the build sources are now always the spec's sources plus its tests. `IProjectSources.All` is now `IProjectSources.Build`. Dropped `build/build.fsproj` from the build sources, while `Lint` still covers it.
- `Build` now fails when the repo root holds more than one solution, `.slnx` or `.sln`, instead of picking one of them. Keep one committed.
- Added a `Solution` target that generates `<Project.Name>.slnx` at the repo root from the project's declared sources, overwriting it on every run.

## 2.0.0 - 2026-09-09
- Initial implementation after extracting the hand-copied FAKE build infrastructure into its own library. `README.md` covers the consumer side — targets, adoption, and updating; `CONTRIBUTING.md` covers working on the engine itself.
- [**BC**] the `RuntimeId` spec type is now `RuntimeTarget` and its `Other` case is now `Custom`.
- [**BC**] the `Publish` target now crashes if the Nuget API key is not supplied.
- Added `OSXArm64` runtime target.
- Console applications can select a portable, auto-detected, or specific runtime ID that is then used by build, run, watch, and Mirrord targets
- Console application releases can opt out of single-file publishing via a new `PublishSingleFile` spec field.
- A console release gives each runtime target its own intermediate directory to isolate them safely.
