module Alma.Build.Tests.ChangeLogTests

open System

open Expecto
open Alma.Build.ChangeLog

let private sampleChangeLog = [
    "<!-- Keep a changelog -->"
    "# Changelog"
    ""
    "## Unreleased"
    "### Added"
    "- Pending change"
    ""
    "## 1.2.3 - 2026-10-14"
    ""
    "### Fixed"
    "- Fix `something` in [docs](https://example.com)"
    "  - Nested detail"
    ""
    "### Added"
    "- Add something"
    ""
    ""
    "## 1.2.2 - 2026-10-01"
    "- Older change"
]

let private onlyUnreleased = [ "# Changelog"; "## Unreleased"; "- Pending change" ]

[<Tests>]
let changeLogReleaseTests =
    let parse tag lines = ChangeLogRelease.parse (tag |> Option.map TagName) "CHANGELOG.md" lines

    let version tag lines = parse tag lines |> Result.map (fun (release, _) -> release.NugetVersion)

    testList "ChangeLogRelease" [
        test "should take the topmost release below Unreleased, ignoring text above it and subsection headers" {
            let result = version None sampleChangeLog

            Expect.equal result (Ok "1.2.3") "Version should come from the topmost release"
        }

        test "should take the raw body up to the next section" {
            let result = parse None [ "## 1.2.3"; "### Fix"; "- Fix something"; ""; "## 1.2.2"; "- Add something" ] |> Result.map snd

            Expect.equal result (Ok [ "### Fix"; "- Fix something"; "" ]) "The body runs from below the heading up to the next section"
        }

        test "should parse a release whose body holds indented headings" {
            let lines = [ "## 1.2.3"; "- Fix something"; "  ### Nested"; "  ## Indented"; "##" ]

            let result = version None lines

            Expect.equal result (Ok "1.2.3") "Only the heading line decides the release"
        }

        test "should return NoReleaseSection error when the change log holds no release heading" {
            [ []; onlyUnreleased; [ "# Changelog"; "Nothing released yet." ] ]
            |> List.iter (fun lines ->
                Expect.equal (version None lines) (Error (ChangeLogError.NoReleaseSection "CHANGELOG.md")) "Without a release heading there is no release"
                Expect.equal (version (Some "1.2.3") lines) (Error (ChangeLogError.NoReleaseSection "CHANGELOG.md")) "A tag run without a release fails the same way")
        }

        test "should return UnparsableHeading error naming the heading when the latest heading is no version" {
            let result = version None [ "## not-a-version"; "- Something" ]

            match result with
            | Error (ChangeLogError.UnparsableHeading ("CHANGELOG.md", heading, _)) -> Expect.equal heading "## not-a-version" "The error names the heading"
            | other -> failtestf "Expected an unparsable heading, got %A" other
        }

        test "should return Unreadable error naming the path when the file is missing" {
            let path = IO.Path.Combine (IO.Path.GetTempPath (), $"fbuild-changelog-{Guid.NewGuid ():N}.md")

            let result = ChangeLogRelease.read None path

            match result with
            | Error error -> Expect.stringContains (ChangeLogError.format error) path "The message names the path"
            | Ok _ -> failtest "A missing change log should fail"
        }

        test "should stamp the latest release version" {
            let result = ChangeLogRelease.parse None "CHANGELOG.md" sampleChangeLog |> ChangeLogRelease.assemblyVersion

            Expect.equal result (Ok "1.2.3") "AssemblyInfo stamps the topmost release"
        }

        test "should stamp 1.0 when the change log holds no usable release" {
            [ onlyUnreleased; [ "## not-a-version"; "- Something" ] ]
            |> List.iter (fun lines ->
                let result = ChangeLogRelease.parse None "CHANGELOG.md" lines |> ChangeLogRelease.assemblyVersion

                Expect.equal result (Ok "1.0") $"No usable release in {lines} stamps 1.0")
        }

        test "should fail AssemblyInfo with Unreadable error when the change log is missing" {
            let missing = Error (ChangeLogError.Unreadable ("CHANGELOG.md", "Could not find file"))

            let result = ChangeLogRelease.assemblyVersion missing

            Expect.equal result missing "A missing change log fails AssemblyInfo"
        }

        test "should pass a tag equal to the latest release" {
            [
                "1.2.3", "## 1.2.3 - 2026-10-14"
                "1.2.0", "## 1.2"
                "1.2.3-beta.1", "## 1.2.3-beta.1"
                "0.1.0", "## 0.1.0"
            ]
            |> List.iter (fun (tag, heading) -> Expect.isOk (version (Some tag) [ heading; "- Something" ]) $"Tag {tag} matches {heading}")
        }

        test "should return TagMismatch error naming tag, version and path when the tag differs" {
            [
                "1.3.0", "## 1.2.0", "1.2.0"
                "1.2.3", "## 1.2.3-beta.1", "1.2.3-beta.1"
                "1.2.3-beta.2", "## 1.2.3-beta.1", "1.2.3-beta.1"
                "v1.2.3", "## 1.2.3", "1.2.3"
                "release-1", "## 1.2.3", "1.2.3"
                "", "## 1.2.3", "1.2.3"
                "01.2.3", "## 1.2.3", "1.2.3"
                " 1.2.3", "## 1.2.3", "1.2.3"
                "1.2", "## 1.2", "1.2"
                "1.2.3+build.5", "## 1.2.3", "1.2.3"
            ]
            |> List.iter (fun (tag, heading, changeLogVersion) ->
                match version (Some tag) [ heading; "- Something" ] with
                | Error (ChangeLogError.TagMismatch _ as error) ->
                    [ $"\"{tag}\""; changeLogVersion; "CHANGELOG.md" ]
                    |> List.iter (fun part -> Expect.stringContains (ChangeLogError.format error) part $"Tag {tag}: the message names {part}")
                | other -> failtestf "Tag %s: expected a mismatch, got %A" tag other)
        }
    ]

