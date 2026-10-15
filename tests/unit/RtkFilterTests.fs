module Alma.Build.Tests.RtkFilterTests

open Expecto
open Alma.Build.RtkFilter

let private repoRoot = "/repo"

/// Apply a filter to already-split lines at a given exit code.
let private filtered (filter: Filter) exitCode lines = filter.Run lines exitCode

/// The tool's window separator is a full-width rule; the parser only treats it as a boundary
/// at the real width, so samples must use it.
let private ruleSeparator = System.String ('-', 80)

[<Tests>]
let okWhenEmptyTests =
    testList "Filter.okWhenEmpty" [
        test "should collapse to the message when inner keeps nothing and the exit is clean" {
            let filter = Filter.ofLines (fun _ -> []) |> Filter.okWhenEmpty "all good"

            let result = filtered filter 0 [ "noise" ]

            Expect.equal result [ "all good" ] "a clean empty run should show the ok message"
        }

        test "should show the raw lines when inner keeps nothing but the exit is non-zero" {
            let filter = Filter.ofLines (fun _ -> []) |> Filter.okWhenEmpty "all good"

            let result = filtered filter 1 [ "raw a"; "raw b" ]

            Expect.equal result [ "raw a"; "raw b" ] "a failing empty run must never claim ok"
        }

        test "should keep inner output when it is non-empty" {
            let filter = Filter.ofLines (fun _ -> [ "kept" ]) |> Filter.okWhenEmpty "all good"

            let result = filtered filter 0 [ "in" ]

            Expect.equal result [ "kept" ] "non-empty inner output should pass through unchanged"
        }
    ]

[<Tests>]
let dotnetBuildTests =
    let filter = Filter.dotnetBuild repoRoot

    testList "Filter.dotnetBuild" [
        test "should report ok when the exit is clean and there are no diagnostics" {
            let result = filtered filter 0 [ "Restore complete"; "Build succeeded" ]

            Expect.equal result [ "dotnet build: ok" ] "a clean build collapses to a single ok line"
        }

        test "should keep a relativized, deduplicated diagnostic when the build fails" {
            let line = "/repo/src/A.fs(10,5): error FS0001: boom [/repo/src/A.fs]"

            let result = filtered filter 1 [ line; line ]

            Expect.equal
                result
                [ "src/A.fs(10,5): error FS0001: boom [src/A.fs]" ]
                "a repeated diagnostic is deduped and its paths relativized"
        }

        test "should report ok when the only warnings are NuGet audit noise" {
            let line = "/repo/x.fsproj : warning NU1901: audit finding [/repo/x.fsproj]"

            let result = filtered filter 0 [ line ]

            Expect.equal result [ "dotnet build: ok" ] "NU audit warnings are noise"
        }

        test "should drop NU5xxx packaging warnings when building" {
            let line = "/repo/x.fsproj : warning NU5125: license is deprecated [/repo/x.fsproj]"

            let result = filtered filter 0 [ line ]

            Expect.equal result [ "dotnet build: ok" ] "packaging warnings are noise for a build"
        }

        test "should show raw output rather than ok when a failing run kept nothing" {
            let lines = [ "Restore complete"; "some progress" ]

            let result = filtered filter 1 lines

            Expect.equal result lines "a failing build must never be summarised as ok"
        }

        test "should fall back to raw output when a failing run carries an un-whitelisted error" {
            let lines = [ "MSBUILD : error : project file could not be loaded"; "Build FAILED" ]

            let result = filtered filter 1 lines

            Expect.equal result lines "a code-less error must not be summarised away"
        }
    ]

[<Tests>]
let dotnetPackTests =
    let filter = Filter.dotnetPack repoRoot

    testList "Filter.dotnetPack" [
        test "should keep NU5xxx packaging warnings when packing" {
            let line = "/repo/x.fsproj : warning NU5125: license is deprecated [/repo/x.fsproj]"

            let result = filtered filter 0 [ line ]

            Expect.equal
                result
                [ "x.fsproj : warning NU5125: license is deprecated [x.fsproj]" ]
                "packaging warnings are signal for pack"
        }

        test "should report ok when the only warnings are NU19xx audit noise" {
            let line = "/repo/x.fsproj : warning NU1901: audit finding [/repo/x.fsproj]"

            let result = filtered filter 0 [ line ]

            Expect.equal result [ "dotnet pack: ok" ] "audit warnings are noise even for pack"
        }

        test "should report ok when the only warnings are NU1510 prune noise" {
            let line =
                "x.fsproj : warning NU1510: PackageReference System.Text.Json will not be pruned."

            let result = filtered filter 0 [ line ]

            Expect.equal result [ "dotnet pack: ok" ] "prune warnings are noise even for pack"
        }
    ]

[<Tests>]
let dotnetRestoreTests =
    testList "Filter.dotnetRestore" [
        test "should report ok when the restore is clean" {
            let result = filtered (Filter.dotnetRestore repoRoot) 0 [ "Restore complete" ]

            Expect.equal result [ "dotnet restore: ok" ] "a clean restore collapses to a single ok line"
        }
    ]

