module Alma.Build.Tests.UtilsTests

open System.Runtime.InteropServices
open Expecto
open Alma.Build.Utils

[<Tests>]
let solutionPickTests =
    testList "Solution.pick" [
        test "should prefer the .slnx when both a .slnx and a .sln are present" {
            let result = Solution.pick [ "a.sln"; "b.slnx" ]

            Expect.equal result (Some "b.slnx") ".slnx wins over .sln"
        }

        test "should fall back to the .sln when no .slnx is present" {
            let result = Solution.pick [ "a.sln" ]

            Expect.equal result (Some "a.sln") "a lone .sln is picked"
        }

        test "should return None when neither a .slnx nor a .sln is present" {
            let result = Solution.pick [ "readme.md"; "build.fsproj" ]

            Expect.equal result None "candidates with no solution file yield None"
        }
    ]

[<Tests>]
let stringToOptionTests =
    testList "stringToOption" [
        test "should return None when the string is empty" {
            let result = stringToOption ""

            Expect.equal result None "an empty string maps to None"
        }

        test "should return None when the string is null" {
            let result = stringToOption null

            Expect.equal result None "a null string maps to None"
        }

        test "should return Some when the string is non-empty" {
            let result = stringToOption "value"

            Expect.equal result (Some "value") "a non-empty string is preserved"
        }
    ]

[<Tests>]
let envVarTests =
    testList "envVar" [
        test "should return the value when the variable is set to a non-empty string" {
            System.Environment.SetEnvironmentVariable ("FBUILD_TEST_SET", "hello")

            let result = envVar "FBUILD_TEST_SET"

            Expect.equal result (Some "hello") "a set variable returns its value"
        }

        test "should return None when the variable is not set" {
            System.Environment.SetEnvironmentVariable ("FBUILD_TEST_UNSET", null)

            let result = envVar "FBUILD_TEST_UNSET"

            Expect.equal result None "an unset variable returns None"
        }
    ]

[<Tests>]
let optionTests =
    testList "Option" [
        test "should keep the existing value when bindNone is given a Some" {
            let result = Some 1 |> Option.bindNone (fun _ -> Some 9)

            Expect.equal result (Some 1) "bindNone leaves a Some untouched"
        }

        test "should run the fallback when bindNone is given a None" {
            let result = None |> Option.bindNone (fun _ -> Some 9)

            Expect.equal result (Some 9) "bindNone replaces a None from the fallback"
        }

        test "should unwrap the value when requireSome is given a Some" {
            let result = Some 5 |> Option.requireSome "should not raise"

            Expect.equal result 5 "requireSome returns the wrapped value"
        }

        test "should raise when requireSome is given a None" {
            Expect.throws
                (fun () -> None |> Option.requireSome "boom" |> ignore)
                "requireSome raises on None"
        }
    ]

[<Tests>]
let runtimeIdentifierTests =
    testList "RuntimeIdentifier" [
        test "should preserve the RID string when an identifier is created" {
            let result = RuntimeIdentifier.create "linux-x64" |> RuntimeIdentifier.value

            Expect.equal result "linux-x64" "Created runtime identifier should preserve its RID string"
        }
    ]

[<Tests>]
let runtimeTargetTests =
    testList "RuntimeTarget.value" [
        test "should map to the linux RID when given Linux" {
            let result = RuntimeTarget.value Linux

            Expect.equal result "linux-x64" "Linux maps to linux-x64"
        }

        test "should map to the osx RID when given OSX" {
            let result = RuntimeTarget.value OSX

            Expect.equal result "osx-x64" "OSX maps to osx-x64"
        }

        test "should pass the identifier through when given Custom" {
            let result = RuntimeTarget.value (Custom "linux-bionic-arm64")

            Expect.equal result "linux-bionic-arm64" "Custom returns its own identifier"
        }
    ]

[<Tests>]
let runtimeTargetDetectTests =
    testList "RuntimeTarget.detect" [
        test "should resolve to a known case rather than Custom when running on a mapped OS/architecture" {
            let result = RuntimeTarget.detect ()

            let isCustom = match result with Custom _ -> true | _ -> false

            match RuntimeInformation.OSArchitecture with
            | Architecture.X64 | Architecture.Arm64 ->
                Expect.isFalse
                    isCustom
                    "A mapped x64/arm64 OS should resolve to a named RuntimeTarget case, not Custom \
                     (Custom on Linux would mean it fell back to the host's raw, possibly distro-specific RID)"
            | _ -> ()
        }

        test "should agree with the OS family regardless of the host distro's raw RID" {
            let result = RuntimeTarget.detect () |> RuntimeTarget.value

            let expectedFamily =
                if RuntimeInformation.IsOSPlatform OSPlatform.Windows then "win"
                elif RuntimeInformation.IsOSPlatform OSPlatform.OSX then "osx"
                else "linux"

            Expect.isTrue
                (result.StartsWith expectedFamily)
                $"Detected RID '%s{result}' should belong to the '%s{expectedFamily}' family; unmapped glibc \
                  distros (e.g. Arch Linux) must not leak their raw /etc/os-release ID (e.g. \"arch-x64\")"
        }
    ]

