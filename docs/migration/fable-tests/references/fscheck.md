# Expecto.FsCheck 10 to 11 (FsCheck 3)

Read this before step 1 when `paket.lock` resolves Expecto.FsCheck 10.x or FsCheck 2.x; the script then stops
and asks for it. Alma.Testing needs Expecto 11, and Expecto.FsCheck 11 is built on FsCheck 3, whose API differs.
Do the upgrade on its own, before the script, so the script's baseline counts the upgraded tests.

## Upgrade

1. Note the .NET test total first: `dotnet run --project tests/<old>.fsproj` (or `tests/Server` in an
   application) prints `EXPECTO! <N> tests run`.
2. In `paket.dependencies`, pin `nuget Expecto ~> 11.0` and `nuget Expecto.FsCheck ~> 11.0`, and `nuget FsCheck
   ~> 3.0` where FsCheck is pinned directly; then `dotnet tool run paket install`.
3. Build the test project and fix every error with the table below. Then run it.

Done when the build has no FsCheck errors or new FsCheck warnings, the tests pass, and the total equals step 1's.
Then run the script from step 1 with `--rerun`, since the tree is no longer clean. Its undo discards this upgrade
too.

## Keep each property's meaning

The upgrade changes how a property is written, never what it checks:

- Keep every generator's range, distribution and filter, every `maxTest`, and every property; never delete, skip,
  or `ptestProperty` one to make the build pass.
- A property that fails only under FsCheck 3 has either found a real fault or depends on a changed default
  generator. Find which; fix a fault in `src` only with the user's consent, and report it. Never narrow a
  generator just to make the property pass.
- When FsCheck 3 has no exact equivalent, write the closest one and report the difference to the user.

## API changes

The F# API moved to `FsCheck.FSharp`: keep `open FsCheck` (for `Check`, `Config`, `Arbitrary`, `Gen<'T>` and the
wrapper types such as `PositiveInt` and `NonEmptyString`) and add `open FsCheck.FSharp` for the `Gen`, `Arb`,
`ArbMap` and `Prop` modules, `gen { }` and `==>`.

| FsCheck 2 | FsCheck 3 |
| --- | --- |
| `Arb.generate<'T>` | `ArbMap.defaults \|> ArbMap.generate<'T>` |
| `Arb.from<'T>`, `Arb.Default.<Type>()`, `Arb.Default.Derive()` | `ArbMap.defaults \|> ArbMap.arbitrary<'T>` |
| `Arb.register<MyArbs> () \|> ignore` | `{ FsCheckConfig.defaultConfig with arbitrary = [ typeof<MyArbs> ] }` with `testPropertyWithConfig`; there is no global registration |
| `Gen.sample size n gen` (a list) | `Gen.sampleWithSize size n gen` (an array); `Gen.sample n gen` takes the default size |
| `Gen.suchThat` | `Gen.filter` |
| `{ Config.Default with MaxTest = n }` | `Config.Default.WithMaxTest n` (a class with `With…` methods); under Expecto set `maxTest` on `FsCheckConfig` |
| `StdGen`, a pinned `replay` seed | FsCheck 3's `Rnd`; take `replay`'s type from the compiler and a new seed from a failing run, since old seeds do not carry over |

A type holding static members that return `Arbitrary<'T>` still works as an `arbitrary` entry. A member that
called `Arb.generate` or `Arb.Default` inside it changes as the table says.

When an error is not in the table, look the member up in FsCheck 3's API rather than guessing a name.
