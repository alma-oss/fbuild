module Alma.Build.Tests.UtilsTests

open System.Runtime.InteropServices
open System.Xml.Linq
open Expecto
open Alma.Build.Utils

[<Tests>]
let solutionPickTests =
    testList "Solution.pick" [
        test "should pick the .slnx when it is the only solution" {
            let result = Solution.pick [ "build.fsproj"; "a.slnx" ]

            Expect.equal result (Ok (Some "a.slnx")) "a lone .slnx is picked"
        }

        test "should pick the .sln when it is the only solution" {
            let result = Solution.pick [ "build.fsproj"; "a.sln" ]

            Expect.equal result (Ok (Some "a.sln")) "a lone .sln is picked"
        }

        test "should return None when neither a .slnx nor a .sln is present" {
            let result = Solution.pick [ "readme.md"; "build.fsproj" ]

            Expect.equal result (Ok None) "candidates with no solution file yield None"
        }

        test "should fail listing every solution when a .slnx and a .sln are present" {
            let result = Solution.pick [ "a.sln"; "build.fsproj"; "b.slnx" ]

            Expect.equal
                result
                (Error (SolutionPickError.Ambiguous [ "a.sln"; "b.slnx" ]))
                "a .sln beside a .slnx leaves no single solution to build"
        }

        test "should fail listing every solution when two of the same extension are present" {
            let result = Solution.pick [ "a.slnx"; "b.slnx" ]

            Expect.equal
                result
                (Error (SolutionPickError.Ambiguous [ "a.slnx"; "b.slnx" ]))
                "two .slnx leave no single solution to build"
        }
    ]

[<Tests>]
let solutionFileNameCreateTests =
    testList "SolutionFileName.create" [
        test "should keep the name with inner dots when it holds nothing unsafe" {
            let result = "Alma.Build" |> SolutionFileName.create |> Result.map SolutionFileName.value

            Expect.equal result (Ok "Alma.Build.slnx") "a dotted name passes through"
        }

        test "should drop spaces, tabs and non-breaking spaces when the name holds them" {
            let result = "My\tGreat\u00A0Project App" |> SolutionFileName.create |> Result.map SolutionFileName.value

            Expect.equal result (Ok "MyGreatProjectApp.slnx") "every kind of whitespace is dropped"
        }

        test "should drop path separators and Windows-reserved characters when the name holds them" {
            let result = "a/b\\c:d*e?f\"g<h>i|j" |> SolutionFileName.create |> Result.map SolutionFileName.value

            Expect.equal result (Ok "abcdefghij.slnx") "path-unsafe characters are dropped"
        }

        test "should drop control characters when the name holds them" {
            let result = "Foo\u0001Bar" |> SolutionFileName.create |> Result.map SolutionFileName.value

            Expect.equal result (Ok "FooBar.slnx") "control characters are dropped"
        }

        test "should trim trailing dots when the name ends in them" {
            let result = "Foo. " |> SolutionFileName.create |> Result.map SolutionFileName.value

            Expect.equal result (Ok "Foo.slnx") "a trailing dot Windows would strip is trimmed"
        }

        test "should keep non-ASCII letters when the name holds them" {
            let result = "Hrabě" |> SolutionFileName.create |> Result.map SolutionFileName.value

            Expect.equal result (Ok "Hrabě.slnx") "diacritics are valid in file names"
        }

        test "should return NothingUsable error when nothing is left after dropping" {
            let result = "\t/. " |> SolutionFileName.create |> Result.map SolutionFileName.value

            Expect.equal result (Error (SolutionFileNameError.NothingUsable "\t/. ")) "an empty name would write a hidden .slnx"
        }
    ]

