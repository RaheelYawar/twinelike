# Harlowe Parser — C# Library

## Project Overview
A C# library (netstandard2.0) for parsing and running Twine/Harlowe interactive fiction. Targets game engines (Unity, Godot, etc.) as the primary consumer — they implement `IRenderOutput` to drive whatever text layer they have (TextMeshPro tags, BBCode, plain text, etc.). Accepts Twine 2 HTML exports and Twee 3 source; emits Twee 3 back out.

- `ARCHITECTURE.md` — how it works: the host contract, every runtime subsystem, the five load-bearing decisions, and the file map. Read the relevant section before changing the runtime.
- `TODO.md` — open bugs and the roadmap. `MACRO-DIVERGENCES.md` — per-macro gaps vs reference Harlowe. `COMPATIBILITY.md` — deliberate Harlowe-version differences (the compatibility-profile switch table).
- `tools/README.md` — the test bench that compares Twinelike with real Harlowe 3.3.9.
- `RELEASING.md` — how to cut a release, from changelog to nuget.org.
- `docs/plans/` — finished design plans, kept for their reasoning. Active plans sit at the root.

## Build & Run
- **Framework:** Library targets `netstandard2.0` for maximum consumer reach (Unity 2018.1+, Godot 3/4, .NET Framework 4.6.1+, .NET 5+, Mono, Xamarin). Test project multi-targets `net48` + `net8.0`. Both SDK-style csprojs.
- **Build:** `dotnet build Twinelike.sln` (no warnings: broken doc-comment references count). **Test:** `dotnet test Twinelike.sln`.
- **Against real Harlowe:** `node tools/bench/verify.mjs` after any runtime change. It needs no browser or network, and fails when a suite matches fewer cases than `tools/cases/expected.json` records.
- **Output:** Library DLL (`twinelike.dll`) plus `twinelike.xml` — `AssemblyName` is lower-case `twinelike` (avoids case-sensitive-filesystem breakage on Linux CI / IL2CPP). The NuGet `PackageId` stays PascalCase `Twinelike`; the in-code namespace stays `Harlowe.*`.
- The library csproj sits at the repo root and uses `<DefaultItemExcludes>` to keep it from globbing the test and tools folders.

## Spec & reference sources
When a design question turns on how Twine/Twee/Harlowe is *supposed* to behave, fetch and quote the authoritative source before recommending a fix — don't reason from "what seems reasonable" or "what other tools probably do."

