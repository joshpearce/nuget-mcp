#!/usr/bin/env bash
# Automates the grep-based half of the fixture vetting checklist documented in
# test-fixtures/NOTES.md (UsingTask, Exec build events, T4 templates, dedicated
# analyzer/source-generator packages, NuGet install/restore scripts). Re-run
# this on every pinned-commit bump, not just at initial vendoring.
#
# This does NOT replace human judgment: it distinguishes a dedicated analyzer/
# source-generator package (flagged) from an ecosystem tooling package that
# merely lists "analyzers" as one of several standard NuGet IncludeAssets
# buckets (e.g. Microsoft.EntityFrameworkCore.Tools, xunit.runner.visualstudio,
# coverlet.collector -- judged acceptable in NOTES.md) by matching on the
# package ID itself, not on IncludeAssets metadata. Anything outside this
# checklist (e.g. conditional build-time targets like BuildBundlerMinifier)
# still needs the same manual read NOTES.md already gives it.

# No `-u` (nounset): bash < 4.4 (e.g. macOS's stock /bin/bash 3.2) treats even
# an explicitly-declared empty array's "${arr[@]}" expansion as unbound under
# nounset, which would break the empty-results case (e.g. zero .tt files).
set -eo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
FIXTURES_DIR="$REPO_ROOT/test-fixtures"

if [[ ! -d "$FIXTURES_DIR" ]]; then
    echo "vet-fixtures: no test-fixtures/ directory found at $FIXTURES_DIR" >&2
    exit 1
fi

violations=0

# Collect the project/build files in scope: *.csproj, *.targets, *.props, *.tt
# (Directory.Build.props / Directory.Build.targets are covered by the *.props
# / *.targets patterns since find matches on suffix, not exact basename).
# Built with a plain `while read -d ''` loop rather than `mapfile -d ''` (bash
# 4.4+ only) so this also runs under macOS's stock /bin/bash (3.2).
project_files=()
while IFS= read -r -d '' f; do
    project_files+=("$f")
done < <(find "$FIXTURES_DIR" \
    \( -name '*.csproj' -o -name '*.targets' -o -name '*.props' \) \
    -print0)

tt_files=()
while IFS= read -r -d '' f; do
    tt_files+=("$f")
done < <(find "$FIXTURES_DIR" -name '*.tt' -print0)

install_script_files=()
while IFS= read -r -d '' f; do
    install_script_files+=("$f")
done < <(find "$FIXTURES_DIR" \( -iname 'install.ps1' -o -iname 'init.ps1' \) -print0)

# A zero-project-file result almost always means the submodules haven't been
# checked out yet (e.g. fresh clone before `git submodule update --init
# --recursive`), not that the fixtures are clean -- fail loudly instead of
# silently reporting "clean" against nothing.
if [[ "${#project_files[@]}" -eq 0 ]]; then
    echo "vet-fixtures: found 0 project/build files under $FIXTURES_DIR -- the fixture" >&2
    echo "submodules are likely not checked out. Run 'git submodule update --init --recursive'" >&2
    echo "and re-run this script; a zero-file result is not the same as a clean result." >&2
    exit 1
fi

report() {
    local check="$1" file="$2" detail="$3"
    echo "VIOLATION [$check]: $file -- $detail" >&2
    violations=$((violations + 1))
}

# 1. UsingTask -- custom MSBuild task definitions.
for f in "${project_files[@]}"; do
    if grep -q "UsingTask" "$f"; then
        report "UsingTask" "$f" "defines or references a custom MSBuild task"
    fi
done

# 2. <Exec ...> build events -- arbitrary command execution during build.
for f in "${project_files[@]}"; do
    if grep -qE '<Exec[[:space:]]' "$f"; then
        report "Exec" "$f" "runs an <Exec> command as part of the build"
    fi
done

# 3. T4 templates -- code generation via arbitrary template logic.
for f in "${tt_files[@]}"; do
    report "T4" "$f" "T4 template file present"
done

# 4. NuGet install/init PowerShell scripts -- arbitrary code execution on package install.
for f in "${install_script_files[@]}"; do
    report "InstallScript" "$f" "NuGet install/init PowerShell script present"
done

# 5. Dedicated analyzer/source-generator packages or project references.
#    Matches on the package/project name itself (e.g. NSubstitute.Analyzers.CSharp,
#    a project named SourceGenerator), not on IncludeAssets="...analyzers..."
#    metadata, since many legitimate ecosystem tooling packages set that asset
#    bucket without shipping custom analyzer/codegen logic against fixture code.
for f in "${project_files[@]}"; do
    while IFS= read -r line; do
        report "Analyzer/SourceGenerator" "$f" "$(echo "$line" | sed 's/^[[:space:]]*//')"
    done < <(grep -inE '<(Package|Project)Reference[^>]*Include="[^"]*(Analyzer|SourceGenerator)[^"]*"' "$f" || true)

    while IFS= read -r line; do
        report "Analyzer/SourceGenerator" "$f" "$(echo "$line" | sed 's/^[[:space:]]*//')"
    done < <(grep -inE 'OutputItemType="Analyzer"' "$f" || true)
done

file_count_summary="${#project_files[@]} project/build file(s), ${#tt_files[@]} .tt file(s), ${#install_script_files[@]} install-script file(s)"

if [[ "$violations" -gt 0 ]]; then
    echo "" >&2
    echo "vet-fixtures: $violations violation(s) found across $file_count_summary." >&2
    exit 1
fi

echo "vet-fixtures: clean -- no UsingTask/Exec/T4/install-script/dedicated-analyzer patterns found across $file_count_summary."
exit 0