[<Tests>]
let fsharplintTests =
    let filter = Filter.fsharplint repoRoot

    let oneWarning = [
        "========== Linting /repo/src/A.fs =========="
        "Found trailing whitespace at end of line."
        "Error on line 19 starting at column 33"
        "        Instance.create"
        "                       ^"
        "See https://fsprojects.github.io/FSharpLint/how-tos/rules/FL0061.html"
        ruleSeparator
        "========== Finished: 1 warnings =========="
        "========== Summary: 1 warnings =========="
    ]

    testList "Filter.fsharplint" [
        test "should report ok when the run is clean" {
            let clean = [
                "========== Linting /repo/src/A.fs =========="
                "========== Finished: 0 warnings =========="
                "========== Summary: 0 warnings =========="
            ]

            let result = filtered filter 0 clean

            Expect.equal result [ "fsharplint: ok" ] "a clean lint collapses to a single ok line"
        }

        test "should format a warning with its rule code when the tool named a rule" {
            let result = filtered filter 0 oneWarning

            Expect.equal
                result
                [ "fsharplint: 1 warning"; "  src/A.fs:19:33 FL0061  Found trailing whitespace at end of line." ]
                "a warning renders as a compact file:line:col rule message line"
        }

        test "should split two warnings in one file on the full-width separator" {
            let twoInOneFile = [
                "========== Linting /repo/src/A.fs =========="
                "First warning."
                "Error on line 5 starting at column 3"
                "See https://fsprojects.github.io/FSharpLint/how-tos/rules/FL0001.html"
                ruleSeparator
                "Second warning."
                "Error on line 9 starting at column 7"
                "See https://fsprojects.github.io/FSharpLint/how-tos/rules/FL0002.html"
                ruleSeparator
                "========== Finished: 2 warnings =========="
                "========== Summary: 2 warnings =========="
            ]

            let result = filtered filter 0 twoInOneFile

            Expect.equal
                result
                [ "fsharplint: 2 warnings"
                  "  src/A.fs:5:3 FL0001  First warning."
                  "  src/A.fs:9:7 FL0002  Second warning." ]
                "the separator splits one file's warnings into distinct entries"
        }

        test "should omit the rule code when the tool printed no rule URL" {
            let noRule = [
                "========== Linting /repo/src/B.fs =========="
                "Some message without a url."
                "Error on line 3 starting at column 1"
                ruleSeparator
                "========== Finished: 1 warnings =========="
                "========== Summary: 1 warnings =========="
            ]

            let result = filtered filter 0 noRule

            Expect.equal
                result
                [ "fsharplint: 1 warning"; "  src/B.fs:3:1  Some message without a url." ]
                "a warning with no rule URL leaves the code out entirely"
        }

        test "should hand back raw output when the parse disagrees with the tool's tally" {
            let drift = [
                "========== Linting /repo/src/A.fs =========="
                "Found trailing whitespace at end of line."
                "Error on line 19 starting at column 33"
                "See https://fsprojects.github.io/FSharpLint/how-tos/rules/FL0061.html"
                ruleSeparator
                "========== Finished: 1 warnings =========="
                "========== Summary: 2 warnings =========="
            ]

            let result = filtered filter 0 drift

            Expect.equal
                (List.head result)
                "fsharplint: parsed 1 of 2 warnings — output format changed, showing it raw"
                "a count mismatch leads with a drift notice"
            Expect.equal (List.tail result) drift "the drift notice is followed by the raw output"
        }

        test "should distrust the parse when the run printed no summary banner" {
            let noSummary = [
                "========== Linting /repo/src/A.fs =========="
                "Found trailing whitespace at end of line."
                "Error on line 19 starting at column 33"
                "See https://fsprojects.github.io/FSharpLint/how-tos/rules/FL0061.html"
                ruleSeparator
                "========== Finished: 1 warnings =========="
            ]

            let result = filtered filter 0 noSummary

            Expect.equal
                (List.head result)
                "fsharplint: no summary banner — output truncated, showing it raw"
                "a missing summary with warnings present leads with a distrust notice"
            Expect.equal (List.tail result) noSummary "the distrust notice is followed by the raw output"
        }

        test "should show raw output rather than ok when a clean parse comes from a failing run" {
            let clean = [
                "========== Linting /repo/src/A.fs =========="
                "========== Finished: 0 warnings =========="
                "========== Summary: 0 warnings =========="
            ]

            let result = filtered filter 1 clean

            Expect.equal result clean "a failing lint must never claim ok"
        }
    ]

