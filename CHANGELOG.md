# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased

- [**BC**] the `RuntimeId` spec type is now `RuntimeTarget` and its `Other` case is now `Custom`.
- Added `OSXArm64` runtime target.
- Console applications can select a portable, auto-detected, or specific runtime ID that is then used by build, run, watch, and Mirrord targets
- Console application releases can opt out of single-file publishing via a new `PublishSingleFile` spec field.
- A console release gives each runtime target its own intermediate directory to isolate them safely.

## 2.0.0 - 2026-09-07
- Initial implementation after extracting the hand-copied FAKE build infrastructure into its own library. `README.md` covers the consumer side — targets, adoption, and updating; `CONTRIBUTING.md` covers working on the engine itself.
