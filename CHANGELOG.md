# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased
- [**BC**] A library states its tests when it creates its spec: replace `Spec.defaultLibrary` with `Spec.library LibraryTests.dotNet` (tests in `tests/*.fsproj`), `LibraryTests.noTests`, `LibraryTests.shared` (`tests/Shared` and `tests/DotNet`) or `LibraryTests.fable` (`tests/Fable` alone, for a library that only exists under Fable), and drop `TestsSources`; a custom glob is `Spec.library (LibraryTests.DotNet (<glob>))`.
- [**BC**] SAFE-stack client tests run under Vitest. Move them from Fable.Mocha to `Alma.Testing` (`bin/migrate-to-alma-testing` in the engine repository), replace `mocha` with `vitest` and `jsdom` in your `devDependencies`, then run `Bootstrap` and commit `tests/Client/vitest.config.mts`.
- [**BC**] The `InstallClient` target is now `InstallNpm`.
- [**BC**] Test projects see a project's internal code by their own assembly name, not a fixed `tests`. A test project outside the spec's tests loses that access.
- A library can test its Fable-compiled code under Vitest, with `tests/Shared` in node or `tests/Fable` in jsdom. See `docs/migration/library-fable-tests/SKILL.md` and `bin/migrate-library-fable-tests` in the engine repository.
- `WatchTests` and `Tests` work for every project type, and `Tests` runs a project's test suites concurrently.
- A failing test shows its full output.
- `bin/migrate-to-alma-build` in the engine repository moves a repository on the hand-copied FAKE build onto `Alma.Build` 4.

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
