# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased

- Initial implementation, superseding the hand-copied FAKE build infrastructure.
- Add vendoring guide for consumer repositories (`docs/vendoring.md`): pin `Alma.Build` in `paket.dependencies`, add a `build/` project, and copy the packaged support files into the repo root.
- Fix target selection to take the target from the first argument whatever follows it. `Args.run` previously matched only a bare target or `-t <target>`, so `Release no-clean` fell through to the default and silently ran `Build`.
- Fix `Tests` to run every project matched by `TestsSources`. It previously used that glob only as an emptiness check and then ran a single `dotnet run` in `tests/`, so a repository whose test projects live in subdirectories reported "There are no tests yet." and passed without running anything.
