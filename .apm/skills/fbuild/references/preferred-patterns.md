# Preferred Patterns

## Core Principles

- `build/Build.fs` is the only build file a consumer repo owns. Keep it to one `ProjectDefinition`: start from the `Spec.default*` constructor for the project shape and change only the fields that differ through the matching `Spec.map*`.
- Treat the deployed support files (`build.sh`, `.editorconfig`, `fsharplint.json`, `build/README.md`, `.config/dotnet-tools.json`, `Directory.Build.props`) as engine output: commit them, never hand-edit them, and refresh them with `./build.sh Bootstrap` after every engine version bump.
- Let the engine pick commands and arguments. Targets take everything from the spec; the `Command` runner is `internal` and not part of the consumer surface.
- `Build.fs` always has the same three steps: `args |> Args.init`, `Targets.init { ... }`, `args |> Args.run` as the `main` return value. See `examples.md` → Basic.

## Target Graph

`Targets.init` creates the chain for the spec case; running a target runs everything before it.

| Spec | Chain | Side targets |
| --- | --- | --- |
| `Library` | `Clean → AssemblyInfo → Build → Lint → Tests → Release → Publish` | — |
| `Executable` | `Clean → AssemblyInfo → Build → Lint → Tests → Release` | `Watch`, `WatchMirrord`, `Run`, `RunMirrord` after `Tests` |
| `ConsoleApplication` | `Clean → AssemblyInfo → Build → Lint → Tests → Release → ZipRelease` | `Watch`, `WatchMirrord`, `Run`, `RunMirrord` after `Build` |
| `SAFEStackApplication` | `SafeClean → AssemblyInfo → InstallClient → Build → Lint → Tests → Bundle` | `WatchTests` after `Lint`; `Run`, `RunMirrord` after `Build` |

- Every spec also gets `Info`, `Bootstrap` and `Solution`, which depend on nothing.
- Default target is `Build`. A target is given bare or after `-t`; every later argument is a build argument: `./build.sh -t Release no-clean`.
- `no-clean` skips `Clean`, `no-lint` skips `Lint`; the rest of the chain still runs.
- A `SAFEStackApplication` has no `Release`, `Publish` or `ZipRelease` target; `Bundle` is its release. Its `Tests` runs the server test project only.

## Spec Defaults

| Constructor | Sources | Tests | Release |
| --- | --- | --- | --- |
| `Spec.defaultLibrary` | `./*.fsproj`, `src/*.fsproj`, `src/**/*.fsproj` | `tests/*.fsproj` | `release/`, `Changelog = "CHANGELOG.md"`, `NugetApi.NotUsed` |
| `Spec.defaultExecutable` | `./*.fsproj`, `src/**/*.fsproj` | `tests/**/*.fsproj` | `ReleaseDir = "/app"` (absolute) |
| `Spec.defaultConsoleApplication targets` | `./*.fsproj`, `src/*.fsproj`, `src/**/*.fsproj` | `tests/*.fsproj` | `./dist`, `ReleaseSource` = first source match, `Portable`, `PublishSingleFile = true` |
| `Spec.defaultSAFEStackApplication templateVersion` | `src/**/*.fsproj` | `tests/**/*.fsproj` | `deploy/`; `src/{Shared,Server,Client}`, `tests/{Shared,Server,Client}` |

- `Library` and `ConsoleApplication` test globs are one level deep. Projects under `tests/<name>/` need `TestsSources = !! "tests/*/*.fsproj"`. See `examples.md` → Realistic.
- Globs and `ReleaseSource` are resolved against the working directory, which is the repo root under `build.sh`.
- `Changelog` is optional (`string option`, set when `CHANGELOG.md` exists) for every case but `Library`, where it is a required path.

## Recommended API Usage

- Library published to nuget.org from CI: `Spec.mapLibrary` with `NugetApi = NugetApi.KeyInEnvironment "NUGET_API_KEY"`. See `examples.md` → Realistic.
- Console app released for several runtimes: `Spec.defaultConsoleApplication [ Linux; OSX; OSXArm64 ]`; add `RuntimeMode`/`PublishSingleFile` through `Spec.mapConsoleApplication`. See `examples.md` → Console application with runtimes.
- Deployable app: `Spec.defaultExecutable` with a relative `ReleaseDir` for local runs. See `examples.md` → Executable.
- SAFE app: `Spec.defaultSAFEStackApplication "<template version>"`; the version is only written into `Info` and assembly metadata. See `examples.md` → SAFE stack.
- Git metadata: `Git = Git.init ()`. Use `None` only where there is no working copy; `AssemblyInfo` then reads `GIT_BRANCH`/`branch` and `GIT_COMMIT`/`commit` from the environment, else writes `unknown`.
- IDE solution: run `./build.sh Solution` once after adoption and whenever projects are added, then commit `<Project.Name>.slnx`.

## Runtime Selection