- **Twee 3 / serialization** → the Twee 3 spec at `github.com/iftechfoundation/twine-specs` (IFTF-maintained; closest to "official").
- **Harlowe macro/runtime semantics** → reference Harlowe source, branch `default`. The canonical Mercurial repo (`foss.heptapod.net/games/harlowe`) sits behind Anubis bot-protection that blocks automated fetches, so fetch from the `github.com/Codaea/harlowe-branch-default-2` mirror — a `branch/default` archive (the pinned audit baseline is `4.0.0-unstable` = v3.3.8 + 67, Mercurial node `facd4762815b17e1de9cb2e2a6c47cd223d23858`, recorded in the snapshot's `.hg_archival.txt`; re-pin here and in `tools/pins.json` when deliberately moving to a newer node — note `latesttag` reads v3.3.8 because the *last 3.x release*, v3.3.9, was tagged off a separate release branch, and its single fix is already in this node) carrying the same `ts/macrolib/*.ts` (macro semantics) and `ts/internaltypes/*.ts` (runtime types) — via the `gh api … /contents/<path>` raw pattern below. It's an unofficial personal mirror: verify provenance and treat it as less authoritative than the IFTF spec, and if it ever vanishes or goes stale, fall back to a local `references/harlowe-branch-default.zip` snapshot (gitignored — third-party Zlib source kept out of this MIT repo, so **not** committed; ask the user to download it via browser). The bench's spec suite reads the unpacked snapshot. The old `github.com/modality/harlowe` mirror is Harlowe 1.x — don't use it. Quote the relevant TypeScript, don't paraphrase.
- **Released behaviour** → when the manual and the 3.3.9 build disagree, measure the build: `tools/bench/record.mjs` renders cases on real Harlowe 3.3.9.
- **Cross-tool compatibility claims** → check at least one popular impl (Tweego at `github.com/tmedwards/tweego`, Extwee at `github.com/videlais/extwee`) before claiming "tools generally do X."

Use `gh api -H "Accept: application/vnd.github.raw" repos/<owner>/<repo>/contents/<path>` to fetch raw files — WebFetch has session limits. Distinguish authority levels: the IFTF spec is closest to "official"; third-party compilers are popular but not authoritative.

**Don't write line numbers** into docstrings or comments when citing reference source — they rot when the reference repo updates. Cite by file path + a quoted identifier or short phrase the reader can grep for ("the `ws` pattern in `ts/markup/patterns.ts`", "`varref.ts`'s `set()` parent-walk loop").

When quoting reference regex character classes into C# XML doc comments, **do not paste literal Unicode characters** — U+2028 (LINE SEPARATOR) and U+2029 (PARAGRAPH SEPARATOR) are treated as line breaks by the C# lexer, which silently truncates the `///` block (CS1519). Spell the code points out as text (`U+2028`, `U+202F`, etc.).

Real example: the `\::` body-content escape in `TweeWriter` looked like a normal Twee feature worth fixing symmetrically — checking the spec + Tweego + Extwee revealed it's a library-local extension nobody else implements, flipping the recommendation from "fix the asymmetry" to "document and leave."

## Version policy
**Target: the newest Harlowe release, currently 3.3.9** (what Twine 2.12.1 ships; 4.0 is unreleased) **audited against the 4.0-unstable tip** (the pinned node above). Where 4.0 deliberately changed the language and the change is coherent, each major keeps its own behaviour through a compatibility switch: `--` comments and erroring unset variables belong to 4.0, so a 3.x story gets literal `--` and unset story variables reading as `0`. Where the manual and the released build disagree, follow the build, because that's what stories were tested against: `(move: $p into $q)` deletes `$p` in real 3.3.9 and 4.0-unstable, against its own article. Where the unstable branch is broken, follow 3.3.9 and say so in the code.

**The long-term promise is per-major lock-in: a story keeps the semantics of the major it declares, indefinitely.** Twine binds a story to the newest version within its major, so lock-in is per *major*, not per minor, and it starts at 3.x — 1.x/2.x were never audited here. Delivery is one engine with append-only **compatibility profiles** (`HarloweProfile`; how it works is in `ARCHITECTURE.md`). Absent, unparseable and below-3 versions select V3, because guessing V4 would delete the prose after every `--`; only a major newer than we implement selects newest. Saves are pinned to `HarloweProfile.SaveFormat` and never follow the story, so an author's `format-version` bump can't re-interpret existing saves.

**Whenever a decision turns on a version difference, record it** (macro docstring + `MACRO-DIVERGENCES.md`), because those tags are the profile's switch inventory. Two standing rules keep the switch count bounded: our *own* bugs are fixed under every profile (only differences reference intended between majors get a switch), and new-in-N macros stay registered under older profiles (under real 3.x an unknown macro was an in-prose error, so no shipped story depends on its absence). The inventory lives in `COMPATIBILITY.md` (append-only — add a row the moment a version difference is found, so a missing decision is visible as an absent row rather than an unwritten one) and the procedure in `docs/plans/COMPATIBILITY-PLAN.md`. Adding a switch requires adding a probe to `CompatibilityProfileTests`, which reflects over the switch properties and fails on any switch that doesn't change behaviour — the class of bug here produces silent drift, never a red test, so that harness is the real safeguard. A probe asserting *rendered output* is necessary but not sufficient: measured on row 1, undoing the `ScanText` guard alone fragments the token stream while rendering byte-identically, and only a token-level assertion catches it.

## Code Conventions
- **Namespace:** `Harlowe`
- **Indentation:** 2 spaces
- **Braces:** Allman style (opening brace on its own line)
- **Private fields:** `_camelCase` prefix
- **Public properties:** PascalCase; expression-bodied members where concise
- **Data models:** public fields (not properties) for simple DTOs (`Branch`, `StyleSpec`); a property only where assigning must do something (`HarlowePassage.Body`/`RawBody` re-parse)
- **Null handling:** return `null` or `string.Empty` for missing lookups, not exceptions; a null argument to a public method throws `ArgumentNullException`
- **No LINQ** — explicit loops and Dictionary lookups

## Error Policy
In-prose errors, never exceptions — mirrors Harlowe's authoring model: a single bad expression renders an inline error at the spot it happened and the rest of the passage continues. `HarloweValue.Error` propagates through the evaluator (every operator short-circuits) and surfaces via `IRenderOutput.Error(message)`. No `try/catch` on the runtime hot path.

Parse errors follow the same contract at load time: they never abort a load, the tokenizer never throws, and the body parser recovers per node (`ARCHITECTURE.md`, "Parsers"). A loader throws `HarloweParseException` only when the file as a whole is unusable.

## Status

**4701 tests passing (net48 + net8.0); no high-severity divergences open.** 23 active divergences, all low severity, are listed in `MACRO-DIVERGENCES.md`, and feature gaps and next-slice candidates in `TODO.md`. Against real Harlowe 3.3.9, the bench's current match counts are in `tools/cases/expected.json`. Per-slice history lives in `git log`.
