# Doing the script's steps by hand

Read this when the script cannot run or its report says a step was left.

## Pin the packages

In `paket.dependencies`, add `nuget Alma.Testing ~> 1.0` to the main group and raise the `Build`
group to `nuget Alma.Build ~> 4.0`.

Keep the Expecto pin if it is within Alma.Testing's `[11.0.0, 12)`; otherwise move it inside. A test-only
package stays in the main group when that pin satisfies it (`Expecto.FsCheck ~> 11.0`); one that needs a newer
Expecto than the pin allows goes in its own Paket group.

Remove `Fable.Mocha` from `paket.dependencies` and every `paket.references`, then run
`dotnet tool restore && dotnet tool run paket install`. Done when the install succeeds; commit `paket.lock`.

## Add npm

`./build.sh` runs `npm install` and fails without a `package.json`. Add `vitest` (`^3.2.4`) to the root
`package.json`'s `devDependencies` (create a `"private": true, "type": "module"` file if none), run
`npm install`, commit `package-lock.json`, and git-ignore `node_modules/` and `output/`.

## Select the tests

```fsharp
Spec.library LibraryTests.shared
|> Spec.mapLibrary (fun library -> { library with NugetApi = NugetApi.KeyInEnvironment "NUGET_API_KEY" })
```

`Spec.defaultLibrary` and `TestsSources` no longer exist; the tests are the argument of `Spec.library`.

## Projects

- The old test project becomes `tests/DotNet/DotNet.Tests.fsproj`: add a level to every relative path in it
  (`../../src/…`, `..\..\.paket\Paket.Restore.targets`) and a `ProjectReference` to
  `../Shared/Shared.Tests.fsproj`.
- `tests/Shared/Shared.Tests.fsproj` is a library project referencing `src/<Library>`, with `Alma.Testing` in
  its `paket.references` and the `Paket.Restore.targets` import.
- A project only the tests use goes to `Shared.Tests.fsproj` when shared tests need it (it must compile under
  Fable), otherwise to `DotNet.Tests.fsproj`.
- Leave `AssemblyInfo.fs` out of the test projects; the library's own grants them `internal` access
  (exception: `tests/Shared`'s, see shared-tests.md). Delete `tests/bin`, `tests/obj` and the generated,
  git-ignored `tests/AssemblyInfo.fs`.

Run `./build.sh AssemblyInfo`. Done when `grep InternalsVisibleTo src/*/AssemblyInfo.fs` shows `Shared.Tests`
and `DotNet.Tests`; only `tests` means `Alma.Build` is too old.

## The DotNet runner

Rename the old runner (usually `Tests.fs`) to `tests/DotNet/DotNet.Tests.fs`. Expecto discovers `[<Tests>]`
only in its own assembly, so mark the Shared list there and keep the old `runTestsInAssemblyWithCLIArgs`
call. Give the file a fully qualified `module` declaration (without one F# warns that `DotNet.Tests` is not a valid
identifier) and open the shared prefix, as the Shared entry does:

```fsharp
module <Library.Namespace>.Test.DotNet.Tests

open Expecto
open <Library.Namespace>.Test

[<Tests>]
let sharedTests = Shared.Tests.tests

[<EntryPoint>]
let main argv = Tests.runTestsInAssemblyWithCLIArgs [ Parallel ] argv
```

Done when `dotnet run --project tests/DotNet` passes.

## Bootstrap

`./build.sh Bootstrap` renders each suite's `vitest.config.mts` and adds `fable` to
`.config/dotnet-tools.json`; `./build.sh Solution` writes `<Project.Name>.slnx` with the new projects. Delete
an older solution of another name (`Build` fails on two) and commit all of it.

## Old plumbing and CI

Delete Fable.Mocha runners and pages, hand-written Vitest or Mocha configs, npm scripts running `fable` or
`mocha`, and the CI steps that called them.

`Release` and `Publish` run `InstallNpm` and `Tests`, so every workflow that runs `./build.sh` needs Node first:

```yaml
- uses: actions/setup-node@v4
  with:
    node-version: 24
```

Pin the action as the repository pins its others; keep `node-version` 20 or later. Update cache keys naming the
old test project (`hashFiles('tests/tests.fsproj')`). Add Node.js and npm to the README's Requirements.
Done when every file from `grep -rln "build.sh" .github/workflows` sets up Node and `./build.sh Release` passes.
