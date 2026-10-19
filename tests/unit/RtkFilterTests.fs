module Alma.Build.Tests.RtkFilterTests

open Expecto
open Alma.Build.RtkFilter

let private repoRoot = "/repo"

/// Apply a filter to stdout lines at a given exit code, returning the text of the lines it keeps.
let private filtered (filter: Filter) exitCode lines =
    filter.Run (List.map Line.stdout lines) exitCode |> List.map Line.text

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
            let filter = Filter.ofLines (fun _ -> [ Line.stdout "kept" ]) |> Filter.okWhenEmpty "all good"

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

        test "should show raw output when a failing run names no error and ran no tests" {
            let result = filtered filter 1 cleanRun

            Expect.equal result cleanRun "a failure the filter cannot name shows its raw output so it is never hidden"
        }

        test "should keep only the compile errors when compilation fails" {
            let error = "./src/Client/Api.fs(9,11): (9,12) error FSHARP: The value 'x' is not defined. (code 39)"
            let failingCompile = cleanRun @ [ error; error; "Compilation failed" ]

            let result = filtered filter 1 failingCompile

            Expect.equal result [ error ] "a failing compile reduces to its deduplicated errors"
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
    ]

[<Tests>]
let vitestTests =
    let filter = Filter.vitest repoRoot

    let compiled = [
        "Started Fable compilation..."
        "Compiled 5/5: Doubling.fs"
        "Fable compilation finished in 2542ms"
        ".> env NODE_ENV=test npx vitest run --reporter=json"
    ]

    let tally = "fable: compiled 5/5 in 2542ms"

    let failingTest =
        """{"ancestorTitles":["Shared","Doubling"],"title":"should double the value when given a number","status":"failed","failureMessages":["Error: Doubling 21 must give 42.\nexpected: 43\n  actual: 42\n    at failWithDiff (/repo/tests/Shared/output/fable_modules/Alma.Testing.1.0.0/Testing.fs:133:16)\n    at Array.test (/repo/tests/Shared/Doubling.fs:9:13)\n    at runTest (file:///repo/node_modules/@vitest/runner/dist/chunk-hooks.js:1574:12)"]}"""

    let passingTest =
        """{"ancestorTitles":["Shared"],"title":"should pass","status":"passed","failureMessages":[]}"""

    let file status message (tests: string list) =
        sprintf
            """{"assertionResults":[%s],"status":"%s","message":"%s","name":"/repo/tests/Shared/output/Shared.Tests.js"}"""
            (String.concat "," tests)
            status
            message

    /// The report line as Fable's `--run` prints it, behind its own colour codes.
    let report (failed, passed, skipped) (files: string list) =
        sprintf
            "\u001b[39m\u001b[22m{\"numTotalTestSuites\":3,\"numTotalTests\":%d,\"numPassedTests\":%d,\"numFailedTests\":%d,\"numPendingTests\":%d,\"numTodoTests\":0,\"testResults\":[%s]}"
            (failed + passed + skipped)
            passed
            failed
            skipped
            (String.concat "," files)

    testList "Filter.vitest" [
        test "should collapse to the tally and the counts when every test passes" {
            let run = compiled @ [ report (0, 2, 1) [ file "passed" "" [ passingTest; passingTest ] ] ]

            let result = filtered filter 0 run

            Expect.equal result [ tally; "vitest: 2 passed, 1 skipped (3)" ] "a passing run reports what ran and what was skipped"
        }

        test "should keep each failing test with its message and own frames when a test fails" {
            let run = compiled @ [ report (1, 1, 0) [ file "failed" "" [ failingTest; passingTest ] ]; "\u001b[31mCompilation failed" ]

            let result = filtered filter 1 run

            Expect.equal
                result
                [
                    tally
                    "vitest: 1 failed, 1 passed (2)"
                    "  × Shared > Doubling > should double the value when given a number"
                    "    Error: Doubling 21 must give 42."
                    "    expected: 43"
                    "      actual: 42"
                    "    at Array.test (tests/Shared/Doubling.fs:9:13)"
                ]
                "a failure keeps its message and the suite's own F# frame, without dependency or runner frames"
        }

        test "should name the test file when the file itself fails" {
            let run = compiled @ [ report (0, 0, 0) [ file "failed" "Failed to load url ./missing.js" [] ] ]

            let result = filtered filter 1 run

            Expect.equal
                result
                [ tally; "vitest: 0 passed (0)"; "  × tests/Shared/output/Shared.Tests.js"; "    Failed to load url ./missing.js" ]
                "a file that fails to load is reported by its path and message"
        }

        test "should list ten failures and count the rest when more tests fail" {
            let run = compiled @ [ report (12, 0, 0) [ file "failed" "" (List.replicate 12 failingTest) ] ]

            let result = filtered filter 1 run

            Expect.equal (result |> List.filter (fun line -> line.StartsWith "  × ") |> List.length) 10 "ten failures are listed"
            Expect.equal (List.last result) "  … 2 more failures" "the failures past ten are counted"
        }

        test "should keep only the compile errors when compilation fails" {
            let error = "./Doubling.fs(9,11): (9,12) error FSHARP: The value 'x' is not defined. (code 39)"
            let run = [ "Started Fable compilation..."; error; "Compilation failed" ]

            let result = filtered filter 1 run

            Expect.equal result [ error ] "a failing compile reduces to its errors"
        }

        test "should show raw output when a failing run's report lists no failure" {
            let run = compiled @ [ report (0, 1, 0) [ file "passed" "" [ passingTest ] ] ]

            let result = filtered filter 1 run

            Expect.equal result run "a failure the report does not explain is never summarised away"
        }

        test "should show raw output when a clean run wrote no report" {
            let result = filtered filter 0 compiled

            Expect.equal result compiled "a clean run without a report cannot claim its tests passed"
        }
    ]