[<Tests>]
let solutionPartitionSupersededTests =
    testList "Solution.partitionSuperseded" [
        test "should mark a solution superseded when its contents are identical" {
            let rendered = "<Solution>\n  <Project Path=\"a\" />\n</Solution>\n"

            let result = Solution.partitionSuperseded rendered [ "Old Name.slnx", rendered ]

            Expect.equal result ([ "Old Name.slnx" ], []) "an identical solution is superseded"
        }

        test "should mark a solution superseded when only line endings and trailing whitespace differ" {
            let rendered = "<Solution>\n  <Project Path=\"a\" />\n</Solution>\n"

            let result = Solution.partitionSuperseded rendered [ "Old.slnx", "<Solution>\r\n  <Project Path=\"a\" />\r\n</Solution>" ]

            Expect.equal result ([ "Old.slnx" ], []) "CRLF and a missing final newline do not make a solution differ"
        }

        test "should keep a solution differing when its project list differs" {
            let rendered = "<Solution>\n  <Project Path=\"a\" />\n</Solution>\n"

            let result = Solution.partitionSuperseded rendered [ "Other.slnx", "<Solution>\n  <Project Path=\"b\" />\n</Solution>\n" ]

            Expect.equal result ([], [ "Other.slnx" ]) "a different project list is a different solution"
        }

        test "should split a mixed set when some match and some differ" {
            let rendered = "<Solution />\n"

            let result = Solution.partitionSuperseded rendered [ "a.slnx", rendered; "b.sln", "Microsoft Visual Studio Solution File" ]

            Expect.equal result ([ "a.slnx" ], [ "b.sln" ]) "each solution is classified on its own"
        }
    ]

[<Tests>]
let relativePathTests =
    testList "RelativePath.create" [
        test "should strip the root and use forward slashes when the path uses backslashes" {
            let result = RelativePath.create "/repo" "/repo\\src\\Foo.fsproj" |> RelativePath.value

            Expect.equal result "src/Foo.fsproj" "the Windows separator is normalized regardless of OS"
        }

        test "should strip the root and keep forward slashes when the path already uses them" {
            let result = RelativePath.create "/repo" "/repo/src/Foo.fsproj" |> RelativePath.value

            Expect.equal result "src/Foo.fsproj" "an already-forward-slashed path passes through unchanged"
        }

        test "should not add a leading slash for a root-level project" {
            let result = RelativePath.create "/repo" "/repo/build.fsproj" |> RelativePath.value

            Expect.equal result "build.fsproj" "a root-level project keeps no directory component"
        }

        test "should reach back with a parent segment when the path sits outside the root" {
            let result = RelativePath.create "/repo" "/shared/Core.fsproj" |> RelativePath.value

            Expect.equal result "../shared/Core.fsproj" "a project outside the root stays reachable from the solution"
        }

        test "should keep the root itself out of the path when the root carries a trailing slash" {
            let result = RelativePath.create "/repo/" "/repo/src/Foo.fsproj" |> RelativePath.value

            Expect.equal result "src/Foo.fsproj" "a trailing separator on the root adds no empty segment"
        }
    ]

/// A `<Project>` of the generated `<Solution>`, by whether the solution build includes it.
type private ListedProject =
    | Built of path: string
    | Unbuilt of path: string

/// One child of the generated `<Solution>`, in document order.
type private SolutionEntry =
    | Folder of name: string * projects: ListedProject list
    | RootProject of ListedProject