[<Tests>]
let fableTests =
    let filter = Filter.fable

    let cleanRun = [
        "Fable 5.0.0-rc.7: F# to JavaScript compiler"
        "Minimum @fable-org/fable-library-js version (when installed from npm): 2.0.0-rc.6"
        ""
        "Stand with Ukraine! https://standwithukraine.com.ua/"
        ""
        "Parsing src/Client/Client.fsproj..."
        "Project and references (198 source files) parsed in 4369ms"
        "Loaded Feliz.HookAttribute from ../../Feliz.CompilerPlugins.dll"
        "Started Fable compilation..."
        "Compiled 198/198: src/Client/Page/EntriesSearch/View.fs"
        "Fable compilation finished in 13684ms"
    ]

    testList "Filter.fable" [
        test "should collapse a clean run to its compiled tally when the exit is clean" {
            let result = filtered filter 0 cleanRun

            Expect.equal result [ "fable: compiled 198/198 in 13684ms" ] "a successful compile reduces to a single tally line"
        }

        test "should show raw output when the run fails" {
            let result = filtered filter 1 cleanRun

            Expect.equal result cleanRun "a failing compile shows its raw output so the error is never hidden"
        }

        test "should keep a diagnostic when a clean run emitted a warning" {
            let warning =
                "./src/Client/Api.fs(9,11): (9,12) warning FSHARP: Incomplete pattern matches on this expression. (code 25)"
            let withWarning = cleanRun @ [ warning ]

            let result = filtered filter 0 withWarning

            Expect.equal result [ warning ] "a warning on a clean run is surfaced, not summarised away"
        }

        test "should report ok when a clean run printed no closing tally" {
            let result = filtered filter 0 [ "Started Fable compilation..." ]

            Expect.equal result [ "fable: ok" ] "a clean run with no tally still collapses to ok"
        }

        test "should collapse a clean run to its tally when Fable colours its lines" {
            let coloured = cleanRun |> List.map (fun line -> $"\u001b[32m{line}\u001b[0m")

            let result = filtered filter 0 coloured

            Expect.equal result [ "fable: compiled 198/198 in 13684ms" ] "colour codes do not hide the tally"
        }

        test "should keep Vitest's summary after the tally when a clean run ran tests" {
            let vitestRun =
                cleanRun @ [
                    " ✓ output/Shared.Tests.js (1 test) 8ms"
                    ""
                    " Test Files  1 passed (1)"
                    "      Tests  1 passed (1)"
                    "   Start at  11:10:29"
                ]

            let result = filtered filter 0 vitestRun

            Expect.equal
                result
                [ "fable: compiled 198/198 in 13684ms"; "Test Files  1 passed (1)"; "Tests  1 passed (1)" ]
                "a passing test run reports how many files and tests passed"
        }

        test "should show raw output with the failing test's stack when a test run fails" {
            let failingRun =
                cleanRun @ [
                    " ❯ output/Shared.Tests.js (1 test | 1 failed) 15ms"
                    "   × Shared > should double the value when given a number 12ms"
                    " ❯ Shared.Tests.fs:9:13"
                    "      Tests  1 failed (1)"
                ]

            let result = filtered filter 1 failingRun

            Expect.equal result failingRun "a failing test keeps its name and its .fs line"
        }
    ]

[<Tests>]
let testsTests =
    let filter = Filter.tests

    let expectoRun = [
        "\u001b[37m[\u001b[37m11:07:19 \u001b[37mINF\u001b[37m] \u001b[37mEXPECTO? Running tests... <Expecto>"
        "\u001b[1000D\u001b[?25h[11:07:19 INF] EXPECTO! 2 tests run in 00:00:00.21 for All – 2 passed, 0 ignored, 0 failed, 0 errored. Success! <Expecto>"
    ]

    testList "Filter.tests" [
        test "should collapse a clean run to Expecto's summary when the runner is Expecto" {
            let result = filtered filter 0 expectoRun

            Expect.equal
                result
                [ "tests: 2 tests run in 00:00:00.21 for All – 2 passed, 0 ignored, 0 failed, 0 errored. Success!" ]
                "a passing Expecto run reduces to its summary, without colour codes"
        }

        test "should report ok when a clean run printed no Expecto summary" {
            let result = filtered filter 0 [ "Passed!" ]

            Expect.equal result [ "tests: ok" ] "another runner's clean run collapses to ok"
        }

        test "should show raw output when the run fails" {
            let failingRun = [
                "[11:10:03 ERR] All.Shared.should double the value when given a number failed in 00:00:00.01."
                "Doubling 21 must give 42."
                "   at Expecto.Expect.equal"
            ]

            let result = filtered filter 1 failingRun

            Expect.equal result failingRun "a failing test keeps its name and message"
        }
    ]

[<Tests>]
let toLinesTests =
    testList "toLines" [
        test "should strip carriage returns and drop the trailing empty line when the capture is CRLF-terminated" {
            let result = toLines "a\r\nb\r\n"

            Expect.equal result [ "a"; "b" ] "a CRLF-terminated capture yields clean lines with no trailing blank"
        }

        test "should keep interior blank lines when only the final line is empty" {
            let result = toLines "a\n\nb\n"

            Expect.equal result [ "a"; ""; "b" ] "only the final empty line is dropped"
        }

        test "should keep the line when the capture has no trailing newline" {
            let result = toLines "solo"

            Expect.equal result [ "solo" ] "a capture without a trailing newline is one line"
        }
    ]

[<Tests>]
let runBufferedTests =
    testList "runBuffered" [
        test "should split and filter a raw capture when given a clean build" {
            let result = runBuffered (Filter.dotnetBuild repoRoot) "Build succeeded\n" 0

            Expect.equal result [ "dotnet build: ok" ] "a clean captured build collapses to ok"
        }
    ]
