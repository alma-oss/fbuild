module Alma.Build.Tests.UtilsTests

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
let runtimeIdTests =
    testList "RuntimeId.value" [
        test "should map to the linux RID when given Linux" {
            Expect.equal (RuntimeId.value Linux) "linux-x64" "Linux maps to linux-x64"
        }

        test "should map to the osx RID when given OSX" {
            Expect.equal (RuntimeId.value OSX) "osx-x64" "OSX maps to osx-x64"
        }

        test "should pass the identifier through when given Other" {
            Expect.equal (RuntimeId.value (Other "linux-bionic-arm64")) "linux-bionic-arm64" "Other returns its own identifier"
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
