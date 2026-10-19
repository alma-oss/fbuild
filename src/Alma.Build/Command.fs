namespace Alma.Build

/// Typed build commands and the RTK-aware runners that execute them: FAKE trace compaction,
/// the command vocabulary, RTK transport selection, serial and parallel runners, and the
/// temp-log tee that keeps a failing run's suppressed output reachable.
module internal Command =
    open System
    open System.IO
    open System.Linq

    open Fake.Core
    open Fake.IO
    open Fake.IO.FileSystemOperators

    let isRtkActive = Environment.hasEnvironVar "RTK_ACTIVE"

    /// Strips FAKE's planning/progress output when RTK is active; errors pass through.
    [<RequireQualifiedAccess>]
    module CompactTrace =
        let internal isNoise (message: string) =
            let m = message.TrimStart ()

            m.StartsWith "Shortened DependencyGraph"
            || m.StartsWith "The running order"
            || m.StartsWith "Group -"
            || m.Contains "<=="
            || m.StartsWith "Building project with version"
            // FAKE's internal command echoes (mono/git probes)
            || m.Contains "(In: "

        let internal isSeparator (message: string) =
            let m = message.Trim ()
            m.Length > 0 && (m |> Seq.forall ((=) '-') || m |> Seq.forall ((=) '='))

        // Bare git SHA echoed by `git rev-parse` at startup
        let internal isCommitHash (message: string) =
            let m = message.Trim ()
            m.Length = 40 && m |> Seq.forall Uri.IsHexDigit

        let install () =
            if isRtkActive then
                let inner = CoreTracing.defaultConsoleTraceListener
                // the Build Time Report is FAKE's final output, so mute from its header on
                let mutable inReport = false

                let compact =
                    { new ITraceListener with
                        member _.Write data =
                            match data with
                            | TraceData.LogMessage (m, _)
                            | TraceData.TraceMessage (m, _)
                            | TraceData.ImportantMessage m ->
                                if m.Contains "Build Time Report" then
                                    inReport <- true

                                if isNoise m || isSeparator m || isCommitHash m || inReport then
                                    ()
                                else
                                    inner.Write data
                            | TraceData.OpenTag (KnownTags.Target name, _) ->
                                Console.ForegroundColor <- ConsoleColor.Green
                                printfn "▸ %s" name
                                Console.ResetColor ()
                            | TraceData.CloseTag (KnownTags.Target _, _, TagStatus.Success) -> ()
                            | _ -> inner.Write data
                    }

                CoreTracing.setTraceListeners [ compact ]

    type DotnetCommand =
        | Build
        | Restore
        | Lint
        | Tests
        | Pack
        | Publish
        | Fable
        | FableTests
        | Run
        | WatchRun
        | FableWatch

    type NugetCommand =
        | Push
        | AddSource

    type NpmCommand =
        | Install
        | Version
        | Ls

    /// Arguments are supplied separately at the call site.
    type Command =
        | Dotnet of DotnetCommand
        | Nuget of NugetCommand
        | Npm of NpmCommand
        | Mirrord
        | Raw of cmd: string

    [<RequireQualifiedAccess>]
    module Command =
        /// `Build` has restored every project before a suite runs. `fable --run` forces `NODE_ENV=production` on the
        /// command it runs, and `env` resets it: under `production`, Vite reports a production build.
        let fableVitestArguments =
            [ "-o"; "output"; "-s"; "--noRestore"; "--run"; "env"; "NODE_ENV=test"; "npx"; "vitest" ]

        let private npmPath () =
            match ProcessUtils.tryFindFileOnPath "npm" with
            | Some path -> path
            | None ->
                "npm was not found in path. Please install it and make sure it's available from your path. "
                + "See https://safe-stack.github.io/docs/quickstart/#install-pre-requisites for more info"
                |> failwith

        let render: Command -> string * string list = function
            | Dotnet dotnet ->
                "dotnet",
                match dotnet with
                | Build -> [ "build" ]
                | Restore -> [ "restore" ]
                | Lint -> [ "fsharplint"; "lint" ]
                | Tests -> [ "run" ]
                | Pack -> [ "pack" ]
                | Publish -> [ "publish" ]
                | Fable -> [ "fable" ]
                | FableTests -> "fable" :: fableVitestArguments @ [ "run" ]
                | FableWatch -> [ "fable"; "watch" ]
                | Run -> [ "run" ]
                | WatchRun -> [ "watch"; "run" ]
            | Nuget nuget ->
                "dotnet",
                "nuget"
                :: match nuget with
                   | Push -> [ "push" ]
                   | AddSource -> [ "add"; "source" ]
            | Npm npm ->
                npmPath (),
                match npm with
                | Install -> [ "install" ]
                | Version -> [ "--version" ]
                | Ls -> [ "ls" ]
            | Mirrord -> "mirrord", []
            | Raw cmd -> cmd, []

    [<RequireQualifiedAccess>]
    module Rtk =
        open RtkFilter

        type Transport =
            /// Generic `rtk err <cmd>`: errors/warnings only.
            | Err
            /// Direct spawn — no rtk (long-running, or shell-metachar commands).
            | Raw

        let mode: Command -> Transport * Filter option = function
            | Dotnet Build -> Transport.Err, Some (Filter.dotnetBuild (Directory.GetCurrentDirectory ()))
            | Dotnet Publish -> Transport.Raw, Some (Filter.dotnetPublish (Directory.GetCurrentDirectory ()))
            | Dotnet Lint -> Transport.Raw, Some (Filter.fsharplint (Directory.GetCurrentDirectory ()))
            | Dotnet Tests -> Transport.Raw, Some (Filter.tests (Directory.GetCurrentDirectory ()))
            | Dotnet Pack -> Transport.Err, Some (Filter.dotnetPack (Directory.GetCurrentDirectory ()))
            | Dotnet Restore -> Transport.Err, Some (Filter.dotnetRestore (Directory.GetCurrentDirectory ()))
            | Dotnet Fable -> Transport.Raw, Some Filter.fable
            | Dotnet FableTests -> Transport.Raw, Some (Filter.vitest (Directory.GetCurrentDirectory ()))
            | Npm Install -> Transport.Err, None
            | Dotnet (Run | WatchRun | FableWatch) -> Transport.Raw, None
            | Npm (Version | Ls) -> Transport.Raw, None
            | Nuget _ -> Transport.Raw, None
            | Mirrord -> Transport.Raw, None
            | Command.Raw _ -> Transport.Raw, None

        /// What a command needs under RTK to write output its filter can parse.
        let arguments: Command -> string list = function
            | Dotnet FableTests -> [ "--reporter=json" ]
            | _ -> []

        let wrap (transport: Transport) (cmd: string) (args: string list): string * string list =
            match transport with
            | Raw -> cmd, args
            | Err -> "rtk", "err" :: cmd :: args

    /// Label the parallel runner prefixes a job's output lines with.
    type JobName = JobName of string

    [<RequireQualifiedAccess>]
    module JobName =
        let value (JobName name) = name

    type ExitCode = ExitCode of int

    [<RequireQualifiedAccess>]
    module ExitCode =
        let value (ExitCode code) = code
        let isSuccess (ExitCode code) = code = 0

    /// Every line a process wrote to either stream. Each stream is read on its own, so lines from
    /// the two interleave in the order they arrived, which can differ from the order they were written.
    type CapturedOutput = CapturedOutput of RtkFilter.Line list

    [<RequireQualifiedAccess>]
    module CapturedOutput =
        let value (CapturedOutput output) = output

    /// How the parallel runner surfaces a job's output.
    type OutputMode =
        /// Lines appear as the process writes them — the only option for one that never exits.
        | Streamed
        /// Captured, then rendered into the lines to print once the process has exited.
        | Buffered of (CapturedOutput -> ExitCode -> RtkFilter.Line list)

    type ParallelJob = {
        Name: JobName
        Process: CreateProcess<ProcessResult<unit>>
        Output: OutputMode
    }

    /// When the blocks of a grouped parallel run print.
    type BlockOrder =
        /// In job order, each as soon as its job and every job before it have exited.
        | JobOrder
        /// Each as soon as its job exits.
        | CompletionOrder

    /// How a parallel run sequences its jobs' output, chosen by the caller.
    type OutputSequencing =
        /// Jobs write to the console as they go, interleaved.
        | Live
        /// Captured from every job — a `Streamed` one included — then printed as one block per job.
        /// Requires every job to terminate.
        | GroupedByJob of BlockOrder

    // ---- Runners ----

    let private spawn dir (cmd: string) (args: string list) =
        let proc =
            CreateProcess.fromRawCommand cmd args |> CreateProcess.withWorkingDirectory dir

        if isRtkActive then
            proc |> CreateProcess.disableTraceCommand
        else
            proc

    /// `(Raw, None)` when RTK is inactive, so inactive output is byte-identical.
    let private plan (command: Command) =
        if isRtkActive then
            Rtk.mode command
        else
            Rtk.Transport.Raw, None

    let private wrapped (command: Command) (args: string list) (dir: string) =
        let transport, _ = plan command
        let cmd, verb = Command.render command
        let rtkArguments = if isRtkActive then Rtk.arguments command else []
        let cmd', args' = Rtk.wrap transport cmd (verb @ args @ rtkArguments)
        spawn dir cmd' args'

    let private capture (proc: CreateProcess<ProcessResult<unit>>) =
        let lines = Collections.Concurrent.ConcurrentQueue<RtkFilter.Line> ()

        let collect stream (text: string) =
            if not (isNull text) then
                lines.Enqueue { Stream = stream; Text = text }

        let result =
            proc
            |> CreateProcess.redirectOutputIfNotRedirected
            |> CreateProcess.withOutputEvents (collect RtkFilter.Stdout) (collect RtkFilter.Stderr)
            |> Proc.run

        CapturedOutput (List.ofSeq lines), ExitCode result.ExitCode

    let private write (line: RtkFilter.Line) =
        match line.Stream with
        | RtkFilter.Stdout -> printfn "%s" line.Text
        | RtkFilter.Stderr -> eprintfn "%s" line.Text

    // ---- Full-output tee ----

    /// Per user: the temp directory is shared, and a directory another user created is not writable.
    let private teeDir =
        Path.Combine (Path.GetTempPath (), sprintf "fake-rtk-tee-%s" Environment.UserName)

    /// Keep only the newest `keep` logs; the epoch-prefixed names sort chronological.
    let private rotateTee keep =
        let logs = Directory.GetFiles (teeDir, "*.log") |> Array.sort
        let excess = logs.Length - keep

        if excess > 0 then
            logs[.. excess - 1] |> Array.iter File.delete

    /// Save both streams to a rotated temp log, returning a `[full output: <path>]` hint; best-effort.
    let private teeFullOutput (command: Command) (CapturedOutput lines): string option =
        try
            Directory.ensure teeDir
            let slug = Command.render command |> fst
            let stamp = DateTimeOffset.Now.ToUnixTimeSeconds ()
            let unique = Path.GetRandomFileName().Substring (0, 8)
            let path = teeDir </> sprintf "%d_%s_%s.log" stamp slug unique
            File.writeString false path (lines |> List.map (fun line -> line.Text + "\n") |> String.concat "")
            // Rotation is best-effort cleanup; a failure here must not void the hint we can return.
            try rotateTee 20 with _ -> ()
            Some (sprintf "[full output: %s]" path)
        with _ ->
            None

    /// A failing run whose filter dropped meaningful lines has left the caller unable to see what
    /// actually went wrong, so its full log is worth teeing. A clean run — or a failing one whose
    /// filter kept every meaningful line — has not.
    let internal shouldTee (exitCode: ExitCode) (CapturedOutput raw) (filtered: RtkFilter.Line list) =
        let meaningful lines =
            lines |> List.filter (fun (line: RtkFilter.Line) -> line.Text.Trim () <> "") |> List.length

        not (ExitCode.isSuccess exitCode) && meaningful raw > meaningful filtered

    /// Lines to print for a finished buffered run: the filtered output, plus a `[full output: …]`
    /// hint when a failing run had output suppressed — the filtered lines are all the caller sees.
    let private report (command: Command) (filter: RtkFilter.Filter) (captured: CapturedOutput) (exitCode: ExitCode) =
        let filtered = filter.Run (CapturedOutput.value captured) (ExitCode.value exitCode)

        if shouldTee exitCode captured filtered then
            filtered @ (teeFullOutput command captured |> Option.map RtkFilter.Line.stdout |> Option.toList)
        else
            filtered

    /// Exit-code handling belongs to the parallel runner, which prints a buffered job's report
    /// before failing the build, so the process carries no `ensureExitCode`.
    let toJob (name: JobName) (command: Command) (args: string list) (dir: string): ParallelJob = {
        Name = name
        // FAKE's own command echo bypasses the runner's console lock, so the runner prints the start line itself.
        Process = wrapped command args dir |> CreateProcess.disableTraceCommand
        Output =
            match plan command |> snd with
            | Some filter -> Buffered (report command filter)
            | None -> Streamed
    }

    /// rtk closes a clean run with an `[ok] …` line carrying no newline, so even an unfiltered
    /// rtk run has to be captured and reprinted; a direct spawn keeps the console it inherited.
    let private outputFilter: Rtk.Transport * RtkFilter.Filter option -> RtkFilter.Filter option = function
        | Rtk.Transport.Raw, None -> None
        | _, None -> Some RtkFilter.Filter.passthrough
        | _, filter -> filter

    let private exitCode (command: Command) (args: string list) (dir: string): int =
        match plan command |> outputFilter with
        | Some filter ->
            let captured, code = wrapped command args dir |> capture
            report command filter captured code |> List.iter write
            ExitCode.value code
        | None -> (wrapped command args dir |> Proc.run).ExitCode

    /// Raises on a non-zero exit code.
    let run (command: Command) (args: string list) (dir: string) =
        match exitCode command args dir with
        | 0 -> ()
        | code ->
            let cmd, verb = Command.render command
            failwithf "`%s` exited with code %d" (cmd :: verb |> String.concat " ") code

    /// Whether the command exits zero; runs without echoing the command line or printing its output.
    let succeeds (command: Command) (args: string list) (dir: string) =
        let cmd, verb = Command.render command

        let result =
            CreateProcess.fromRawCommand cmd (verb @ args)
            |> CreateProcess.withWorkingDirectory dir
            |> CreateProcess.disableTraceCommand
            |> CreateProcess.redirectOutput
            |> Proc.run

        result.ExitCode = 0

    let runInRoot (command: Command) (args: string list) = run command args "."

    // ---- Parallel runner ----

    [<RequireQualifiedAccess>]
    module JobLine =
        let private cursorControl = Text.RegularExpressions.Regex @"\x1b\[[0-9;?]*[A-HJKfhlsu]"

        /// Keeps colour codes; cursor codes would move the line over a prefix written before it.
        let withoutCursorControl (line: string) = cursorControl.Replace (line, "")

    module private Parallel =
        let locker = obj ()

        let colors = [|
            ConsoleColor.Blue
            ConsoleColor.Yellow
            ConsoleColor.Magenta
            ConsoleColor.Cyan
            ConsoleColor.DarkBlue
            ConsoleColor.DarkYellow
            ConsoleColor.DarkMagenta
            ConsoleColor.DarkCyan
        |]

        let print color (colored: string) (line: RtkFilter.Line) =
            let writer =
                match line.Stream with
                | RtkFilter.Stdout -> Console.Out
                | RtkFilter.Stderr -> Console.Error

            lock
                locker
                (fun () ->
                    let currentColor = Console.ForegroundColor
                    Console.ForegroundColor <- color
                    writer.Write colored
                    Console.ForegroundColor <- currentColor
                    writer.WriteLine (JobLine.withoutCursorControl line.Text)
                )

        let onStdout index (JobName name) (line: string) =
            let color = colors[index % colors.Length]

            if isNull line then
                print color $"{name}: --- END ---" (RtkFilter.Line.stdout "")
            else if String.isNotNullOrEmpty line then
                print color $"{name}: " (RtkFilter.Line.stdout line)

        let onStderr (JobName name) (line: string) =
            let color = ConsoleColor.Red

            if isNull line |> not then
                print color $"{name}: " { Stream = RtkFilter.Stderr; Text = line }

        let printStartingJob index (job: ParallelJob) =
            let color = colors[index % colors.Length]
            let name = job.Name |> JobName.value
            let wd = job.Process.WorkingDirectory |> Option.defaultValue ""
            let exe = job.Process.Command.Executable
            let args = job.Process.Command.Arguments.ToStartInfo
            print color $"{name}: {wd}> {exe} {args}" (RtkFilter.Line.stdout "")

        let printStarting indexed =
            for index, job in indexed do
                printStartingJob index job

        let private streamProcess (job: ParallelJob) onOut onErr =
            job.Process
            |> CreateProcess.redirectOutputIfNotRedirected
            |> CreateProcess.withOutputEvents onOut onErr
            |> Proc.run
            |> _.ExitCode
            |> ExitCode

        let private runStreaming index (job: ParallelJob) =
            streamProcess job (onStdout index job.Name) (onStderr job.Name)

        let private printBlock index (JobName name) lines =
            let color = colors[index % colors.Length]

            for line in lines do
                print color $"{name}: " line

        let private printLines index lines =
            let color = colors[index % colors.Length]

            for line in lines do
                print color "" line

        /// Runs a job to completion with output captured. A buffered job is rendered through its
        /// report; anything else yields its raw non-empty lines.
        let private runCaptured (job: ParallelJob) =
            let captured, code = capture job.Process

            let lines =
                match job.Output with
                | Buffered report -> report captured code
                | Streamed -> CapturedOutput.value captured |> List.filter (fun line -> String.isNotNullOrEmpty line.Text)

            lines, code

        /// A buffered job yields nothing until it exits, so its whole report is printed in one
        /// locked pass — otherwise a concurrent job's live lines would split the block.
        let private runBuffered index (job: ParallelJob) =
            let lines, code = runCaptured job

            lock locker (fun () -> printBlock index job.Name lines)

            code

        let private restoreTerminalState () =
            // Reset basic TTY state in case an interrupted child process leaves it broken.
            // Without a terminal on stdin there is nothing to reset and `stty` only errors,
            // which would land in the middle of a buffered job's block.
            if not Console.IsInputRedirected then
                try
                    use procHandle =
                        Diagnostics.Process.Start (
                            Diagnostics.ProcessStartInfo (
                                FileName = "stty",
                                Arguments = "sane",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            )
                        )

                    procHandle.WaitForExit 1000 |> ignore
                with _ ->
                    ()

        let private printGroup index job lines =
            printStartingJob index job
            printLines index lines

        /// Every job runs to completion with its output captured, each printed as one block in
        /// `order`. Requires every job to terminate — a job that never exits stalls the whole run.
        let private runGrouped order (indexed: (int * ParallelJob)[]) =
            let jobs = indexed.AsParallel ()

            match order with
            | JobOrder -> jobs.AsOrdered () :> ParallelQuery<_>
            | CompletionOrder -> jobs
            |> _.WithMergeOptions(ParallelMergeOptions.NotBuffered)
            |> _.Select(fun (index, job) -> index, job, runCaptured job)
            |> Seq.map (fun (index, job, (lines, code)) ->
                printGroup index job lines
                job.Name, code
            )
            |> Seq.toArray

        let run sequencing jobs =
            try
                let indexed = jobs |> Seq.toArray |> Array.indexed

                match sequencing with
                | Live ->
                    printStarting indexed

                    indexed
                    |> Array.Parallel.map (fun (index, job) ->
                        let code =
                            match job.Output with
                            | Streamed -> runStreaming index job
                            | Buffered _ -> runBuffered index job

                        job.Name, code
                    )
                | GroupedByJob order -> runGrouped order indexed
            finally
                restoreTerminalState ()

    /// Raises when any job exits non-zero, after every job has finished and reported.
    let runParallelWith (sequencing: OutputSequencing) (jobs: seq<ParallelJob>) =
        match
            jobs
            |> Parallel.run sequencing
            |> Array.filter (snd >> ExitCode.isSuccess >> not)
        with
        | [||] -> ()
        | failed ->
            failed
            |> Array.map (fun (name, code) -> sprintf "%s (exit %d)" (JobName.value name) (ExitCode.value code))
            |> String.concat ", "
            |> failwithf "Parallel run failed: %s"

    /// Raises when any job exits non-zero, after every job has finished and reported.
    let runParallel: seq<ParallelJob> -> unit = runParallelWith Live
