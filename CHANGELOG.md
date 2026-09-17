# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased

- `FSharp.Core` is now declared as a dependency of the packed engine. A consumer whose `Build` group lock already held an older FSharp.Core kept it across `paket install`, and the engine failed to load at startup.
- `Microsoft.Build.Framework`, `Microsoft.Build.Utilities.Core`, and `Microsoft.NET.StringTools` are capped below 18.10, the release that moved them to `net11.0`. The cap ships through the nuspec, so consumer build projects stop warning about an unsupported target framework.
- `Bootstrap` now renders `.config/dotnet-tools.json` from the project spec.
- The tools list excludes `fake-cli` as it's no longer needed for the build process.
- `.config/dotnet-tools.json` is rendered with `System.Text.Json`.
- Fixed the build in a repository that has both a solution and a configured runtime. Such a repo now also receives a `Directory.Build.props` — run `Bootstrap` and commit it when upgrading, and fold any MSBuild properties of your own back into it. While it is missing the build stops up front and tells you to run `Bootstrap`.
- [**BC**] the `AllSources` spec field is now `BuildSources` and `IProjectSources.All` is now `IProjectSources.Build`. Dropped `build/build.fsproj` from the build sources, while `Lint` still covers it.
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