[<Tests>]
let releaseNotesFileTests =
    let content tag lines = ChangeLogRelease.parse tag "CHANGELOG.md" lines |> ReleaseNotesFile.content "CHANGELOG.md"

    testList "ReleaseNotesFile" [
        test "should write only the latest release body, as it stands" {
            let result = content None sampleChangeLog

            Expect.equal
                result
                (Ok "### Fixed\n- Fix `something` in [docs](https://example.com)\n  - Nested detail\n\n### Added\n- Add something\n")
                "Only the 1.2.3 body is written, subsections, nesting, links and inline code unchanged, blank edges trimmed"
        }

        test "should keep trailing link definitions of the last section" {
            let result = content None [ "## 1.2.3"; "- Fix something"; ""; "[1.2.3]: https://example.com/compare/1.2.2...1.2.3" ]

            Expect.equal result (Ok "- Fix something\n\n[1.2.3]: https://example.com/compare/1.2.2...1.2.3\n") "Link definitions inside the section are kept"
        }

        test "should return EmptyBody error when the release body is blank" {
            let result = content None [ "## 1.2.3"; ""; "   "; "## 1.2.2"; "- Older change" ]

            Expect.equal result (Error (ReleaseNotesError.EmptyBody "CHANGELOG.md")) "A blank body is an error"
        }

        test "should return ChangeLog error when the change log yields no release" {
            let result = content None onlyUnreleased

            Expect.equal result (Error (ReleaseNotesError.ChangeLog (ChangeLogError.NoReleaseSection "CHANGELOG.md"))) "No release section is an error"
        }

        test "should return ChangeLog error when the tag differs from the latest release" {
            let result = content (Some (TagName "9.9.9")) sampleChangeLog

            Expect.equal
                result
                (Error (ReleaseNotesError.ChangeLog (ChangeLogError.TagMismatch ("CHANGELOG.md", TagName "9.9.9", "1.2.3"))))
                "A tag run writes no notes for another release"
        }

        test "should name the change log path, or say none is named, in every failure" {
            Expect.stringContains (ReleaseNotesError.format (ReleaseNotesError.EmptyBody "docs/CHANGELOG.md")) "docs/CHANGELOG.md" "The message names the path"
            Expect.stringContains (ReleaseNotesError.format (ReleaseNotesError.ChangeLog (ChangeLogError.NoReleaseSection "docs/CHANGELOG.md"))) "docs/CHANGELOG.md" "The message names the path"
            Expect.stringContains (ReleaseNotesError.format ReleaseNotesError.NoChangeLog) "no change log" "The message says the spec names none"
        }
    ]

[<Tests>]
let tagNameTests =
    testList "TagName" [
        test "should take a tag only from a tag ref type" {
            Expect.equal (TagName.ofEnvironment (Some "tag") (Some "v1.2.3")) (Some (TagName "v1.2.3")) "The tag name is kept as given"
            Expect.equal (TagName.ofEnvironment (Some "tag") None) (Some (TagName "")) "A tag run without a name has an empty tag"
            [ None; Some "branch"; Some "Tag" ]
            |> List.iter (fun refType -> Expect.isNone (TagName.ofEnvironment refType (Some "1.2.3")) "Only `tag` is a tag run")
        }
    ]
