# Upgrading Vite and Vitest

Read this when a script asks for a newer Vite, or to move a project already on Alma.Testing to the versions it
supports. Alma.Testing declares Vitest 5, which needs Vite ^6.4, ^7 or ^8.

| Project | Run |
| --- | --- |
| a SAFE-stack application still on Fable.Mocha | `bin/migrate-safe-stack-tests --upgrade-vite`, which does both in one run |
| a SAFE-stack application already on Alma.Testing, or one to measure first | `bin/upgrade-vite` |
| a library (Vitest brings its own Vite) | `bin/upgrade-vite`, which only raises Vitest |

`bin/upgrade-vite` takes the same `--work-dir` and `--rerun` as the other scripts.

## What the script does

For a client (a `package.json` listing `vite`) it:

- builds the client with `Bundle` and records the JS and CSS size;
- installs the latest Vite 8, @vitejs/plugin-react 6 and Vitest 5;
- raises each other package whose Vite peer range excludes Vite 8 to its latest release when that admits it,
  and lists the ones no release does;
- in `src/Client/vite.config.*`, drops `minify: "esbuild"` (Vite 8 no longer installs esbuild and minifies with
  Oxc by default) and `build.target` (the JS and the CSS then target the browsers Baseline lists as widely
  available, Vite 8's default), renames `rollupOptions` to `rolldownOptions`, and drops an
  `optimizeDeps.esbuildOptions` holding only a target;
- runs `Bundle` and `Tests` again and checks the installed majors, Vite's deprecation warnings, both sizes
  against the baseline (at most 10% larger) and the Vitest total.

## Left for you

- **An app that must support browsers older than Baseline's widely available ones:** restore `build.target` and
  set `build.cssTarget` to those browsers (versions such as `"chrome100"`; `cssTarget` does not take
  `"baseline-widely-available"`). Vite 8 minifies CSS with Lightning CSS, which lowers modern CSS (`oklch()`,
  nesting) for that target with colour fallbacks and prefixes, so expect a Tailwind or daisyUI stylesheet to
  grow, up to double for `es2020`; the size check fails when it does.
- **`optimizeDeps.esbuildOptions` with more than a target:** move each option to
  `optimizeDeps.rolldownOptions`; Vite 8 converts the old block with a deprecation warning and will drop it.
- **`cssMinify: "esbuild"` or a top-level `esbuild` block:** drop `cssMinify` (Lightning CSS) or install
  esbuild; `esbuild` options become `oxc`, whose JSX options differ.
- **`manualChunks`:** Rolldown splits with `output.codeSplitting.groups`.
- **Babel options in `react({ babel: … })`:** @vitejs/plugin-react 6 dropped Babel; move them to
  `@rolldown/plugin-babel`.
- **A plugin no release supports Vite 8 for:** replace it or drop it, then run again with `--rerun`.
- **A package raised to its latest release:** read its changelog for a major version's breaking changes.

## Check by hand

The script builds and tests; it does not run the app. Start it (`./build.sh Run`, or `RunMirrord` where the
server needs the cluster) and open the client: the page renders, a change hot-reloads with Elmish HMR, and the
server calls work. The first page load after a restart can be slower than on Vite 6; a reload is faster.

## When it does not work

Vite 8 has no switch back to Rollup. Undo the run (`git checkout -- . && git clean -fd` on a tree that started
clean) and stay on Vitest 5 with Vite 6.4 or 7: `npm install --save-dev vite@7` covers the Vitest 5 minimum. The
Vite migration guide (<https://vite.dev/guide/migration>) suggests trying the `rolldown-vite` package on Vite 7
first to separate bundler problems from the rest.
