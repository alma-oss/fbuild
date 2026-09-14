namespace Alma.Build

module DotnetTools =
    open Utils

    type DotnetTool = { Name: string; Version: string; Commands: string list }

    let private commonTools = [
        {
            Name = "paket"
            Version = "10.3.1"
            Commands = [ "paket" ]
        }
        {
            Name = "dotnet-fsharplint"
            Version = "0.26.10"
            Commands = [ "dotnet-fsharplint" ]
        }
    ]

    let private safeStackTools = [
        {
            Name = "fable"
            Version = "5.13.0"
            Commands = [ "fable" ]
        }
        {
            Name = "femto"
            Version = "0.21.0"
            Commands = [ "femto" ]
        }
    ]

    let tools =
        function
        | SAFEStackApplication _ -> commonTools @ safeStackTools
        | Library _
        | Executable _
        | ConsoleApplication _ -> commonTools

[<RequireQualifiedAccess>]
module ToolsManifest =
    open System.Collections.Generic
    open System.Text.Json
    open Fake.IO.FileSystemOperators
    open DotnetTools

    let path = ".config" </> "dotnet-tools.json"

    type ToolDto = { Version: string; Commands: string list }

    type ManifestDto = {
        Version: int
        IsRoot: bool
        Tools: Dictionary<string, ToolDto>
    }

    let private options =
        JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true)

    let render (tools: DotnetTool list) =
        let manifest = {
            Version = 1
            IsRoot = true
            Tools =
                tools
                |> List.map (fun tool -> KeyValuePair (tool.Name, { Version = tool.Version; Commands = tool.Commands }))
                |> Dictionary
        }

        JsonSerializer.Serialize (manifest, options) |> sprintf "%s\n"
