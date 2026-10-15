# SAFE-stack application branch

Read this before step 1 for an application whose `build/Build.fs` uses `Spec.defaultSAFEStackApplication`.
It needs Alma.Build 4 first (client tests under Vitest need it): run `bin/migrate-to-alma-build` if the pin is
older; the script stops and says so otherwise.

## Target layout

The template's layout stays; only the client runner and the libraries change:

```
package.json                   # vitest and jsdom in devDependencies, mocha and global-jsdom out
tests/
  Client/                      # Vitest in jsdom
    Client.Tests.fs            # the runner: one list, `register`; the only file Vitest runs
    <Area>.fs                  # client tests, `open Alma.Testing`, exposing `tests`
    vitest.config.mts          # rendered by Bootstrap
  Server/                      # Expecto executable
    Server.Tests.fs            # entry: `[<Tests>]` hook for the Shared list, `runTestsInAssemblyWithCLIArgs`
  Shared/                      # tests of shared code, written with Alma.Testing
```

`Client.Tests.fs` registers the client tests and the Shared list; the server's Expecto entry marks the Shared
list with `[<Tests>]`, so shared tests run under both. Nothing else under `tests/Client` is a Vitest file.

## What the script does

It replaces `Fable.Mocha` with `Alma.Testing` in sources, `paket.references` and `paket.dependencies` (in place,
so the line keeps its position); turns `Mocha.runTests x` into `register x`; renames another runner to
`Client.Tests.fs`; deletes the browser page and Vite config that served Fable.Mocha (`index.html`,
`vite.config.*` in `tests/Client`); adds the Shared list to the server's Expecto entry; swaps the npm packages;
runs Bootstrap and Tests; and checks the result. It never edits `Build.fs`: the spec call does not change.

When `Fable.Mocha` is not in `paket.dependencies`, the `Alma.Testing` line is an ask like in the library branch.
Pass `--alma-testing <version>` when Alma.Testing is a preview (for example `1.0.0-preview.4`) that
`~> 1.0` does not match. It installs Vitest 5, which needs Vite ^6.4, ^7 or ^8: with an older Vite in
`package-lock.json` the script stops and asks for `--upgrade-vite`, which also moves the client to Vite 8 and
@vitejs/plugin-react 6 in the same run, as [vite.md](vite.md) describes.

## Offer Vite 8

Before step 1, read the client's Vite from `package-lock.json` (`npm ls vite`). Below 8, ask the user which of
these to do, and say what each costs:

- **Upgrade in the same run:** pass `--upgrade-vite`. One run and one review, but no bundle-size comparison.
- **Upgrade afterwards:** run the script without it, then `bin/upgrade-vite` as [vite.md](vite.md) describes. It
  records the bundle size before the upgrade and checks it after.
- **Stay:** run without it. Possible only on Vite 6.4 or 7; an older Vite needs one of the two above.

## Left for you

- **A second runner:** Vitest runs only `Client.Tests.fs`. Add that module's tests to the list `Client.Tests.fs`
  registers and drop its `register`.
- **An `Expect` function Alma.Testing lacks:** express it with `Expect.isTrue` or another listed function. The
  supported set is in `shared-tests.md`, "What a Shared list may use".
- **Leftover Fable.Mocha API:** `Mocha.*`, `SyncTest`, `AsyncTest`, `TestListSequential`, `FocusState`: use
  `testCase`, `testList`, `ptestCase`, `ftestCase`.
- **Shared list not on the server:** reference `tests/Shared` from the server test project and mark each list with
  `[<Tests>]` in the file with the `[<EntryPoint>]`.
- **Failures:** use `failtest`, not `failwith`: under Fable `failwith` loses the `.fs` line.

## By hand

Do these when the script cannot run, in order:

1. `paket.dependencies` and every `paket.references`: replace `Fable.Mocha` with `Alma.Testing`; run
   `dotnet tool restore && dotnet tool run paket install`.
2. Sources: `open Fable.Mocha` and the `#if FABLE_COMPILER open Fable.Mocha #else open Expecto #endif` block
   become `open Alma.Testing`; the runner becomes `register all`.
3. `npm uninstall mocha global-jsdom && npm install --save-dev vitest jsdom`; commit `package-lock.json`.
4. `./build.sh Bootstrap` renders `tests/Client/vitest.config.mts`; commit it.
5. Every workflow that runs `./build.sh` has `actions/setup-node` before it; the README's Requirements name
   Node.js and npm.

Done when `./build.sh Tests` passes with the client tests reported by Vitest.
