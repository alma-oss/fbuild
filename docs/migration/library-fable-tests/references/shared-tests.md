# Sharing tests between .NET and Fable

Read this for a file kept in `tests/DotNet`, an out-of-line module, or a Shared helper the DotNet half
uses. Move tracked files with `git mv`; ignore the `NU1510` build warning.

## What a Shared list may use

- `open Alma.Testing` instead of `open Expecto`, `open Fable.Mocha` or the `#if FABLE_COMPILER` open block.
- No `[<Tests>]`: expose the list as `tests`; the entry gathers it.
- Only what Alma.Testing has: `testList`, `testCase`, `testCaseAsync`, `test`, `testAsync`, their `p`/`f`
  variants, `testSequenced`, `failtest`, `failtestf`, and `Expect.` `equal`, `notEqual`, `isTrue`, `isFalse`,
  `isNull`, `isNotNull`, `isSome`, `wantSome`, `isNone`, `isOk`, `wantOk`, `isError`, `wantError`, `isEmpty`,
  `isNonEmpty`, `hasLength`, `contains`, `containsAll`, `exists`, `all`, `stringContains`, `isGreaterThan`,
  `isLessThan`, `throws`, `throwsC`, `isNotNaN`, `isNotInfinity`, `floatClose`, `floatLessThanOrClose`,
  `floatGreaterThanOrClose`. Rewrite other calls from step 1's grep, usually with `Expect.isTrue`.
- `failtest`, not `failwith`: under Fable `failwith` loses the `.fs` line.
- No DOM; the suite runs in plain Node.

## Mixed files

Decide test by test: shared when it and everything it calls run under Fable, .NET-only otherwise. Position,
list membership and section comments decide nothing; a list holding both becomes two lists.

- Shared tests of `<X>.fs` stay in `tests/Shared/<X>.fs` as `module <Library.Namespace>.Test.<X>`.
- .NET-only tests move to `tests/DotNet/<X>.fs`. Rename its module before the first build
  (`<Library.Namespace>.Test.<X>DotNet`): equal names collide, with a confusing error. It keeps `[<Tests>]` and
  `open Expecto`, and opens `<Library.Namespace>.Test.<X>` and every Shared module it uses.
- Shared helpers the DotNet half uses: list `grep -nE "(let|module|type) private" tests/Shared/*.fs` and grep
  each name in `tests/DotNet`. For each, make it public in Shared (`Utils.fs` for helpers several files use),
  move it to DotNet, or restructure. A public helper cannot mention a library `internal` type in its
  signature: make it `internal` and put `<Compile Include="AssemblyInfo.fs" />` first in
  `Shared.Tests.fsproj`, whose generated `InternalsVisibleTo` covers `DotNet.Tests`. Anything Fable compiles,
  `Result.orFail` included, is fine in Shared.
- FsCheck-free fixtures (constants, stubs) go to `tests/Shared/Fixtures.fs`; generators stay in DotNet. When
  shared tests open a generators module only for such values, move them to `Fixtures.fs` and have the
  generators module open it.
- Update both projects' compile lists, the entry's list and the DotNet opens; build errors point to what is
  missing.

## The entry

Last in compile order and the suite's only `register`: a module registering on its own is never run, or runs
twice if the entry imports it. Its full name must not also be a namespace of other test modules.

```fsharp
module <Library.Namespace>.Test.Shared.Tests

open Alma.Testing
open <Library.Namespace>.Test

let tests = testList "Shared" [ Parsing.tests; Formatting.tests ]

register tests
```

The script writes this shape when the shared modules have a common prefix, and fully qualified names
otherwise. Report a test module outside `<Library.Namespace>.Test` to the user.

Done when `dotnet build tests/Shared`, `dotnet build tests/DotNet` and `dotnet run --project tests/DotNet` pass.
