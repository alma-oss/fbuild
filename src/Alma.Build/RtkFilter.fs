namespace Alma.Build

/// Buffered output filters for RTK-wrapped build commands.
module internal RtkFilter =
    open System.IO
    open System.Text.Json
    open System.Text.RegularExpressions

    type Stream =
        | Stdout
        | Stderr

    /// One line a process wrote, newline stripped.
    type Line = { Stream: Stream; Text: string }

    [<RequireQualifiedAccess>]
    module Line =
        let text (line: Line) = line.Text

        let stdout (text: string) = { Stream = Stdout; Text = text }

        let mapText (f: string -> string) (line: Line) = { line with Text = f line.Text }

    type Filter = { Run: Line list -> int -> Line list }

    [<RequireQualifiedAccess>]
    module Filter =
        let create (run: Line list -> int -> Line list): Filter = { Run = run }

        let ofLines (f: Line list -> Line list): Filter = create (fun lines _ -> f lines)

        let passthrough: Filter = ofLines id

        /// When `inner` keeps nothing, a clean exit collapses to `message`; a failing one shows raw output.
        let okWhenEmpty (message: string) (inner: Filter): Filter =
            create (fun lines exitCode ->
                match inner.Run lines exitCode with
                | [] -> if exitCode = 0 then [ Line.stdout message ] else lines
                | kept -> kept
            )

        /// NuGet audit/prune warnings; NU *errors* are not noise.
        let private isNugetNoise (line: string) =
            Regex.IsMatch (line, @"NU\d{4}") && not (line.Contains "error")

        /// `dotnet pack` reports packaging problems as `NU5xxx`, which are signal; the audit range
        /// (`NU19xx`) and the restore-phase prune warning (`NU1510`) are noise there as anywhere.
        let private isPackNoise (line: string) =
            Regex.IsMatch (line, @"NU(19\d{2}|1510)") && not (line.Contains "error")

        let private isDiagnostic (line: string) =
            Regex.IsMatch (line, @"(warning|error) [A-Z]+\d+")

        let private isError (line: string) = line.Contains "error"

        /// MSBuild prints absolute paths both as the diagnostic location and in the
        /// trailing `[…fsproj]`; strip the repo-root prefix from each occurrence.
        let private relativize (repoRoot: string) (line: string) =
            line.Replace (
                repoRoot.TrimEnd (Path.DirectorySeparatorChar)
                + string Path.DirectorySeparatorChar,
                ""
            )

        /// MSBuild emits its diagnostics amid restore/progress chatter, repeating each once per
        /// restore/build phase; keep the deduped ones the `isNoise` predicate doesn't drop, with
        /// locations relative to `repoRoot`. `label` names the command in the all-clear line. A
        /// failing run whose error the whitelist didn't keep falls back to the raw output, so a
        /// code-less `error :` is never summarised away.
        let private diagnostics (isNoise: string -> bool) (label: string) (repoRoot: string): Filter =
            let keep =
                List.filter (fun (line: Line) -> isDiagnostic line.Text && not (isNoise line.Text))
                >> List.map (Line.mapText (relativize repoRoot))
                >> List.distinctBy Line.text

            create (fun lines exitCode ->
                let unwhitelistedError =
                    exitCode <> 0
                    && lines
                       |> List.exists (fun { Text = text } -> isError text && not (isDiagnostic text) && not (isNoise text))

                if unwhitelistedError then lines else keep lines
            )
            |> okWhenEmpty (sprintf "%s: ok" label)

        let dotnetBuild: string -> Filter = diagnostics isNugetNoise "dotnet build"

        let dotnetPublish: string -> Filter = diagnostics isNugetNoise "dotnet publish"

        let dotnetRestore: string -> Filter = diagnostics isNugetNoise "dotnet restore"

        let dotnetPack: string -> Filter = diagnostics isPackNoise "dotnet pack"

        /// Parses `dotnet fsharplint lint` output into one compact diagnostic per warning.
        /// The tool emits one block per linted file, warnings within a block separated by a
        /// `----` rule and the whole run closed by a `Summary:` banner:
        ///
        ///     ========== Linting /abs/repo/tests/Tests.fs ==========
        ///     ========== Finished: 0 warnings ==========
        ///     ========== Linting /abs/repo/tests/HealthCheckUrl.fs ==========
        ///     Found trailing whitespace at end of line.
        ///     Error on line 19 starting at column 33
        ///             Instance.createFromValues
        ///                                      ^
        ///     See https://fsprojects.github.io/FSharpLint/how-tos/rules/FL0061.html
        ///     ----------------------------------------------------------------
        ///     ========== Finished: 1 warnings ==========
        ///     ========== Summary: 1 warnings ==========
        ///
        /// giving `  tests/HealthCheckUrl.fs:19:33 FL0061  Found trailing whitespace at end of line.`
        module private Fsharplint =
            let private lintingBanner = Regex @"^========== Linting (.+?) =========="
            let private errorLocation = Regex @"^Error on line (\d+) starting at column (\d+)"
            let private ruleUrl = Regex @"(FL\d+)\.html"
            let private summaryBanner = Regex @"^========== Summary: (\d+) warnings? =========="

            /// One line of the tool's output, newline already stripped.
            type OutputLine = OutputLine of string

            [<RequireQualifiedAccess>]
            module OutputLine =
                let value (OutputLine line) = line

                /// The tool's separator is a full-width rule; demanding that width keeps a source
                /// line of its own dashes, echoed as a warning's context, from splitting the window.
                let private separatorWidth = 80

                /// Banner rules (`==========`) and warning separators (`----`) both close a window.
                let isBoundary (OutputLine line) =
                    line.StartsWith "=========="
                    || (line.Length >= separatorWidth && line |> Seq.forall ((=) '-'))

                let tryMatch (regex: Regex) (OutputLine line) =
                    let matched = regex.Match line
                    if matched.Success then Some matched else None

            type Warning = {
                /// Repo-root-relative; the tool itself prints absolute paths.
                File: string
                /// 1-based source line, as the tool prints it.
                Line: string
                /// 0-based source column — the offset the tool's caret sits under, one less than
                /// the column an editor shows.
                Column: string
                /// Rule code such as `FL0061`; absent when the tool printed no rule URL.
                Rule: string option
                Message: string
            }

            /// `  tests/HealthCheckUrl.fs:19:33 FL0061  Found trailing whitespace at end of line.`,
            /// the code left out entirely when the tool named no rule.
            let format (w: Warning) =
                let rule = w.Rule |> Option.map (sprintf " %s") |> Option.defaultValue ""

                sprintf "  %s:%s:%s%s  %s" w.File w.Line w.Column rule w.Message

            /// One `Linting` banner and every line below it up to the next one — so `Body` also
            /// holds the block's own `Finished:` banner, and the last section holds `Summary:`.
            type Section = { File: string; Body: OutputLine list }

            /// A maximal run of non-boundary lines within a section, tagged with that section's
            /// file — for a single warning, its message / location / source / caret / URL lines.
            type Window = { File: string; Lines: OutputLine list }

            [<RequireQualifiedAccess>]
            module Section =
                /// Splits on `Linting` banners, resolving each announced path against `repoRoot`.
                /// A banner closes the run of lines below it, so lines gathered after the last
                /// banner are seen first and belong to no file — dropped by taking `snd`.
                let listOf repoRoot (lines: OutputLine list): Section list =
                    (lines, ([], []))
                    ||> List.foldBack (fun line (body, sections) ->
                        match OutputLine.tryMatch lintingBanner line with
                        | Some banner ->
                            let file = Path.GetRelativePath (repoRoot, banner.Groups[1].Value)
                            [], { File = file; Body = body } :: sections
                        | None -> line :: body, sections
                    )
                    |> snd

                /// The boundary lines that separate windows are dropped along with the empty runs
                /// between adjacent ones, so a section for a clean file yields no windows at all.
                let windows (section: Section) =
                    (section.Body, [])
                    ||> List.foldBack (fun line windows ->
                        match OutputLine.isBoundary line, windows with
                        | true, _ -> [] :: windows
                        | false, current :: rest -> (line :: current) :: rest
                        | false, [] -> [ [ line ] ]
                    )
                    |> List.filter (not << List.isEmpty)
                    |> List.map (fun lines -> { File = section.File; Lines = lines })

            [<RequireQualifiedAccess>]
            module Window =
                let private firstGroupOf regex window =
                    window.Lines
                    |> List.tryPick (OutputLine.tryMatch regex)
                    |> Option.map (fun matched -> matched.Groups[1].Value)

                /// `None` unless the window carries an `Error on line …` line, which is what tells a
                /// warning apart from the tool's own chatter. `Message` is the window's first
                /// non-blank line; `Rule` is absent when the window printed no rule URL.
                let toWarning (window: Window): Warning option =
                    window.Lines
                    |> List.tryPick (OutputLine.tryMatch errorLocation)
                    |> Option.map (fun location ->
                        let message =
                            window.Lines
                            |> List.map (OutputLine.value >> _.Trim())
                            |> List.tryFind ((<>) "")
                            |> Option.defaultValue ""

                        {
                            File = window.File
                            Line = location.Groups[1].Value
                            Column = location.Groups[2].Value
                            Rule = window |> firstGroupOf ruleUrl
                            Message = message
                        }
                    )

            let parse repoRoot: string list -> Warning list =
                List.map OutputLine
                >> Section.listOf repoRoot
                >> List.collect Section.windows
                >> List.choose Window.toWarning

            /// The tally the tool prints for itself, to check a parse against. `None` when the
            /// run emitted no summary at all, which is itself a reason not to trust the parse.
            let reportedCount: string list -> int option =
                List.map OutputLine
                >> List.tryPick (OutputLine.tryMatch summaryBanner)
                >> Option.bind (fun matched ->
                    match System.Int32.TryParse matched.Groups[1].Value with
                    | true, count -> Some count
                    | false, _ -> None
                )

            /// `fsharplint: 1 warning`
            let header (warnings: Warning list) =
                let count = List.length warnings
                sprintf "fsharplint: %d %s" count (if count = 1 then "warning" else "warnings")

            /// `fsharplint: parsed 3 of 6 warnings — output format changed, showing it raw`
            let driftNotice (parsed: int) (reported: int) =
                sprintf "fsharplint: parsed %d of %d warnings — output format changed, showing it raw" parsed reported

            /// A run that printed warnings but no `Summary:` banner exited mid-output, so the parse
            /// can't be trusted to be complete; its raw lines are shown instead of an under-count.
            let noSummaryNotice =
                "fsharplint: no summary banner — output truncated, showing it raw"

        /// A parse that loses warnings would silently under-report, since this summary is all
        /// the caller sees; disagreeing with the tool's own tally — or a missing tally with
        /// warnings present — hands back its raw output.
        let private fsharplintSummary (repoRoot: string): Filter =
            ofLines (fun lines ->
                let texts = lines |> List.map Line.text

                match Fsharplint.parse repoRoot texts, Fsharplint.reportedCount texts with
                | warnings, Some reported when reported <> List.length warnings ->
                    Line.stdout (Fsharplint.driftNotice (List.length warnings) reported) :: lines
                | [], _ -> []
                | _, None -> Line.stdout Fsharplint.noSummaryNotice :: lines
                | warnings, Some _ ->
                    Fsharplint.header warnings :: List.map Fsharplint.format warnings |> List.map Line.stdout
            )

        let fsharplint (repoRoot: string): Filter =
            fsharplintSummary repoRoot |> okWhenEmpty "fsharplint: ok"

        let private ansiEscape = Regex @"\x1b\[[0-9;?]*[A-Za-z]"

        let private withoutAnsi (line: string) = ansiEscape.Replace (line, "")

        /// `dotnet fable` prints a banner, plugin loads, and per-file progress, closing a clean run
        /// with `Compiled N/M: <file>` and `Fable compilation finished in <ms>ms` — a few hundred
        /// lines with no diagnostics. Fable prints diagnostic paths relative to its cwd already, so
        /// there is nothing to relativize.
        module private Fable =
            let private compiledTally = Regex @"^Compiled (\d+)/(\d+):"
            let private finishedIn = Regex @"Fable compilation finished in (\d+)ms"

            let private lastMatch (regex: Regex) lines =
                lines
                |> List.choose (fun line ->
                    let matched = regex.Match line
                    if matched.Success then Some matched else None
                )
                |> List.tryLast

            /// `fable: compiled 198/198 in 13684ms`, or `None` when the run printed no closing tally.
            let summary lines =
                match lastMatch compiledTally lines, lastMatch finishedIn lines with
                | Some tally, Some finished ->
                    sprintf "fable: compiled %s/%s in %sms" tally.Groups[1].Value tally.Groups[2].Value finished.Groups[1].Value
                    |> Some
                | _ -> None

            let isDiagnostic (line: string) =
                Regex.IsMatch (line, @"\b(error|warning)\b", RegexOptions.IgnoreCase)

        /// A clean run collapses to its compile tally; a run with diagnostics keeps them alone. A failing
        /// run with none shows its raw output, so a failure this cannot name is never summarised away.
        let fable: Filter =
            create (fun lines exitCode ->
                let plain = lines |> List.map (Line.mapText withoutAnsi)

                match plain |> List.filter (Line.text >> Fable.isDiagnostic) |> List.distinctBy Line.text with
                | [] when exitCode <> 0 -> lines
                | [] -> [ plain |> List.map Line.text |> Fable.summary |> Option.defaultValue "fable: ok" |> Line.stdout ]
                | diagnostics -> diagnostics
            )

        /// Vitest started with `--reporter=json` writes its whole report as one line on stdout, which
        /// Fable's `--run` prefixes with its own colour codes:
        ///
        ///     {"numTotalTestSuites":3,…,"numFailedTests":1,…,"testResults":[{"assertionResults":[{
        ///       "ancestorTitles":["Shared","Doubling"],"title":"should double …","status":"failed",
        ///       "failureMessages":["Error: Doubling 21 must give 42.\n…\n    at Array.test (/abs/tests/Shared/Doubling.fs:9:13)\n…"]
        ///     }],"status":"failed","message":"","name":"/abs/tests/Shared/output/Shared.Tests.js"}]}
        module private Vitest =
            let private reportStart = "{\"numTotalTestSuites\""
            let private stackFrame = Regex @"^\s*at "

            /// A frame in the suite's own F# source, which source maps resolve the compiled JS to.
            /// Frames in `fable_modules` belong to the suite's dependencies.
            let private isOwnFrame (line: string) =
                Regex.IsMatch (line, @"\.fs:\d+:\d+\)?$") && not (line.Contains "fable_modules")

            let private shownFailures = 10

            type Failure = {
                /// `Shared > Doubling > should double …`, or the test file's path when the file itself
                /// failed, such as on an import error.
                Name: string
                /// The error message, then the frames in the suite's own F# source.
                Details: string list
            }

            type Report = {
                Passed: int
                Failed: int
                /// Skipped and todo tests.
                Skipped: int
                Total: int
                Failures: Failure list
            }

            let private details repoRoot (message: string) =
                message.Split '\n'
                |> Array.toList
                |> List.choose (fun line ->
                    match stackFrame.IsMatch line with
                    | true when isOwnFrame line -> Some (line.Trim ())
                    | true -> None
                    | false when System.String.IsNullOrWhiteSpace line -> None
                    | false -> Some (line.TrimEnd ())
                )
                |> List.map (relativize repoRoot)

            let private failuresOf repoRoot (file: JsonElement) =
                let text (name: string) (element: JsonElement) = element.GetProperty(name).GetString ()

                let failedTests =
                    file.GetProperty("assertionResults").EnumerateArray ()
                    |> Seq.filter (text "status" >> (=) "failed")
                    |> Seq.map (fun test -> {
                        Name =
                            Seq.append
                                (test.GetProperty("ancestorTitles").EnumerateArray () |> Seq.map _.GetString())
                                [ text "title" test ]
                            |> String.concat " > "
                        Details =
                            test.GetProperty("failureMessages").EnumerateArray ()
                            |> Seq.toList
                            |> List.collect (_.GetString() >> details repoRoot)
                    })
                    |> Seq.toList

                match failedTests, text "status" file with
                | [], "failed" -> [ { Name = relativize repoRoot (text "name" file); Details = details repoRoot (text "message" file) } ]
                | failedTests, _ -> failedTests

            let private parse repoRoot (json: string): Report =
                use document = JsonDocument.Parse json
                let root = document.RootElement
                let count (name: string) = root.GetProperty(name).GetInt32 ()

                {
                    Passed = count "numPassedTests"
                    Failed = count "numFailedTests"
                    Skipped = count "numPendingTests" + count "numTodoTests"
                    Total = count "numTotalTests"
                    Failures = root.GetProperty("testResults").EnumerateArray () |> Seq.toList |> List.collect (failuresOf repoRoot)
                }

            /// `None` when the run wrote no report, or one this cannot read.
            let tryReport repoRoot (lines: string list) =
                lines
                |> List.tryPick (fun line ->
                    match line.IndexOf reportStart with
                    | -1 -> None
                    | index -> Some (line.Substring index)
                )
                |> Option.bind (fun json ->
                    try Some (parse repoRoot json) with _ -> None
                )

            /// `vitest: 1 failed, 3 passed, 1 skipped (5)`
            let private header (report: Report) =
                let counts = [
                    if report.Failed > 0 then sprintf "%d failed" report.Failed
                    sprintf "%d passed" report.Passed
                    if report.Skipped > 0 then sprintf "%d skipped" report.Skipped
                ]

                sprintf "vitest: %s (%d)" (String.concat ", " counts) report.Total

            let private format (failure: Failure) =
                sprintf "  × %s" failure.Name :: List.map (sprintf "    %s") failure.Details

            let summary (report: Report) =
                let shown = report.Failures |> List.truncate shownFailures
                let hidden = report.Failures.Length - shown.Length

                [
                    header report
                    yield! shown |> List.collect format
                    if hidden > 0 then sprintf "  … %d more failures" hidden
                ]

        /// Collapses a Fable-compiled suite run under Vitest with `--reporter=json` to the compile
        /// tally, the report's counts, and each failing test with its message and its frames in the
        /// suite's own F# source, paths relative to `repoRoot`. Without a report that agrees with the
        /// exit code, a failing run is filtered as a failing compile and a clean one is shown raw.
        let vitest (repoRoot: string): Filter =
            create (fun lines exitCode ->
                let texts = lines |> List.map (Line.text >> withoutAnsi)

                match Vitest.tryReport repoRoot texts with
                | Some report when (exitCode = 0) = List.isEmpty report.Failures ->
                    Option.toList (Fable.summary texts) @ Vitest.summary report |> List.map Line.stdout
                | _ when exitCode = 0 -> lines
                | _ -> fable.Run lines exitCode
            )

        module private Expecto =
            let private runningBanner = "EXPECTO? "
            let private summaryMarker = "EXPECTO! "
            let private tag = " <Expecto>"
            let private frameworkFrame = Regex @"^\s*at (Expecto|Microsoft\.FSharp|System)\."

            /// `tests: 2 tests run in … – 1 passed, …`, or `None` for any line but Expecto's summary.
            let summary (line: Line) =
                match line.Text.IndexOf summaryMarker with
                | -1 -> None
                | index ->
                    Some { line with Text = sprintf "tests: %s" (line.Text.Substring(index + summaryMarker.Length).Replace(tag, "").Trim ()) }

            /// Everything but the start banner, blank lines, and the stack frames inside Expecto,
            /// FSharp.Core and the BCL — what is left names each failing test, its message, and the
            /// test's own frames, with paths relative to `repoRoot`.
            let failure (repoRoot: string) (lines: Line list) =
                lines
                |> List.choose (fun line ->
                    match summary line with
                    | Some summary -> Some summary
                    | None when
                        line.Text.Contains runningBanner
                        || frameworkFrame.IsMatch line.Text
                        || System.String.IsNullOrWhiteSpace line.Text
                        ->
                        None
                    | None -> Some (line |> Line.mapText (fun text -> (text.Replace (tag, "") |> relativize repoRoot).TrimEnd ()))
                )

        /// A clean test run collapses to Expecto's `EXPECTO! … tests run …` summary, or `tests: ok`
        /// from a runner that prints none. A failing one drops only lines known to be noise, so an
        /// unfamiliar runner's failure still shows in full.
        let tests (repoRoot: string): Filter =
            create (fun lines exitCode ->
                let lines = lines |> List.map (Line.mapText withoutAnsi)

                if exitCode <> 0 then
                    Expecto.failure repoRoot lines
                else
                    match lines |> List.choose Expecto.summary with
                    | [] -> [ Line.stdout "tests: ok" ]
                    | summaries -> summaries
            )
