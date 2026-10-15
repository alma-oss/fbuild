---
name: library-fable-tests
description: Moves an F# library that compiles to .NET and Fable onto Alma.Build 4's tests/Shared and tests/DotNet layout (LibraryTests.shared), so `./build.sh -t Tests` runs Expecto and Vitest. Use to migrate a library's tests.
---

# Migrate a library's tests to Alma.Build 4

For a library that compiles to both .NET and Fable. Any other library only changes `build/Build.fs`, then
stops: replace `Spec.defaultLibrary` with `Spec.library LibraryTests.dotNet` (custom glob:
`Spec.library (LibraryTests.DotNet (<TestsSources glob>))`), `LibraryTests.noTests`, or, for a library that
only exists under Fable, `LibraryTests.fable` (tests alone in `tests/Fable`, entry `Fable.Tests.fs`, run in jsdom).

## Target layout

```
package.json, package-lock.json   # vitest
build/Build.fs                    # Spec.library LibraryTests.shared
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

Before step 1, adapt the affected steps and report each adaptation at the end.

- **No Fable content** (`grep -rln 'PackagePath="fable\|Fable' src --include=*.fsproj --include=paket.references`
  prints nothing): only the `build/Build.fs` change above; stop.
- **The library is a package its own tests use** (Alma.Testing): skip its pin; test projects reference
  `src/` with `ProjectReference`.
- **Tests that check how a test run ends** (fixtures that fail or focus on purpose, runner output): a Vitest
  suite must pass in one run, so keep them in `tests/DotNet`, driving their own fixture through Fable and
  Vitest. Put the fixture in a subdirectory such as `tests/DotNet/fixtures/`; the engine ignores it.
- **A file that must stay verbatim and calls `register`** (an upstream copy): rename it to the entry
  `Shared.Tests.fs`, or ask the user.

## Steps

### 1. Run the script

On a clean working tree, from the repository root, run fbuild's
`bin/migrate-library-fable-tests`. It sends all command output to `migration.log` and prints
only `migration-report.md`. Do your own triage only when the report asks for it.

- Before changing anything it lists what it needs from you, all at once: npm, leftover Fable.Mocha tests, several
  test projects, and the `Alma.Testing` line with its exact version and where it goes: after the Expecto packages
  or above a `// [ FAKE GROUP ]` comment (which belongs to `group Build`); in a `group Tests`, also under
  `group Tests` in `tests/*/paket.references`. The script never adds it, so run it once to learn the version
  (it changes nothing), add the line, then run again.
- Editing a file dirties the tree, so after doing what it asked, run it again with `--rerun`, which continues on
  the tree as it is. A plain run refuses a dirty tree.
- A run that started clean can be undone with `git checkout -- . && git clean -fd`; never on a tree that held
  uncommitted work of the user's.
- When the script cannot run, or the report says a step was left: [references/manual-steps.md](references/manual-steps.md).

Done when the script ends with a report.

### 2. Work through the report

Read `migration-report.md` in full. After a `--rerun`, also read `migration-report.previous.md`: the new report
covers only that run, so the earlier "Guessed by the script" list is not repeated.

- **Left for you:** files kept in `tests/DotNet` because they mix .NET-only tests or unsupported `Expect`
  calls, and modules outside the shared prefix. Classify and split them with
  [references/shared-tests.md](references/shared-tests.md); take shared and .NET-only counts from
  `--list-tests` (`<root list>.<…>`) only when a split needs them. Report out-of-line modules to the user.
- **Guessed by the script:** check each line. A shortened name is wrong when it resolves to another module:
  compare the Shared list's names with `Alma.Testing`'s (`Expect`, `Test`, `Tests`, `Accuracy`, `FocusState`)
  and with the library's own modules (`grep -rh '^module' src`).
- **Checks:** a `FAILED` line is a fault to fix.

`./build.sh -t Tests` runs Clean, Build and Lint first. While splitting, build `tests/Shared` and
`tests/DotNet` with `dotnet build` and run `dotnet run --project tests/DotNet`, or pass `no-clean no-lint`
after the target. A call runs one target; further words are build arguments.

When "Left for you" is empty and every check is `ok`, review only the guesses. Done when each "Left for you"
item is marked fixed or reported to the user, and every guess is checked.

### 3. Resolve Fable's warnings

If the report counts `warning FABLE` lines, use [references/fable-warnings.md](references/fable-warnings.md).
Done when the count is 0.

### 4. Update docs and agent instructions

Find the CI workflows that run `./build.sh` and every document and agent instruction that names the old test
project, its commands, or rules about `tests/` and `build/`; update them with
[references/docs-and-instructions.md](references/docs-and-instructions.md). Done when none names the old test
project, path or commands.

### 5. Verify and hand off

The script's own Tests run covers everything it changed. Run `./build.sh Release` only if `src` or `build`
files changed after the script's run. Done when the report's checks are all `ok` (or Release passes) and the
Expecto total equals the baseline in `migration-baseline.txt`, unless tests were deliberately changed.

Leave every change uncommitted unless asked, and tell the user what `git status` should show:

- modified: `paket.dependencies`, `paket.lock`, `package.json`, `.config/dotnet-tools.json`, `build/Build.fs`, the
  CI workflows, the README and other docs, and the moved tests;
- new (to `git add`): `package-lock.json`, the `vitest.config.mts` files, the generated `<Project.Name>.slnx`.

The script's `migration.log`, `migration-report.md`, `migration-report.previous.md` and `migration-baseline.txt`
stay out of git; delete them after the hand-off.
