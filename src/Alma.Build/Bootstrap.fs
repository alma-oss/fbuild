namespace Alma.Build

module Bootstrap =
    open System

    open Fake.Core
    open Fake.IO

    open Utils

    [<RequireQualifiedAccess>]
    module internal BundledResource =
        let private assembly = Reflection.Assembly.GetExecutingAssembly ()

        /// Every resource embedded under `prefix`, as its resource name paired with its path
        /// relative to the prefix. Logical names may use `\`, so they are normalized before matching.
        let resourcesUnder (prefix: string) =
            assembly.GetManifestResourceNames ()
            |> Seq.map (fun name -> name, name.Replace ('\\', '/'))
            |> Seq.filter (snd >> String.startsWith prefix)
            |> Seq.map (fun (name, normalized) -> name, normalized.Substring prefix.Length)

        /// Writes an embedded resource to `path`, creating the directory it goes in.
        let extractTo path name =
            match path |> Path.getDirectory with
            | "" -> ()
            | directory -> Directory.ensure directory

            use stream = assembly.GetManifestResourceStream (name: string)
            use file = IO.File.Create path

            stream.CopyTo file

    [<RequireQualifiedAccess>]
    type DeployedContents =
        | Bundled of resource: string
        | Rendered of contents: string

    [<RequireQualifiedAccess>]
    module internal DeployedContents =
        /// Writes the contents to `path`, creating the directory it goes in.
        let writeTo path = function
            | DeployedContents.Bundled resource -> resource |> BundledResource.extractTo path
            | DeployedContents.Rendered contents ->
                match path |> Path.getDirectory with
                | "" -> ()
                | directory -> Directory.ensure directory

                IO.File.WriteAllText (path, contents)

    type DeployedFile = {
        Path: string
        Contents: ProjectSpec -> DeployedContents option
    }

    [<RequireQualifiedAccess>]
    module internal RuntimeProps =
        let path = "Directory.Build.props"

        let resource = "runtime/Directory.Build.props"

        let neededBy (specs: ProjectSpec) =
            specs.RuntimeConfiguration
            |> Option.exists (fun configuration ->
                configuration.RuntimeMode |> RuntimeMode.runtimeIdentifier |> Option.isSome)

        let argument runtimeIdentifier =
            runtimeIdentifier |> RuntimeIdentifier.value |> sprintf "-p:AlmaBuildRuntimeIdentifier=%s"

    let internal deployedFiles: DeployedFile list =
        [
            {
                Path = ToolsManifest.path
                Contents =
                    fun specs ->
                        specs
                        |> DotnetTools.tools
                        |> ToolsManifest.render
                        |> DeployedContents.Rendered
                        |> Some
            }

            {
                Path = RuntimeProps.path
                Contents =
                    fun specs ->
                        if specs |> RuntimeProps.neededBy
                        then Some (DeployedContents.Bundled RuntimeProps.resource)
                        else None
            }
        ]

    /// Deploys the support files bundled in the engine assembly, overwriting local copies —
    /// how a consumer first gets `build.sh` and how a version bump refreshes it.
    let internal deploy specs =
        BundledResource.resourcesUnder "bootstrap/"
        |> Seq.iter (fun (resource, relative) ->
            resource |> BundledResource.extractTo relative

            if relative.EndsWith ".sh" && not (OperatingSystem.IsWindows ()) then
                IO.File.SetUnixFileMode (
                    relative,
                    IO.UnixFileMode.UserRead ||| IO.UnixFileMode.UserWrite ||| IO.UnixFileMode.UserExecute
                    ||| IO.UnixFileMode.GroupRead ||| IO.UnixFileMode.GroupExecute
                    ||| IO.UnixFileMode.OtherRead ||| IO.UnixFileMode.OtherExecute
                )

            Trace.tracefn " -> %s" relative
        )

        deployedFiles
        |> List.iter (fun deployedFile ->
            match deployedFile.Contents specs with
            | None -> ()
            | Some contents ->
                contents |> DeployedContents.writeTo deployedFile.Path
                Trace.tracefn " -> %s" deployedFile.Path
        )

        if Solution.all () |> List.isEmpty then
            Trace.tracefn "Run the Solution target to generate a solution file for this project."