[<Tests>]
let solutionRenderTests =
    let built path = SolutionProject.Built (RelativePath.create "/repo" ("/repo/" + path))
    let unbuilt path = SolutionProject.Unbuilt (RelativePath.create "/repo" ("/repo/" + path))

    let attribute name (element: XElement) =
        match element.Attribute (XName.Get name) with
        | null -> failtestf "<%s> carries no %s attribute" element.Name.LocalName name
        | attribute -> attribute.Value

    let listedProject (element: XElement) =
        let path = element |> attribute "Path"

        match element.Elements (XName.Get "Build") |> Seq.map (attribute "Project") |> Seq.toList with
        | [] -> Built path
        | [ "false" ] -> Unbuilt path
        | other -> failtestf "<Project Path=%A> carries unexpected <Build> elements %A" path other

    let entriesOf = function
        | None -> failtest "render produced no content"
        | Some (content: string) ->
            XDocument.Parse(content).Root.Elements ()
            |> Seq.map (fun element ->
                match element.Name.LocalName with
                | "Folder" ->
                    Folder (
                        element |> attribute "Name",
                        element.Elements (XName.Get "Project") |> Seq.map listedProject |> Seq.toList
                    )
                | "Project" -> RootProject (element |> listedProject)
                | other -> failtestf "<%s> is not an element the solution format allows here" other
            )
            |> Seq.toList

    testList "Solution.render" [
        test "should return None when there are no projects" {
            let result = Solution.render []

            Expect.equal result None "an empty project set renders no file"
        }

        test "should return None when no project is built" {
            let result = Solution.render [ unbuilt "build/build.fsproj" ]

            Expect.equal result None "a solution of nothing but unbuilt projects is not written"
        }

        test "should group projects under a Folder per top-level directory" {
            let result = Solution.render [ built "src/Alma.Build/Alma.Build.fsproj" ] |> entriesOf

            Expect.equal
                result
                [ Folder ("/src/", [ Built "src/Alma.Build/Alma.Build.fsproj" ]) ]
                "a single project is wrapped in its top-level directory's Folder"
        }

        test "should sort folders by name and projects within a folder by path" {
            let result =
                Solution.render [
                    built "tests/unit/unit.fsproj"
                    built "src/Alma.Build/Alma.Build.fsproj"
                    built "tests/integration/integration.fsproj"
                ]
                |> entriesOf

            Expect.equal
                result
                [
                    Folder ("/src/", [ Built "src/Alma.Build/Alma.Build.fsproj" ])
                    Folder ("/tests/", [ Built "tests/integration/integration.fsproj"; Built "tests/unit/unit.fsproj" ])
                ]
                "src sorts before tests, and integration sorts before unit within tests"
        }

        test "should list a root-level project unwrapped after every folder" {
            let result = Solution.render [ built "build.fsproj"; built "src/Alma.Build/Alma.Build.fsproj" ] |> entriesOf

            Expect.equal
                result
                [
                    Folder ("/src/", [ Built "src/Alma.Build/Alma.Build.fsproj" ])
                    RootProject (Built "build.fsproj")
                ]
                "a project with no directory component is unwrapped, after the src Folder"
        }

        test "should emit no Folder when every project sits at the root" {
            let result = Solution.render [ built "test.rootlibrary.fsproj"; built "build.fsproj" ] |> entriesOf

            Expect.equal
                result
                [ RootProject (Built "build.fsproj"); RootProject (Built "test.rootlibrary.fsproj") ]
                "root-level projects are listed on their own, sorted by path"
        }

        test "should collapse duplicate paths from overlapping globs" {
            let result =
                Solution.render [ built "src/Alma.Build/Alma.Build.fsproj"; built "src/Alma.Build/Alma.Build.fsproj" ]
                |> entriesOf

            Expect.equal
                result
                [ Folder ("/src/", [ Built "src/Alma.Build/Alma.Build.fsproj" ]) ]
                "the duplicate contributes only one Project entry"
        }

        test "should group an unbuilt project by its top-level directory when listed with built ones" {
            let result =
                Solution.render [ built "src/Alma.Build/Alma.Build.fsproj"; unbuilt "build/build.fsproj" ] |> entriesOf

            Expect.equal
                result
                [
                    Folder ("/build/", [ Unbuilt "build/build.fsproj" ])
                    Folder ("/src/", [ Built "src/Alma.Build/Alma.Build.fsproj" ])
                ]
                "the unbuilt project gets its own build Folder, sorted among the others"
        }

        test "should list a project once, built, when it is given as both built and unbuilt" {
            let result =
                Solution.render [ unbuilt "build/build.fsproj"; built "build/build.fsproj"; built "src/Alma.Build/Alma.Build.fsproj" ]
                |> entriesOf

            Expect.equal
                result
                [
                    Folder ("/build/", [ Built "build/build.fsproj" ])
                    Folder ("/src/", [ Built "src/Alma.Build/Alma.Build.fsproj" ])
                ]
                "a project declared as a source stays in the solution build"
        }

        test "should indent by two spaces and end with a newline" {
            let result = Solution.render [ built "build.fsproj"; built "src/Alma.Build/Alma.Build.fsproj" ]

            Expect.equal
                result
                (Some "<Solution>\n  <Folder Name=\"/src/\">\n    <Project Path=\"src/Alma.Build/Alma.Build.fsproj\" />\n  </Folder>\n  <Project Path=\"build.fsproj\" />\n</Solution>\n")
                "the generated file matches the formatting of a hand-written .slnx"
        }

        test "should nest Build Project=false under an unbuilt project when rendering" {
            let result = Solution.render [ built "src/Alma.Build/Alma.Build.fsproj"; unbuilt "build/build.fsproj" ]

            Expect.equal
                result
                (Some "<Solution>\n  <Folder Name=\"/build/\">\n    <Project Path=\"build/build.fsproj\">\n      <Build Project=\"false\" />\n    </Project>\n  </Folder>\n  <Folder Name=\"/src/\">\n    <Project Path=\"src/Alma.Build/Alma.Build.fsproj\" />\n  </Folder>\n</Solution>\n")
                "the slnx exclusion is the format dotnet build honours"
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

