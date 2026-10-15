"""Plumbing shared by the migrate-* scripts: command runner and log, report, baseline, preflight asks, and the
README and CI edits every Fable-testing migration makes."""

import json
import re
import shutil
import subprocess
from pathlib import Path

EXPECT_FUNCTIONS = {
    "equal", "notEqual", "isTrue", "isFalse", "isNull", "isNotNull", "isSome", "wantSome", "isNone",
    "isOk", "wantOk", "isError", "wantError", "isEmpty", "isNonEmpty", "hasLength", "contains",
    "containsAll", "exists", "all", "stringContains", "isGreaterThan", "isLessThan", "throws", "throwsC",
    "isNotNaN", "isNotInfinity", "floatClose", "floatLessThanOrClose", "floatGreaterThanOrClose",
}
ANSI = re.compile(r"\x1b\[[0-9;?]*[A-Za-z]")
VITEST = "5"
"""The Vitest major Alma.Testing declares; `npm install vitest@5` takes its latest release."""

LOG = REPORT = BASELINE = PREVIOUS = None


def use_work_dir(directory):
    """Points the log, report, baseline and previous report at `directory`, which must lie outside the worktree."""
    global LOG, REPORT, BASELINE, PREVIOUS
    work = Path(directory).resolve()
    if work == Path.cwd() or Path.cwd() in work.parents:
        raise SystemExit(f"--work-dir {directory} is inside the worktree; create it elsewhere, for example with `mktemp -d`.")
    work.mkdir(parents=True, exist_ok=True)
    LOG, REPORT, BASELINE, PREVIOUS = (work / name for name in ("migration.log", "migration-report.md", "migration-baseline.txt", "migration-report.previous.md"))