[<Tests>]
let testsTests =
    let filter = Filter.tests repoRoot

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

        test "should keep each failing test's message and own frames when the run fails" {
            let failingRun = [
                "\u001b[37m[\u001b[37m09:25:32 \u001b[37mINF\u001b[37m] \u001b[37mEXPECTO? Running tests...\u001b[37m \u001b[37m<\u001b[37mExpecto\u001b[37m>\u001b[37m"
                "\u001b[0m\u001b[37m[09:25:32 \u001b[31mERR\u001b[37m] Shared.Doubling.should double the value when given a number failed in 00:00:00.04. "
                "Doubling 21 must give 42."
                "\u001b[32mexpected\u001b[36m: 4\u001b[32m3\u001b[36m"
                "\u001b[31m  actual\u001b[36m: 4\u001b[31m2\u001b[36m"
                "   at Fixture.Test.Doubling.tests@8.Invoke(Unit _arg1) in /repo/tests/Shared/Doubling.fs:line 9"
                "   at Expecto.Impl.execTestAsync@584-1.Invoke(Unit unitVar)"
                "   at Microsoft.FSharp.Control.Trampoline.Execute(FSharpFunc`2 firstAction) in D:\\a\\async.fs:line 112 <Expecto>"
                "\u001b[0m[09:25:32 INF] EXPECTO! 2 tests run in 00:00:00.15 for miscellaneous – 1 passed, 0 ignored, 1 failed, 0 errored. <Expecto>"
                "\u001b[0m"
            ]

            let result = filtered filter 1 failingRun

            Expect.equal
                result
                [
                    "[09:25:32 ERR] Shared.Doubling.should double the value when given a number failed in 00:00:00.04."
                    "Doubling 21 must give 42."
                    "expected: 43"
                    "  actual: 42"
                    "   at Fixture.Test.Doubling.tests@8.Invoke(Unit _arg1) in tests/Shared/Doubling.fs:line 9"
                    "tests: 2 tests run in 00:00:00.15 for miscellaneous – 1 passed, 0 ignored, 1 failed, 0 errored."
                ]
                "a failing run keeps the test's name, message and own frame, without colour or framework frames"
        }

        test "should keep every line of an unfamiliar runner's failure when the run fails" {
            let failingRun = [ "Failed MyTest [12 ms]"; "  Assert.Equal() Failure" ]

            let result = filtered filter 1 failingRun

            Expect.equal result failingRun "only lines known to be noise are dropped"
        }

        test "should keep each kept line on the stream it came from when the run fails" {
            let failingRun = [ Line.stdout "Failed MyTest [12 ms]"; { Stream = Stderr; Text = "Unhandled exception." } ]

            let result = filter.Run failingRun 1

            Expect.equal result failingRun "a stderr line stays on stderr"
        }
    ]
