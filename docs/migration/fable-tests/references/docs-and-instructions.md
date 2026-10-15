# Updating documents and agent instructions

Read this in the docs step. Update every document and agent instruction recorded in the survey that describes
the old state; where one is generated (an APM-managed AGENTS.md or skill), edit its source and regenerate it.
Describe the new layout in the structure tree, testing strategy, success criteria and future improvements;
require Node and npm; delete "not verified under Fable" caveats and "run the tests under Fable" backlog items
the Shared suite now covers.

- README runs tests only through `./build.sh` (`-t Tests`, `WatchTests`); `dotnet run --project tests/DotNet`
  belongs in AGENTS.md alone.
- Find what still names the old layout:
  `grep -rnE 'tests/tests\.fsproj|tests/[A-Z][A-Za-z]*\.fs|--filter "|dotnet run --project tests\b|no Fable compiler|Fable test run' --include='*.md' .`.
  The root list is now `All`, so `--filter "<Module>"` becomes `--filter-test-list "<Module>"`.
- A SAFE-stack application's comments in `Client.Tests.fs` and `Server.Tests.fs` often describe Fable.Mocha ("no
  assembly-reflection discovery", "shared tests run as part of Client.Tests instead"); the shared list now runs on both.
- Check statements about where a test runs ("tests run on .NET only") and improvements waiting on a Fable
  test run; the Shared suite may have made them false or done.
- Reconcile spec rules the migration breaks, such as "no Fable reference in `tests/`" (Alma.Testing brings in
  Fable.Core) and "ask first before changing `build/`" (the new `Spec.library` call), and report both to the user.

Done when no document or agent instruction names the old test project, path or commands. Report every
adaptation made for repositories that do not fit.