def move(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    source.rename(target)


def run(command):
    line = f"$ {' '.join(command)}"
    print(line, flush=True)
    with LOG.open("a") as log:
        log.write(f"\n{line}\n")
        log.flush()
        return subprocess.run(command, stdout=log, stderr=subprocess.STDOUT).returncode == 0


def log_text(start=0):
    with LOG.open("rb") as log:
        log.seek(start)
        return ANSI.sub("", log.read().decode(errors="replace"))


def must(command):
    if not run(command):
        tail = "\n".join(log_text().splitlines()[-30:])
        raise SystemExit(f"\n{tail}\n\nFailed: {' '.join(command)}\nThe full output is in {LOG}. Fix it, then rerun this script with --rerun.")


def require_clean_tree(rerun):
    """The undo discards every uncommitted change, so a first run starts from a clean tree."""
    status = subprocess.run(["git", "status", "--porcelain"], capture_output=True, text=True)
    if status.returncode != 0:
        raise SystemExit("Run from inside a git repository.")
    if status.stdout.strip() and not rerun:
        raise SystemExit("The working tree has uncommitted changes; commit or stash them first. "
                         "To continue after a failed run of this script, pass --rerun.")


def build(target):
    return ["./build.sh", target] if Path("build.sh").exists() else ["dotnet", "run", "--project", "build/build.fsproj", "--", target]


def expecto(project, *arguments):
    return ANSI.sub("", subprocess.run(["dotnet", "run", "--project", str(project), "--", *arguments],
                                       capture_output=True, text=True).stdout)


def test_count(project):
    found = re.search(r"EXPECTO! (\d+) tests? run", expecto(project))
    return int(found.group(1)) if found else None


def test_count_of(project, test_list):
    """Tests Expecto runs under the root list `test_list`."""
    found = re.search(r"EXPECTO! (\d+) tests? run", expecto(project, "--filter-test-list", test_list))
    return int(found.group(1)) if found else None


def module_name(text):
    found = re.search(r"(?m)^(?:module|namespace)\s+(?:(?:rec|internal|private|public)\s+)*([\w.]+)", text)
    return found.group(1) if found else None


def write_report(done, edits, review, checks):
    def section(title, entries, empty):
        return f"## {title}\n\n" + ("\n".join(f"- {entry}" for entry in entries) if entries else empty) + "\n"

    results = None if checks is None else [f"{'ok' if ok else 'FAILED'}: {line}" for ok, line in checks]
    REPORT.write_text("\n".join([
        "# Migration report\n",
        f"This report covers the latest run only; the earlier run's is {PREVIOUS}.\n" if PREVIOUS.exists() else "",
        section("Done", done, "Nothing to migrate."),
        section("Left for you", edits, "Nothing left."),
        section("Guessed by the script, review each", review, "Nothing guessed."),
        section("Checks", results or [], "Not run yet."),
    ]))
    if checks is None:
        print(f"\nReport so far: {REPORT}")


def add_readme_node(done, edits):
    path = Path("README.md")
    line = "- [Node.js](https://nodejs.org) with npm (the tests run the Fable compiler and Vitest)"
    if not path.exists():
        edits.append(f"README.md: none found; state the Node.js and npm requirement ({line[2:]})")
        return
    text = path.read_text()
    if re.search(r"\bNode(\.js)?\b", text):
        return
    section = re.search(r"(?ms)^#{1,6}[ \t]*(?:Development[ \t]+)?Requirements\b.*?(?=^#{1,6}[ \t]|\Z)", text)
    bullets = list(re.finditer(r"(?m)^[-*][ \t].*$", section.group(0))) if section else []
    if not bullets:
        edits.append(f"README.md: no Requirements list to extend; add `{line}`")
        return
    at = section.start() + bullets[-1].end()
    path.write_text(text[:at] + "\n" + line + text[at:])
    done.append("README.md: Node.js and npm added to the requirements")


def add_node_to_ci(setup_node, done, edits):
    for workflow in sorted(Path(".github/workflows").glob("*.y*ml")):
        text = workflow.read_text()
        if "build.sh" not in text or "setup-node" in text:
            continue
        lines = text.splitlines()
        anchor = next((i for i, line in enumerate(lines) if "actions/setup-dotnet" in line), None)
        start = next((i for i in range(anchor, -1, -1) if re.match(r"\s*-\s", lines[i])), None) if anchor is not None else None
        if start is None:
            edits.append(f"{workflow}: add a {setup_node} step (node-version 24) before ./build.sh")
            continue
        dash = re.match(r"(\s*)-(\s+)", lines[start])
        key = " " * (len(dash.group(1)) + 1 + len(dash.group(2)))
        end = next((i for i in range(start + 1, len(lines))
                    if lines[i].strip() and len(lines[i]) - len(lines[i].lstrip()) <= len(dash.group(1))), len(lines))
        while end - 1 > start and not lines[end - 1].strip():
            end -= 1
        separator = [""] if end < len(lines) and not lines[end].strip() else []
        step = [f"{dash.group(1)}-{dash.group(2)}uses: {setup_node}", f"{key}with:", f"{key}  node-version: 24"]
        workflow.write_text("\n".join(lines[:end] + separator + step + lines[end:]) + "\n")
        done.append(f"{workflow}: {setup_node} after setup-dotnet")


def alma_testing_ask(arguments):
    text = Path("paket.dependencies").read_text()
    if re.search(r"(?m)^\s*nuget Alma\.Testing\b", text):
        return None
    main, _, groups = text.partition("\ngroup ")
    tests_packages = [number for number, line in enumerate(main.splitlines(), 1) if re.match(r"\s*nuget (?:Expecto|YoloDev\.Expecto)\b", line)]
    if re.search(r"(?m)^group Tests\b", "\ngroup " + groups):
        where = "to its `group Tests`, and list it under `group Tests` in tests/*/paket.references"
    elif tests_packages:
        where = f"after the test packages (the main group's line {tests_packages[-1]}), above any `// [ FAKE GROUP ]` comment"
    else:
        where = "to the main group, above any `// [ FAKE GROUP ]` comment"
    return f"paket.dependencies: add `nuget Alma.Testing {arguments.alma_testing}` {where}"


def npm_ask():
    return None if shutil.which("npm") else "install Node.js and npm; `./build.sh` runs `npm install`"


def vite_ask(remedy):
    """Vitest 5 needs Vite ^6.4, ^7 or ^8; with an older Vite locked, `npm install` refuses Vitest's peer range."""
    locked = locked_version("vite")
    if "vite" not in direct_dependencies() or locked is None:
        return None
    major, minor = (int(part) for part in re.findall(r"\d+", locked)[:2])
    if (major, minor) >= (6, 4):
        return None
    return f"package-lock.json has Vite {locked}, older than the 6.4 Vitest 5 needs: {remedy}"


def fscheck_ask():
    """Alma.Testing needs Expecto 11, whose Expecto.FsCheck is built on FsCheck 3; a test using FsCheck 2 has to be rewritten."""
    lock = Path("paket.lock")
    text = lock.read_text() if lock.exists() else ""
    found = re.findall(r"(?m)^    (Expecto\.FsCheck|FsCheck) \(((\d+)[^)]*)\)$", text)
    old = sorted({f"{name} {version}" for name, version, major in found if int(major) < (11 if name == "Expecto.FsCheck" else 3)})
    if not old:
        return None
    return (f"paket.lock resolves {', '.join(old)}; move to Expecto.FsCheck 11 and FsCheck 3 first, "
            "as docs/migration/fable-tests/references/fscheck.md in the engine repository says")


# ---- Vite upgrade ----

VITE = "8"
PLUGIN_REACT = "6"
"""The majors `vite_upgrade` installs: Vite 8 and the @vitejs/plugin-react that supports it."""
VITE_CONFIGS = [Path("src/Client") / f"vite.config.{extension}" for extension in ("mts", "ts", "mjs", "js")]


def direct_dependencies():
    try:
        package = json.loads(Path("package.json").read_text())
    except (OSError, ValueError):
        return {}
    return {**package.get("dependencies", {}), **package.get("devDependencies", {})}


def locked_version(name):
    try:
        return json.loads(Path("package-lock.json").read_text())["packages"][f"node_modules/{name}"]["version"]
    except (OSError, ValueError, KeyError):
        return None


def admits_major(peer_range, major):
    """Whether an npm range admits `major`, for the forms peer ranges use: `^`, `~`, `>=` with an optional `<`,
    `x`, `*` and `||`."""
    for alternative in (part.strip() for part in peer_range.split("||")):
        if alternative in ("", "*", "x", "latest"):
            return True
        lower = re.match(r"(\^|~|>=|>|=)?\s*v?(\d+)", alternative)
        upper = re.search(r"<(=?)\s*v?(\d+)", alternative)
        below_upper = upper is None or major < int(upper.group(2)) or (upper.group(1) == "=" and major == int(upper.group(2)))
        if lower is None:
            if upper and below_upper:
                return True
        elif lower.group(1) in (">=", ">"):
            if int(lower.group(2)) <= major and below_upper:
                return True
        elif int(lower.group(2)) == major:
            return True
    return False


def npm_view(spec, field):
    result = subprocess.run(["npm", "view", spec, field, "--json"], capture_output=True, text=True)
    try:
        value = json.loads(result.stdout) if result.returncode == 0 and result.stdout.strip() else None
    except ValueError:
        return None
    return value[-1] if isinstance(value, list) else value


def build_target(text):
    """`build.target`'s line in a Vite config: the `target:` among the direct options of `build: {`."""
    lines = text.splitlines(keepends=True)
    opening = next((i for i, line in enumerate(lines) if re.match(r"\s*build\s*:\s*\{\s*$", line)), None)
    if opening is None:
        return None
    outer = len(lines[opening]) - len(lines[opening].lstrip())
    inner = None
    for line in lines[opening + 1:]:
        if not line.strip():
            continue
        indent = len(line) - len(line.lstrip())
        if indent <= outer:
            return None
        inner = indent if inner is None else inner
        if indent == inner and re.match(r"""\s*target\s*:\s*(["'][^"'\n]*["']|\[[^\]\n]*\])\s*,?\s*$""", line):
            return line
    return None


def migrate_vite_config(done, edits, review):
    """Renames the Vite 7 options Vite 8 replaced and drops the ones its defaults cover, `build.target` among them,
    so the JS and the CSS target Vite 8's default browsers; lists the ones it cannot rewrite by pattern."""
    path = next((candidate for candidate in VITE_CONFIGS if candidate.exists()), None)
    if path is None:
        return
    original = path.read_text()
    text = re.sub(r"""(?m)^[ \t]*minify\s*:\s*(["'])(?:esbuild|oxc)\1\s*,?[ \t]*\n""", "", original)
    text = re.sub(r"\brollupOptions\b", "rolldownOptions", text)
    text = re.sub(r"""(?m)^[ \t]*esbuildOptions\s*:\s*\{\s*target\s*:\s*["'][^"'\n]+["']\s*,?\s*\}\s*,?[ \t]*\n""", "", text)
    target = build_target(text)
    if target:
        text = text.replace(target, "", 1)
        review.append(f"{path}: build.target {target.split(':', 1)[1].strip().rstrip(',')} removed, so the JS and the CSS target "
                      "the browsers Baseline lists as widely available; restore it, and set build.cssTarget, only for an app "
                      "that must support older browsers")

    if "esbuildOptions" in text:
        edits.append(f"{path}: move optimizeDeps.esbuildOptions to optimizeDeps.rolldownOptions; Vite 8 converts it with a deprecation warning")
    if re.search(r"""\bcssMinify\s*:\s*["']esbuild""", text):
        edits.append(f"{path}: cssMinify \"esbuild\" needs esbuild, which Vite 8 no longer installs; drop it (Lightning CSS) or add esbuild")
    if re.search(r"(?m)^\s*esbuild\s*:", text):
        edits.append(f"{path}: the `esbuild` options become `oxc`, whose JSX options differ")
    if "manualChunks" in text:
        edits.append(f"{path}: Rolldown replaces output.manualChunks with output.codeSplitting.groups")
    if re.search(r"react\(\s*\{[^)]*\bbabel\b", text, re.S):
        edits.append(f"{path}: @vitejs/plugin-react 6 dropped its Babel options; move them to @rolldown/plugin-babel")
    if text != original:
        path.write_text(text)
        done.append(f"{path}: Vite 8's defaults and option names (no esbuild minify, no build.target, rolldownOptions, "
                    "no esbuildOptions holding only a target)")
        review.append(f"{path}: rewritten by pattern; check the diff")


def vite_upgrade(done, edits, review):
    """Moves a client's Vite onto Vite 8 with @vitejs/plugin-react 6 and its other Vite plugins onto a release
    that admits it, and renames the config options Vite 8 replaced. Returns the npm specs to install."""
    direct = direct_dependencies()
    if "vite" not in direct:
        return []
    specs = [f"vite@{VITE}"] + ([f"@vitejs/plugin-react@{PLUGIN_REACT}"] if "@vitejs/plugin-react" in direct else [])
    for name in sorted(set(direct) - {"vite", "vitest", "@vitejs/plugin-react"}):
        peer = npm_view(f"{name}@{locked_version(name) or direct[name]}", "peerDependencies.vite")
        if peer is None or admits_major(peer, int(VITE)):
            continue
        latest = npm_view(name, "peerDependencies.vite")
        if latest and admits_major(latest, int(VITE)):
            specs.append(f"{name}@latest")
            review.append(f"package.json: {name} raised to its latest release, whose Vite range ({latest}) admits Vite {VITE}; check its changelog")
        else:
            edits.append(f"package.json: {name} declares Vite {peer} and no release admits Vite {VITE}; replace or drop it")
    done.append(f"package.json: {', '.join(specs)}, each its latest release")
    migrate_vite_config(done, edits, review)
    return specs


def bundle_sizes():
    """Bytes of the JS and CSS `./build.sh Bundle` writes into deploy/public."""
    files = [path for path in Path("deploy/public").rglob("*") if path.is_file()]
    return {suffix: sum(path.stat().st_size for path in files if path.suffix == f".{suffix}") for suffix in ("js", "css")}


def stop_with_asks(asks):
    """Stops before anything changes, listing every ask at once."""
    if asks:
        listed = "\n".join(f"- {entry}" for entry in asks)
        raise SystemExit(f"Needs from you before the script can run:\n{listed}\n\nNothing was changed. Do these, then run again with --rerun "
                         "(the tree is no longer clean once you have edited a file).")
