#!/usr/bin/env bash
# Automates the grep-based half of the fixture vetting checklist documented in
# test-fixtures/NOTES.md (UsingTask, Exec build events, T4 templates, dedicated
# analyzer/source-generator packages). Re-run this on every pinned-commit bump,
# not just at initial vendoring.
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

# 4. Dedicated analyzer/source-generator packages or project references.
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

if [[ "$violations" -gt 0 ]]; then
    echo "" >&2
    echo "vet-fixtures: $violations violation(s) found across ${#project_files[@]} project/build file(s) and ${#tt_files[@]} .tt file(s)." >&2
    exit 1
fi

echo "vet-fixtures: clean -- no UsingTask/Exec/T4/dedicated-analyzer patterns found across ${#project_files[@]} project/build file(s) and ${#tt_files[@]} .tt file(s)."
exit 0
