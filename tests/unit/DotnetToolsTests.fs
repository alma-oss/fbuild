module Alma.Build.Tests.DotnetToolsTests

open System.Text.Json
open Expecto
open Alma.Build
open Alma.Build.Utils
// Last, so that a `Name` field resolves to `DotnetTool` rather than to `Utils.ProjectMeta`.
open Alma.Build.DotnetTools

[<Tests>]
let toolsTests =
    let names spec = DotnetTools.tools spec |> List.map (fun tool -> tool.Name)

    testList "DotnetTools.tools" [
        test "should require only the tools every target needs when the spec is a library" {
            Expect.equal
                (names Spec.defaultLibrary)
                [ "paket"; "dotnet-fsharplint" ]
                "A library needs Paket for the restore and fsharplint for Lint"
        }

        test "should require the same tools for an executable as for a library" {
            Expect.equal
                (names Spec.defaultExecutable)
                (names Spec.defaultLibrary)
                "No executable target shells out to a tool a library does not use"
        }

        test "should require the same tools for a console application as for a library" {
            Expect.equal
                (names (Spec.defaultConsoleApplication [ Linux ]))
                (names Spec.defaultLibrary)
                "No console target shells out to a tool a library does not use"
        }

        test "should add the SAFE-stack tools to the common ones when the spec is a SAFE-stack application" {
            Expect.equal
                (names (Spec.defaultSAFEStackApplication "5.0.3"))
                (names Spec.defaultLibrary @ [ "fable"; "femto" ])
                "A SAFE-stack repository needs fable for the build and femto for the npm side"
        }
    ]

[<Tests>]
let renderTests =
    let paket = { Name = "paket"; Version = "10.3.1"; Commands = [ "paket" ] }
    let lint = { Name = "dotnet-fsharplint"; Version = "0.26.10"; Commands = [ "dotnet-fsharplint" ] }

    let quoted (value: string) = $"\"{value}\""

    testList "ToolsManifest.render" [
        test "should render a rooted version 1 manifest when given the required tools" {
            let result = ToolsManifest.render [ paket ]

            Expect.stringContains result "\"version\": 1" "The manifest declares schema version 1"
            Expect.stringContains result "\"isRoot\": true" "The manifest is a root manifest"
        }

        test "should camelCase the manifest keys rather than emit the DTO field names" {
            let result = ToolsManifest.render [ paket ]

            Expect.isFalse (result.Contains "\"IsRoot\"") "A PascalCase DTO field name is a key the dotnet CLI ignores"
        }

        test "should key a tool by its own name when the name is not a valid property name" {
            let result = ToolsManifest.render [ lint ]

            Expect.stringContains result (quoted lint.Name) "A tool name is a dictionary key, not a property"
        }

        test "should render the version and commands of every tool it is given" {
            let result = ToolsManifest.render [ paket; lint ]

            Expect.stringContains result (quoted paket.Version) "The version of the first tool is written"
            Expect.stringContains result (quoted lint.Version) "The version of the second tool is written"
            Expect.stringContains result (quoted (List.head paket.Commands)) "A tool carries the commands it installs"
        }

        test "should render tools in the order they are given when more than one is required" {
            let result = ToolsManifest.render [ paket; lint ]

            Expect.isLessThan
                (result.IndexOf paket.Name)
                (result.IndexOf lint.Name)
                "The manifest keeps the given order rather than sorting the keys"
        }

        test "should end with a newline so the file is a well-formed text file" {
            Expect.stringEnds (ToolsManifest.render [ paket ]) "\n" "A generated file ends with a newline"
        }

        test "should render a manifest the dotnet CLI can read back when the tools are the pinned set" {
            let result = ToolsManifest.render [ paket; lint ]

            use manifest = JsonDocument.Parse result
            let entry = manifest.RootElement.GetProperty("tools").GetProperty paket.Name

            Expect.equal
                ((entry.GetProperty "version").GetString ())
                paket.Version
                "The round-tripped manifest holds the version it was given"

            let commands =
                (entry.GetProperty "commands").EnumerateArray ()
                |> Seq.map (fun command -> command.GetString ())
                |> List.ofSeq

            Expect.equal commands paket.Commands "The round-tripped manifest holds the commands it was given"
        }
    ]
