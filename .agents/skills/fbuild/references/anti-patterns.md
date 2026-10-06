# Anti-Patterns

Format: **mistake → why it is wrong → fix**.

## Outdated / Non-existent API

- **Pinning `nuget Alma.Build 2.0.0` because the README says so** → the README's "Current engine version" and adoption pin still read `2.0.0`, but the engine on this branch is `3.0.0`; 2.0.0 has no `Solution` target, no `IProjectSources.Build`, no rendered tools manifest or `Directory.Build.props`, and still carries the removed `AllSources` field → pin the version in `src/Alma.Build/Alma.Build.fsproj` / the newest `CHANGELOG.md` entry, then run `Bootstrap`.

- **Setting `AllSources` on a spec or reading `IProjectSources.All`** → removed in 3.0.0; build sources are always sources ∪ tests, exposed as `IProjectSources.Build` → delete the field; widen `LibrarySources`/`ApplicationSources`/`TestsSources` instead.

- **Using `RuntimeId` or its `Other` case** → renamed in 2.0.0 → use `RuntimeTarget` and `Custom "<rid>"`.

- **Expecting `-r <rid>` on `Build`/`Tests`/`Run`/`Watch` under `AutoDetect`/`Specific`** → the README says the RID is passed as `-r`, but the engine appends `-p:AlmaBuildRuntimeIdentifier=<rid>` and relies on the deployed `Directory.Build.props` to set `RuntimeIdentifier` per project (`-r` breaks solution builds with `NETSDK1134`) → keep `Directory.Build.props` committed and write project conditions against `$(RuntimeIdentifier)`, which still works.

- **Relying on `RuntimeMode.AutoDetect` on a host missing from `RuntimeTargets`** → the README calls `AutoDetect` "independent of the release matrix", but `Targets.init` rejects a detected RID that is not listed and throws `Build runtime <rid> is not supported.` → list every developer/CI host RID in `RuntimeTargets`, or keep `RuntimeMode.Portable`.

## Common Mistakes

- **Leaving the default `TestsSources` for `tests/unit/*.fsproj`-style layouts** → `defaultLibrary` and `defaultConsoleApplication` glob `tests/*.fsproj` only; nested test projects are not built and `Tests` prints `There are no tests yet.` and passes → set `TestsSources = !! "tests/*/*.fsproj"` (or `tests/**/*.fsproj`) via the mapper.

- **Shipping `Spec.defaultExecutable` unchanged to a dev machine** → its `ReleaseDir` is the absolute `"/app"`, so `Release` publishes into the filesystem root (fine in a container, a permission error elsewhere) → set `ReleaseDir = "app"` when the release should land in the repo.

- **Keeping `defaultConsoleApplication`'s `ReleaseSource` with several source projects** → it is the first match of `./*.fsproj`, `src/*.fsproj`, `src/**/*.fsproj`, taken when the spec is built; with no match the call itself throws → set `ReleaseSource = "src/<App>/<App>.fsproj"` explicitly when there is more than one project.

- **Mismatching `ProjectMeta.Name` and the library directory** → `Release` packs `src/<Project.Name>` when it exists, else a lone root `*.fsproj`, else runs `dotnet pack` over the whole root → name the project after `src/<Name>`.

- **Not returning `Args.run`'s result from `main`** → the build failure becomes exit code 0 and CI goes green → end `main` with `args |> Args.run`.

- **Skipping `Bootstrap` after an engine bump or a `RuntimeMode` change** → support files are version-locked to the engine; a runtime-specific build without `Directory.Build.props` stops with `Directory.Build.props is missing ... Run the Bootstrap target first.` → run `./build.sh Bootstrap` and commit the result.

- **Keeping two solutions in the repo root** → `Build` fails listing every `.slnx`/`.sln`; `Solution` writes `<Project.Name>.slnx` and only warns about others → keep one committed, normally the generated one.

- **Writing test projects that only run under `dotnet test`** → `Tests` runs `dotnet run --no-build --project <proj>`, so a project without its own test-running entry point either fails to start or runs no tests → give test projects `<OutputType>Exe</OutputType>` and an Expecto `[<EntryPoint>]` calling `runTestsInAssemblyWithCLIArgs`.

- **Running `./build.sh Release` on a SAFE app** → `SAFEStackApplication` defines no `Release`/`Publish` target → run `./build.sh Bundle`; output goes to `DeployPath` (`deploy/`).

## Do Not Use / Avoid

- **Editing `build.sh`, `.editorconfig`, `fsharplint.json`, `build/README.md`, `.config/dotnet-tools.json` or `Directory.Build.props` by hand** → `Bootstrap` overwrites them unconditionally; a hand-added tool or MSBuild property is lost on the next run → change them upstream in the engine, or re-apply local MSBuild properties after every `Bootstrap`.

- **Calling `Command.run` / `runInRoot` / `toJob` from `Build.fs`** → the `Command` module is `internal` and does not compile from a consumer → use FAKE's `DotNet.exec`, `Shell`, or `CreateProcess` in extra targets.

- **Committing feed credentials or putting a NuGet key in `Build.fs`** → keys are read from the environment at `Publish` time → use `NugetApi.KeyInEnvironment "<VAR>"` or `NugetApi.Organization` with `PRIVATE_FEED_PASS` set by CI.

- **Using `NugetApi.AskForKey` in CI** → it prompts on the console for confirmation and the key → reserve it for manual releases; use `KeyInEnvironment` in pipelines.

- **Committing the generated `AssemblyInfo.fs`** → it is rewritten whenever its content changes (version, git branch/commit, build number) → git-ignore it.

## Wrong Abstractions

- **Expecting `Spec.map*` to change targets** → mappers only rewrite the spec record; target bodies are fixed by `Targets.init` → change behaviour through spec fields, or add a separate FAKE target in `Build.fs`.

- **Applying a mapper that does not match the constructor** (e.g. `Spec.defaultExecutable |> Spec.mapLibrary ...`) → the mapper returns other cases unchanged, so the override silently does nothing → pair `defaultLibrary`/`mapLibrary`, `defaultExecutable`/`mapExecutable`, `defaultConsoleApplication`/`mapConsoleApplication`, `defaultSAFEStackApplication`/`mapSAFEStackApplication`.

- **Setting `RuntimeMode` to get a platform-specific release** → `Release` always publishes every `RuntimeTargets` entry; `RuntimeMode` only affects local `Build`/`Tests`/`Run`/`Watch` → control the release matrix through `RuntimeTargets` and `PublishSingleFile`.

- **Expecting `LibrarySpec.Organization` to only label the package** → when set, `KeyInEnvironment` and `AskForKey` push to that organization's GitHub Packages feed instead of nuget.org → leave it `None` for nuget.org releases.
