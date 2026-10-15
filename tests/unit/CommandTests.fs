module Alma.Build.Tests.CommandTests

open Expecto
open Alma.Build.Command

[<Tests>]
let renderTests =
    testList "Command.render" [
        test "should prepend the shared nuget verb when given Nuget Push" {
            let result = Command.render (Nuget Push)

            Expect.equal result ("dotnet", [ "nuget"; "push" ]) "Nuget Push composes onto the shared nuget verb"
        }

        test "should prepend the shared nuget verb when given Nuget AddSource" {
            let result = Command.render (Nuget AddSource)

            Expect.equal result ("dotnet", [ "nuget"; "add"; "source" ]) "Nuget AddSource composes onto the shared nuget verb"
        }
    ]

[<Tests>]
let modeTests =
    let modeOf command =
        let transport, filter = Rtk.mode command
        transport, Option.isSome filter

    testList "Rtk.mode" [
        test "should route through err with a filter when given Dotnet Build" {
            Expect.equal (modeOf (Dotnet Build)) (Rtk.Transport.Err, true) "a build is filtered and errors-only"
        }

        test "should route through raw with a filter when given Dotnet Lint" {
            Expect.equal (modeOf (Dotnet Lint)) (Rtk.Transport.Raw, true) "a lint is filtered but spawned directly, not through rtk"
        }

        test "should route through raw with a filter when given Dotnet Tests" {
            Expect.equal (modeOf (Dotnet Tests)) (Rtk.Transport.Raw, true) "a test run is filtered by the engine, which keeps a failure whole"
        }

        test "should route through err with a filter when given Dotnet Pack" {
            Expect.equal (modeOf (Dotnet Pack)) (Rtk.Transport.Err, true) "a pack is filtered so packaging warnings survive"
        }

        test "should route through err with a filter when given Dotnet Restore" {
            Expect.equal (modeOf (Dotnet Restore)) (Rtk.Transport.Err, true) "a restore is filtered like a build"
        }

        test "should route through raw with a filter when given Dotnet Fable" {
            Expect.equal (modeOf (Dotnet Fable)) (Rtk.Transport.Raw, true) "fable is filtered by the engine, which keeps a failing test's stack whole"
        }
    ]

[<Tests>]
let wrapTests =
    testList "Rtk.wrap" [
        test "should leave the command untouched when the transport is raw" {
            let result = Rtk.wrap Rtk.Transport.Raw "dotnet" [ "build" ]

            Expect.equal result ("dotnet", [ "build" ]) "a raw transport spawns the command directly"
        }

        test "should prefix rtk err when the transport is err" {
            let result = Rtk.wrap Rtk.Transport.Err "dotnet" [ "build"; "x" ]

            Expect.equal result ("rtk", [ "err"; "dotnet"; "build"; "x" ]) "an err transport wraps the command in rtk err"
        }
    ]

[<Tests>]
let exitCodeTests =
    testList "ExitCode.isSuccess" [
        test "should hold when the code is zero" {
            Expect.isTrue (ExitCode.isSuccess (ExitCode 0)) "exit code 0 is a success"
        }

        test "should not hold when the code is non-zero" {
            Expect.isFalse (ExitCode.isSuccess (ExitCode 1)) "a non-zero exit code is a failure"
        }
    ]

[<Tests>]
let shouldTeeTests =
    testList "shouldTee" [
        test "should not tee when the run succeeded" {
            let result = shouldTee (ExitCode 0) (CapturedOutput "a\nb\nc\n") [ "a" ]

            Expect.isFalse result "a clean run needs no full-output log even if the filter dropped lines"
        }

        test "should tee when a failing run had meaningful lines suppressed" {
            let result = shouldTee (ExitCode 1) (CapturedOutput "a\nb\nc\n") [ "summary" ]

            Expect.isTrue result "a failure whose filter hid meaningful output is worth teeing"
        }

        test "should not tee when a failing run kept every meaningful line" {
            let result = shouldTee (ExitCode 1) (CapturedOutput "a\nb\n") [ "a"; "b" ]

            Expect.isFalse result "a failure that suppressed nothing meaningful needs no full-output log"
        }
    ]

[<Tests>]
let compactTraceTests =
    testList "CompactTrace" [
        testList "isSeparator" [
            test "should hold for a run of dashes" {
                Expect.isTrue (CompactTrace.isSeparator "------") "a dashed rule is a separator"
            }

            test "should hold for a run of equals signs" {
                Expect.isTrue (CompactTrace.isSeparator "======") "an equals rule is a separator"
            }

            test "should not hold when the line mixes rule characters with text" {
                Expect.isFalse (CompactTrace.isSeparator "--- Info ---") "a line with text is not a separator"
            }

            test "should not hold for an empty line" {
                Expect.isFalse (CompactTrace.isSeparator "") "an empty line is not a separator"
            }
        ]

        testList "isNoise" [
            test "should hold for a dependency-graph line" {
                Expect.isTrue (CompactTrace.isNoise "Shortened DependencyGraph for ...") "the dependency graph is planning noise"
            }

            test "should hold for a FAKE command echo" {
                Expect.isTrue (CompactTrace.isNoise "git rev-parse (In: /repo)") "an internal command echo is noise"
            }

            test "should not hold for ordinary output" {
                Expect.isFalse (CompactTrace.isNoise "Compiling Foo.fs") "real command output is not noise"
            }
        ]

        testList "isCommitHash" [
            test "should hold for a 40-character hex string" {
                Expect.isTrue (CompactTrace.isCommitHash (System.String ('a', 40))) "a 40-char hex string is a bare SHA"
            }

            test "should not hold for a 39-character hex string" {
                Expect.isFalse (CompactTrace.isCommitHash (System.String ('a', 39))) "a SHA must be exactly 40 characters"
            }

            test "should not hold for a 40-character non-hex string" {
                Expect.isFalse (CompactTrace.isCommitHash (System.String ('g', 40))) "a non-hex string is not a SHA"
            }
        ]
    ]

[<Tests>]
let jobLineTests =
    testList "JobLine.withoutCursorControl" [
        test "should drop cursor movement and visibility codes when Expecto draws its log line" {
            let line = "\u001b[1000D\u001b[?25h\u001b[37m[18:54:53 \u001b[31mERR\u001b[37m] failed\u001b[0m\u001b[K"

            let result = JobLine.withoutCursorControl line

            Expect.equal result "\u001b[37m[18:54:53 \u001b[31mERR\u001b[37m] failed\u001b[0m" "Only colour codes stay, so the job name before the line stays visible"
        }

        test "should keep the line when it holds no escape code" {
            let result = JobLine.withoutCursorControl "tests: ok"

            Expect.equal result "tests: ok" "A plain line is printed as it is"
        }
    ]
