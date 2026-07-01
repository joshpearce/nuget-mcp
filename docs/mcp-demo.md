# MCP Demo: SharpZipLib CVE Reachability

These prompts drive the `nuget-usage` MCP server (registered in `.mcp.json`) against the in-repo
synthetic fixture at `test-fixtures/sharpziplib-cve`. The fixture pins the **vulnerable
`SharpZipLib 1.3.1`** in two projects that share the same dependency but exercise different surfaces:

- **`SafeConsumer`** — compression/creation only (`GZipOutputStream`, `FastZip.CreateZip`). Never
  calls the vulnerable extraction API. *Negative control.*
- **`ExposedConsumer`** — extracts untrusted archives via the zip-slip surface
  (`FastZip.ExtractZip`, `TarArchive.ExtractContents`, `ZipInputStream`). *Positive control.*

The point being demonstrated: a dependency scanner would flag *both* projects (both reference the
vulnerable version), but the `analyze_symbol_usage` tool answers the sharper question — **"is the
dangerous code path actually reachable?"** — cutting the dominant class of scanner false positives.

> Prerequisites: `.mcp.json` is approved (`/mcp` shows `nuget-usage` connected), and the fixture has
> been restored. The tool takes an absolute solution path; the prompts below use the checked-out
> location on this machine.

Solution path used below:
`/Users/josh/code/nuget_context/test-fixtures/sharpziplib-cve/SharpZipLibCve.sln`

---

## Prompt 1 — Positive **and** negative in one shot (the headline claim)

Copy/paste into Claude Code:

```
Use the nuget-usage MCP server's analyze_symbol_usage tool on the solution
/Users/josh/code/nuget_context/test-fixtures/sharpziplib-cve/SharpZipLibCve.sln
for the target symbol
ICSharpCode.SharpZipLib.Zip.FastZip.ExtractZip(string, string, string)

This is the vulnerable ZIP zip-slip API (CVE-2021-32842). Both projects in the
solution reference the vulnerable SharpZipLib 1.3.1. Group the reported usages by
project and tell me which project is actually reachable to the vulnerability and
which references the package but never calls the dangerous API. I expect at least
one usage in ExposedConsumer (positive) and zero in SafeConsumer (negative).
```

Expected: usages only in `ExposedConsumer` (`PackageInstaller.cs`), zero in `SafeConsumer` — so
`ExposedConsumer` is reachable/exploitable and `SafeConsumer` is a false positive for a naive
version scanner.

---

## Prompt 2 — Negative control is "API not called", not "library unused"

Copy/paste into Claude Code:

```
Using the nuget-usage MCP server, run analyze_symbol_usage twice on
/Users/josh/code/nuget_context/test-fixtures/sharpziplib-cve/SharpZipLibCve.sln:

1) target symbol: ICSharpCode.SharpZipLib.Zip.FastZip.ExtractZip(string, string, string)
2) target symbol: ICSharpCode.SharpZipLib.GZip.GZipOutputStream

Show me the usages per project for each. I want to confirm that SafeConsumer has
ZERO usages of the vulnerable ExtractZip API but DOES use SharpZipLib on the safe
compression surface (GZipOutputStream). This proves the clean CVE result is
"the dangerous path isn't reached," not "the dependency is unused."
```

Expected: `ExtractZip` → matches in `ExposedConsumer` only; `GZipOutputStream` → matches in
`SafeConsumer`. Together they show `SafeConsumer` genuinely uses the library, just not the vulnerable
API.

---

## Prompt 3 — Member-level precision on a dual-use type (bonus)

Copy/paste into Claude Code:

```
Using the nuget-usage MCP server's analyze_symbol_usage tool on
/Users/josh/code/nuget_context/test-fixtures/sharpziplib-cve/SharpZipLibCve.sln,
compare these two members of the same FastZip type:

- vulnerable:  ICSharpCode.SharpZipLib.Zip.FastZip.ExtractZip(string, string, string)
- benign:      ICSharpCode.SharpZipLib.Zip.FastZip.CreateZip(string, string, bool, string)

Report the file and line of each match. I want to see that extraction and
creation are discriminated at the member level (distinct, non-overlapping call
sites) rather than collapsed together at the type level.
```

Expected: `ExtractZip` and `CreateZip` resolve to distinct, non-overlapping call sites — so a
type-level scan would conflate the vulnerable and benign uses, but member-level symbol analysis keeps
them apart.

---

## Prompt 4 — Package-level view for contrast (optional)

Copy/paste into Claude Code:

```
Using the nuget-usage MCP server's analyze_package_usage tool on
/Users/josh/code/nuget_context/test-fixtures/sharpziplib-cve/SharpZipLibCve.sln
for package SharpZipLib version 1.3.1, list every usage grouped by project.

Then contrast: this package-level view flags BOTH projects (like a dependency
scanner would), whereas analyze_symbol_usage on FastZip.ExtractZip flags only
ExposedConsumer. That difference is the whole point.
```

Expected: both `SafeConsumer` and `ExposedConsumer` show up (both reference the package) — making the
contrast with the symbol-level reachability result explicit.

---

## The symbols at a glance

| Target symbol | Meaning | Reachable in |
|---|---|---|
| `ICSharpCode.SharpZipLib.Zip.FastZip.ExtractZip(string, string, string)` | ZIP zip-slip (CVE-2021-32842) | `ExposedConsumer` |
| `ICSharpCode.SharpZipLib.Tar.TarArchive.ExtractContents(string)` | TAR traversal (CVE-2021-32840/-32841) | `ExposedConsumer` |
| `ICSharpCode.SharpZipLib.Zip.ZipInputStream` | hand-rolled ZIP extraction | `ExposedConsumer` |
| `ICSharpCode.SharpZipLib.GZip.GZipOutputStream` | safe compression surface | `SafeConsumer` |
| `ICSharpCode.SharpZipLib.Zip.FastZip.CreateZip(string, string, bool, string)` | benign archive creation | `SafeConsumer` |

> Symbol-string rules (see `plans/cve-usage-detection-phase1-signature.md`): **member** targets must
> carry the exact parameter-type list (a method's display string includes it, so a bare member name
> matches nothing); **type** targets are coarser; never target a bare namespace.
