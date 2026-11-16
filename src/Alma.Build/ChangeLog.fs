namespace Alma.Build

module ChangeLog =
    open System
    open System.IO

    open Fake.Core

    open Utils

    type internal TagName = TagName of string

    [<RequireQualifiedAccess>]
    module internal TagName =
        let value (TagName tag) = tag

        /// The tag a CI run builds, from `GITHUB_REF_TYPE` and `GITHUB_REF_NAME`; `None` outside a tag run.
        let ofEnvironment (refType: string option) (refName: string option) =
            match refType with
            | Some "tag" -> Some (TagName (refName |> Option.defaultValue ""))
            | _ -> None

    [<RequireQualifiedAccess>]
    type internal ChangeLogError =
        | Unreadable of path: string * reason: string
        | NoReleaseSection of path: string
        | UnparsableHeading of path: string * heading: string * reason: string
        | TagMismatch of path: string * tag: TagName * version: string
        | ProjectVersionMismatch of project: string * tag: TagName * version: string

    [<RequireQualifiedAccess>]
    module internal ChangeLogError =
        let format = function
            | ChangeLogError.Unreadable (path, reason) -> sprintf "Change log %s cannot be read: %s" path reason
            | ChangeLogError.NoReleaseSection path ->
                sprintf "Change log %s holds no release section; add a \"## <version>\" section below \"## Unreleased\" to release." path
            | ChangeLogError.UnparsableHeading (path, heading, reason) ->
                sprintf "The latest release heading %A in change log %s does not parse as a release: %s" heading path reason
            | ChangeLogError.TagMismatch (path, tag, version) ->
                sprintf "Tag %A does not match the latest release %s in change log %s." (TagName.value tag) version path
            | ChangeLogError.ProjectVersionMismatch (project, tag, version) ->
                sprintf "Tag %A does not match Version %A of project %s; set <Version> in the project to the tag's version." (TagName.value tag) version project

    [<RequireQualifiedAccess>]
    module internal ChangeLogRelease =
        let private isSectionHeading (line: string) = line.StartsWith "## "

        let private parseRelease path heading =
            try ReleaseNotes.parse [ heading ] |> Ok
            with e -> Error (ChangeLogError.UnparsableHeading (path, heading, e.Message))

        let private strictVersion =
            Text.RegularExpressions.Regex @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?\z"

        let private semVer version =
            try SemVer.parse version |> Some
            with _ -> None

        let private tagVersion tag =
            match TagName.value tag with
            | tag when strictVersion.IsMatch tag -> semVer tag
            | _ -> None

        /// The latest release in the change-log `lines` of `path`, with the raw body under its heading;
        /// text above the heading is ignored. With a `tag`, the release must be the tag's version.
        let parse (tag: TagName option) (path: string) (lines: string list): Result<ReleaseNotes.ReleaseNotes * string list, ChangeLogError> =
            match lines |> List.skipWhile (fun line -> not (isSectionHeading line) || line = "## Unreleased") with
            | [] -> Error (ChangeLogError.NoReleaseSection path)
            | heading :: rest ->
                let body = rest |> List.takeWhile (isSectionHeading >> not)

                parseRelease path heading
                |> Result.bind (fun release ->
                    match tag with
                    | Some tag when tagVersion tag <> Some release.SemVer -> Error (ChangeLogError.TagMismatch (path, tag, release.NugetVersion))
                    | _ -> Ok (release, body)
                )

        let private readLines path =
            try File.ReadAllLines path |> List.ofArray |> Ok
            with e -> Error (ChangeLogError.Unreadable (path, e.Message))

        let read (tag: TagName option) (path: string) =
            readLines path |> Result.bind (parse tag path)

        /// Checks the evaluated MSBuild `Version` of `project` against the tag, with the tag check's SemVer equality.
        let checkProjectVersion (tag: TagName) (project: string) (version: string): Result<unit, ChangeLogError> =
            match tagVersion tag, semVer version with
            | Some tagVersion, Some version when tagVersion = version -> Ok ()
            | _ -> Error (ChangeLogError.ProjectVersionMismatch (project, tag, version))

        /// The release's assembly version, or `1.0` when the change log holds no usable release;
        /// an unreadable change log stays an error.
        let assemblyVersion = function
            | Ok (release: ReleaseNotes.ReleaseNotes, _) -> Ok release.AssemblyVersion
            | Error (ChangeLogError.Unreadable _ as error) -> Error error
            | Error _ -> Ok "1.0"

    [<RequireQualifiedAccess>]
    type internal ReleaseNotesError =
        | NoChangeLog
        | ChangeLog of ChangeLogError
        | EmptyBody of path: string

    [<RequireQualifiedAccess>]
    module internal ReleaseNotesError =
        let format = function
            | ReleaseNotesError.NoChangeLog -> "Release notes need a change log, but the spec names no change log."
            | ReleaseNotesError.ChangeLog error -> sprintf "No release notes: %s" (ChangeLogError.format error)
            | ReleaseNotesError.EmptyBody path -> sprintf "The latest release section of change log %s has an empty body." path

    [<RequireQualifiedAccess>]
    module internal ReleaseNotesFile =
        let path = "release/RELEASE_NOTES.md"

        let private trimBlankLines =
            let dropLeading = List.skipWhile String.IsNullOrWhiteSpace
            dropLeading >> List.rev >> dropLeading >> List.rev

        /// The latest release body with blank edge lines dropped, joined with `\n` and ending in a newline.
        let content (changeLogPath: string) (release: Result<ReleaseNotes.ReleaseNotes * string list, ChangeLogError>): Result<string, ReleaseNotesError> =
            release
            |> Result.mapError ReleaseNotesError.ChangeLog
            |> Result.bind (fun (_, body) ->
                match trimBlankLines body with
                | [] -> Error (ReleaseNotesError.EmptyBody changeLogPath)
                | lines -> Ok (String.concat "\n" lines + "\n")
            )

    /// The change log to check for a spec's `release` or `publish`, given the run's tag; `None` when none is checked.
    [<RequireQualifiedAccess>]
    module internal ReleaseGuard =
        let release (tag: TagName option) = function
            | Library library -> Some library.Changelog
            | ConsoleApplication _ as spec -> spec.ChangeLog
            | Executable _ as spec when tag.IsSome -> spec.ChangeLog
            | Executable _ | SAFEStackApplication _ -> None

        let publish = function
            | Library { NugetApi = NugetApi.NotUsed } -> None
            | Library library -> Some library.Changelog
            | Executable _ | ConsoleApplication _ | SAFEStackApplication _ -> None