- `RuntimeMode` only exists on `ConsoleApplicationSpec`; the other cases always build portable.
- `Portable` passes no RID. `AutoDetect` resolves the host RID via `RuntimeTarget.detect ()`; `Specific target` uses that target.
- Both `AutoDetect` and `Specific` must resolve to a RID present in `RuntimeTargets`, otherwise `Targets.init` throws `Build runtime <rid> is not supported.` before any target runs.
- A resolved RID reaches `Build`, `Tests`, `Run`, `Watch` (and their Mirrord variants) as `-p:AlmaBuildRuntimeIdentifier=<rid>`; the deployed `Directory.Build.props` maps it to `RuntimeIdentifier` per project. Run `Bootstrap` after switching away from `Portable` and commit the props file.
- `Release` ignores `RuntimeMode`: it publishes every `RuntimeTargets` entry in parallel, self-contained, into `<ReleaseDir>/<rid>`, then zips each to `<ReleaseDir>/<rid>.zip`. An executable `<ReleaseDir>/zipCompiled` script, when present, runs before zipping.
- Use `Custom "<rid>"` for a RID with no named case.

## Publishing

`Publish` (library only) pushes `<ReleaseDir>/*.nupkg` with `--skip-duplicate`:

| `NugetApi` | Key source | Feed |
| --- | --- | --- |
| `NotUsed` | — | nothing pushed |
| `KeyInEnvironment name` | env var `name`; missing → build fails | nuget.org, or GitHub Packages of `LibrarySpec.Organization` when set |
| `AskForKey` | interactive confirm + password prompt | same as above |
| `Organization org` | env var `PRIVATE_FEED_PASS`; missing → build fails | GitHub Packages of `org` |

With `NugetApi.Organization`, setting `NUGET_SERVER_TOKEN` plus `NUGET_SERVER_REPOSITORY` (or `NugetCustomServerRepository`) also sends an `update-readme` repository dispatch to that repo, under `NUGET_SERVER_ORGANIZATION` or `org`.

`Release` packs `src/<Project.Name>` when that directory exists, else the single root `*.fsproj`, else whatever `dotnet pack` finds in the root.

## Error Handling

- The engine fails fast by raising: a missing API key, a missing `Directory.Build.props` under a configured runtime, more than one root solution, or a non-zero exit from any spawned command aborts the build.
- `Args.run` turns a `BuildFailedException` into exit code `1`. Return its result from `main` so CI sees the failure.
- Errors thrown inside `Targets.init` (an unsupported runtime) happen before `Args.run` and surface as an unhandled exception for every target, `Bootstrap` and `Info` included.

## Composition

- Override fields with a record copy inside the matching mapper: `Spec.defaultLibrary |> Spec.mapLibrary (fun spec -> { spec with ... })`. A mapper for another case is a no-op, so the mapper must match the constructor.
- Build globs with FAKE operators (`open Fake.IO.Globbing.Operators`): `!!` include, `++` add, `--` exclude.
- `Sources.Build` is always sources ∪ tests; there is no separate field to keep in sync. `build/build.fsproj` is excluded from it but still linted.
- Extra targets are plain FAKE: `Target.create` plus `==>` between `Targets.init` and `Args.run`. Spawn processes with FAKE (`DotNet.exec`, `CreateProcess`), not the engine's internal runner. See `examples.md` → Extra target.

## Integration with Other Libraries

- `AssemblyInfo` writes `AssemblyInfo.fs` beside every project in `Sources.Build`: title, product, description, version from the newest `CHANGELOG.md` release (`1.0` when it cannot be parsed), `InternalsVisibleTo "tests"`, and metadata `gitbranch`, `gitcommit`, `createdAt`, `buildNumber` (`BUILD_NUMBER`). A project only compiles it when its `.fsproj` lists `<Compile Include="AssemblyInfo.fs" />` ahead of the files that read it; keep the file git-ignored. See `examples.md` → Integration.
- `Alma.ApplicationStatus` consumes that metadata as `AssemblyVersionInformation.AssemblyMetadata_gitbranch` / `AssemblyMetadata_gitcommit` — an `internal` module in namespace `System`, readable only from the same assembly.
- The `Tests` target runs `dotnet run --no-build --project <test project>`, so each test project is an executable with an entry point — the Expecto `runTestsInAssemblyWithCLIArgs` shape.
- `Build` hands MSBuild the single root `.slnx`/`.sln` when there is one, else builds each project directory of `Sources.Build`. Test projects missing from that solution are not built, and `Tests --no-build` then fails.
- Set `RTK_ACTIVE` to route commands through `rtk` and compact their output; unset, output is that of the raw tools.

## Naming Conventions

- `open Alma.Build` then `open Utils` — the model types, `Spec`, `Git`, `Args` and `RuntimeTarget` cases live in `Alma.Build.Utils`; `Targets` is opened from `Alma.Build`.
- `NugetApi` and `RuntimeMode` are `[<RequireQualifiedAccess>]`: write `NugetApi.KeyInEnvironment`, `RuntimeMode.AutoDetect`. `RuntimeTarget` cases are used bare (`Linux`, `Custom "..."`).
- `ProjectMeta.Name` matches the library's `src/<Name>` directory and becomes `<Name>.slnx`.
- Target names are case-sensitive PascalCase strings: `Build`, `Release`, `Bootstrap`, `Solution`.

## Testing Recommendations

- Check a new or edited `Build.fs` with `./build.sh Info` (prints project, spec type, git and build number) and `./build.sh Build no-lint` before running release targets.
- Run `./build.sh Tests` once and confirm it does not print `There are no tests yet.` — that message means `TestsSources` matched nothing.
- After a version bump or a `RuntimeMode` change, run `./build.sh Bootstrap` and review the diff of the deployed files before committing.
- For a console app, run `./build.sh Release` locally at least once per new `RuntimeTargets` entry; CI on Linux still cross-publishes macOS/Windows archives.