[<Tests>]
let runtimeModeTests =
    testList "RuntimeMode.runtimeIdentifier" [
        test "should return None when portable mode is selected" {
            let result = RuntimeMode.runtimeIdentifier RuntimeMode.Portable

            Expect.equal result None "Portable mode does not select a runtime ID"
        }

        test "should return the selected runtime ID when specific mode is selected" {
            let result = RuntimeMode.runtimeIdentifier (RuntimeMode.Specific OSXArm64)

            Expect.equal result (Some (RuntimeIdentifier.create "osx-arm64")) "Specific mode resolves the selected runtime target"
        }

        test "should return the runtime-provided RID when auto-detect mode is selected" {
            let result = RuntimeMode.runtimeIdentifier RuntimeMode.AutoDetect

            Expect.equal
                result
                (Some ((RuntimeTarget.detect () |> RuntimeTarget.toRuntimeIdentifier)))
                "Auto-detect mode should preserve the runtime-provided RID"
        }
    ]

[<Tests>]
let runtimeConfigurationTests =
    testList "RuntimeConfiguration" [
        test "should resolve the detected runtime when it is a release target" {
            let configuration: RuntimeConfiguration = {
                RuntimeTargets = [ Custom (RuntimeIdentifier.value ((RuntimeTarget.detect () |> RuntimeTarget.toRuntimeIdentifier))) ]
                RuntimeMode = RuntimeMode.AutoDetect
            }

            let result = RuntimeConfiguration.resolve configuration

            Expect.equal result (Ok (Some ((RuntimeTarget.detect () |> RuntimeTarget.toRuntimeIdentifier)))) "Detected RID listed as a release target should be accepted"
        }

        test "should return UnsupportedRuntime error when the detected runtime is not a release target" {
            let configuration: RuntimeConfiguration = {
                RuntimeTargets = [ Custom "totally-fake-rid" ]
                RuntimeMode = RuntimeMode.AutoDetect
            }

            let result = RuntimeConfiguration.resolve configuration

            Expect.equal
                result
                (Error (RuntimeConfigurationError.UnsupportedRuntime ((RuntimeTarget.detect () |> RuntimeTarget.toRuntimeIdentifier))))
                "Detected RID absent from release targets should be rejected"
        }

        test "should accept equivalent runtime IDs when their canonical values match" {
            let configuration: RuntimeConfiguration = {
                RuntimeTargets = [ Linux ]
                RuntimeMode = RuntimeMode.Specific (Custom "linux-x64")
            }

            let result = RuntimeConfiguration.resolve configuration

            Expect.equal
                result
                (Ok (Some (RuntimeIdentifier.create "linux-x64")))
                "Equivalent RID values should be supported"
        }

        test "should return UnsupportedRuntime error when the selected runtime is unavailable" {
            let configuration: RuntimeConfiguration = {
                RuntimeTargets = [ Windows ]
                RuntimeMode = RuntimeMode.Specific Linux
            }

            let result = RuntimeConfiguration.resolve configuration

            Expect.equal
                result
                (Error (RuntimeConfigurationError.UnsupportedRuntime (RuntimeIdentifier.create "linux-x64")))
                "Unavailable RID should be rejected"
        }

        test "should expose runtime configuration when the project spec supports runtimes" {
            let result = Spec.defaultConsoleApplication [ Linux ] |> fun spec -> spec.RuntimeConfiguration

            Expect.isSome result "Console application should expose its runtime configuration"
        }

        test "should expose no runtime configuration when the project spec does not support runtimes" {
            let result = Spec.defaultLibrary.RuntimeConfiguration

            Expect.isNone result "Library should not expose runtime configuration"
        }
    ]

[<Tests>]
let teeTests =
    testList "tee" [
        test "should apply the effect and return the value unchanged when threaded through a pipeline" {
            let mutable seen = 0

            let result = 42 |> tee (fun x -> seen <- x)

            Expect.equal result 42 "tee returns its input unchanged"
            Expect.equal seen 42 "tee runs the side effect on its input"
        }
    ]
