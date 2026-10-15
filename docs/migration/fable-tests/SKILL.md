---
name: fable-tests
description: Moves an F# library that compiles to .NET and Fable, or a SAFE-stack application, onto Alma.Testing under Alma.Build 4, so `./build.sh Tests` runs the Fable-side tests in Vitest, and upgrades a project's Vite and Vitest to the versions Alma.Testing supports. Use to migrate Expecto or Fable.Mocha tests, or to move a client to Vite 8 and Vitest 5.
---

# Migrate Fable tests to Alma.Testing and Alma.Build 4

Pick the branch from `build/Build.fs` and read its reference first: it has the target layout and the fit checks.

| Project | Script | Reference |
| --- | --- | --- |
| a library that compiles to .NET and Fable | `bin/migrate-library-fable-tests` | [references/library.md](references/library.md) |
| `Spec.defaultSAFEStackApplication` | `bin/migrate-safe-stack-tests` | [references/safe-stack.md](references/safe-stack.md) |
| a project already on Alma.Testing, or blocked by an old Vite | `bin/upgrade-vite` | [references/vite.md](references/vite.md) |

Any other library only changes `build/Build.fs`, as `references/library.md` says under "Other libraries".

A project still on Expecto.FsCheck 10 (FsCheck 2) upgrades to Expecto.FsCheck 11 (FsCheck 3) first, with
[references/fscheck.md](references/fscheck.md).

## Steps

### 1. Run the script

Create a working directory outside the worktree and pass it to every call: `work=$(mktemp -d)`, then
`<script> --work-dir "$work"`. The script keeps its log (`migration.log`), report, baseline and previous report
there and prints only the report; a rerun needs the same directory. `/tmp` is cleared on restart, and
`rm -rf "$work"` ends it sooner.

On a clean working tree, from the repository root, run the branch's script. For an application, first offer
the user Vite 8, as [references/safe-stack.md](references/safe-stack.md) says under "Offer Vite 8". Do your
own triage only when the report asks for it.

- Before changing anything it lists what it needs from you, all at once: npm, the `Alma.Testing` line when it
  cannot replace Fable.Mocha's, an older Alma.Build, a Vite older than Vitest 5 needs (the remedy is in
  [references/vite.md](references/vite.md)), Expecto.FsCheck 10 or FsCheck 2 (the remedy is in
  [references/fscheck.md](references/fscheck.md)), and the branch's own preconditions. The `Alma.Testing`
  line goes after the Expecto packages or above a `// [ FAKE GROUP ]` comment (which belongs to
  `group Build`); in a `group Tests`, also under `group Tests` in `tests/*/paket.references`. The script never
  adds a line it was not given: run it once to learn the exact version (it changes nothing), add the line,
  then run again.
- Editing a file dirties the tree, so after doing what it asked, run it again with `--rerun`, which continues on
  the tree as it is. A plain run refuses a dirty tree.
- A run that started clean can be undone with `git checkout -- . && git clean -fd`; never on a tree that held
  uncommitted work of the user's.
- When the script cannot run, or the report says a step was left: the branch reference (the library's
  [references/manual-steps.md](references/manual-steps.md), the application's "By hand" list).

Done when the script ends with a report.

### 2. Work through the report

Read `$work/migration-report.md` in full. After a `--rerun`, also read `$work/migration-report.previous.md`: the new report
covers only that run, so the earlier "Guessed by the script" list is not repeated.

- **Left for you:** the branch reference lists the usual items and how to settle them; library files kept in
  `tests/DotNet` use [references/shared-tests.md](references/shared-tests.md). Report out-of-line modules to the
  user.
- **Guessed by the script:** check each line. A shortened name is wrong when it resolves to another module:
  compare the Shared list's names with `Alma.Testing`'s (`Expect`, `Test`, `Tests`, `Accuracy`, `FocusState`)
  and with the project's own modules (`grep -rh '^module' src`).
- **Checks:** a `FAILED` line is a fault to fix.

`./build.sh -t Tests` runs Clean, Build and Lint first. While splitting, build the test projects with
`dotnet build` and run the .NET suite with `dotnet run --project tests/DotNet` (a library) or `tests/Server`
(an application), or pass `no-clean no-lint` after the target. A call runs one target; further words are build
arguments.

When "Left for you" is empty and every check is `ok`, review only the guesses. Done when each "Left for you"
item is marked fixed or reported to the user, and every guess is checked.

### 3. Resolve Fable's warnings

A library's report counts `warning FABLE` lines: resolve them with
[references/fable-warnings.md](references/fable-warnings.md); done when the count is 0. An application's report
counts them for information only; leave the ones its own code already had.

### 4. Update docs and agent instructions

Find the CI workflows that run `./build.sh` and every document and agent instruction that names the old test
project, its commands, or rules about `tests/` and `build/`; update them with
[references/docs-and-instructions.md](references/docs-and-instructions.md). Done when none names the old test
project, path or commands.

### 5. Verify and hand off

The script's own Tests run covers everything it changed. Run `./build.sh Release` (a library) or
`./build.sh Bundle` (an application) only if `src` or `build` files changed after the script's run. Done when the
report's checks are all `ok` and the .NET test total matches `$work/migration-baseline.txt`, unless tests were
deliberately changed.

Leave every change uncommitted unless asked, and tell the user what `git status` should show:

- modified: `paket.dependencies`, `paket.lock`, `package.json`, the `Build.fs` (a library), the CI workflows, the
  README and other docs, and the changed or moved tests (an application also changes `tests/Server` and
  deletes `tests/Client/index.html` and its `vite.config.*`; after a Vite upgrade, also `src/Client/vite.config.*`);
- new (to `git add`): `package-lock.json`, the `vitest.config.mts` files, and for a library `.config/dotnet-tools.json`
  changes and the generated `<Project.Name>.slnx`.

Nothing the script wrote is in the worktree; remove `$work` if you like.
