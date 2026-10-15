# Library branch

Read this before step 1 for a library that compiles to both .NET and Fable.

## Other libraries

A library that compiles to .NET only, or has no Fable content, needs only a `build/Build.fs` change, then stops:
replace `Spec.defaultLibrary` with `Spec.library LibraryTests.dotNet` (custom glob:
`Spec.library (LibraryTests.DotNet (<TestsSources glob>))`), `LibraryTests.noTests`, or, for a library that only
exists under Fable, `LibraryTests.fable` (tests alone in `tests/Fable`, entry `Fable.Tests.fs`, run in jsdom).
No Fable content means `grep -rln 'PackagePath="fable\|Fable' src --include=*.fsproj --include=paket.references`
prints nothing.

## Target layout

```
package.json, package-lock.json   # vitest
build/Build.fs                    # Spec.library LibraryTests.dotNetAndFable
tests/
  Shared/                         # tests of code that runs on both sides
    Shared.Tests.fsproj           # references src/<Library>
    Fixtures.fs, Utils.fs         # optional: FsCheck-free fixtures, then helpers both halves use
    <Area>.fs                     # module <Library.Namespace>.Test.<Area>, exposes `tests`
    Shared.Tests.fs               # entry, last: one list, `register`
    vitest.config.mts             # rendered by Bootstrap
  DotNet/                         # .NET-only tests
    DotNet.Tests.fsproj           # Expecto executable, references ../Shared
    <Area>.fs                     # module <Library.Namespace>.Test.<Area>DotNet, [<Tests>]
    DotNet.Tests.fs               # Expecto entry point, the old Tests.fs renamed
```

The names are fixed: Vitest runs only `output/Shared.Tests.js`. Every test module sits under
`<Library.Namespace>.Test`. Test files are `<Area>.fs`, never `<Area>.Tests.fs`; only the entries and the two
projects carry `.Tests`. Rename a legacy `<Area>.Tests.fs` with `git mv`, with its module and `Compile Include`.

## Check the repository fits

Adapt the affected steps and report each adaptation at the end.

- **The library is a package its own tests use** (Alma.Testing): skip its pin; test projects reference
  `src/` with `ProjectReference`.
- **Tests that check how a test run ends** (fixtures that fail or focus on purpose, runner output): a Vitest
  suite must pass in one run, so keep them in `tests/DotNet`, driving their own fixture through Fable and
  Vitest. Put the fixture in a subdirectory such as `tests/DotNet/fixtures/`; the engine ignores it.
- **A file that must stay verbatim and calls `register`** (an upstream copy): rename it to the entry
  `Shared.Tests.fs`, or ask the user.

