# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased

- `Bootstrap` now renders `.config/dotnet-tools.json` from the project spec.
- The tools list excludes `fake-cli` as it's no longer needed for the build process.
- `.config/dotnet-tools.json` is rendered with `System.Text.Json`.
- Fix: the package now declares an `FSharp.Core` dependency, so consumers resolve a
  version consistent with the rest of `Alma.Build`'s dependency graph instead of
  falling back to whichever `FSharp.Core` their local SDK ships (which could mismatch
  at runtime with a `FileNotFoundException`).

## 2.0.0 - 2026-09-09
- Initial implementation after extracting the hand-copied FAKE build infrastructure into its own library. `README.md` covers the consumer side — targets, adoption, and updating; `CONTRIBUTING.md` covers working on the engine itself.
- [**BC**] the `RuntimeId` spec type is now `RuntimeTarget` and its `Other` case is now `Custom`.
- [**BC**] the `Publish` target now crashes if the Nuget API key is not supplied.
- Added `OSXArm64` runtime target.
- Console applications can select a portable, auto-detected, or specific runtime ID that is then used by build, run, watch, and Mirrord targets
- Console application releases can opt out of single-file publishing via a new `PublishSingleFile` spec field.
- A console release gives each runtime target its own intermediate directory to isolate them safely.
