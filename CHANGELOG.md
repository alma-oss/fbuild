# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased

- Add a `Bootstrap` target that deploys the vendored support files bundled in the engine assembly into the consuming repository, preserving their directory structure and marking `build.sh` executable. The files ship as embedded resources instead of package `content/`, and the consumer quick reference moves from `README.fbuild.md` to `build/README.md`.
- Initial implementation, superseding the hand-copied FAKE build infrastructure. `README.md` covers the consumer side — targets, adoption, and updating; `CONTRIBUTING.md` covers working on the engine itself.
- Fix target selection to take the target from the first argument whatever follows it. `Args.run` previously matched only a bare target or `-t <target>`, so `Release no-clean` fell through to the default and silently ran `Build`.
- Fix `Tests` to run every project matched by `TestsSources`. It previously used that glob only as an emptiness check and then ran a single `dotnet run` in `tests/`, so a repository whose test projects live in subdirectories reported "There are no tests yet." and passed without running anything.
