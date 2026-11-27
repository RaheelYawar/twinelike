# Cleanup plan

From the 2026-11-26 project review. The quick fixes are done: doc comments fixed and `twinelike.xml` shipped, clear errors for API misuse, the README gaps, the divergence list's counts and order, and CONTRIBUTING. Two larger steps remain, then a Muse review of the code.

## 1. The differential harness moves into the repository

Everything that compares Twinelike with real Harlowe 3.3.9 lives in a session scratch folder: about 45 scripts and seven throwaway console projects, each referencing a built DLL by absolute path. A temp-folder cleanup would lose the project's main evidence of correctness.

```
tools/
  README.md             what each suite checks, and how to run and extend it
  pins.json             third-party inputs: URL, commit, SHA-256, licence
  Twinelike.Probe/      .NET 8 console, our side of every comparison
  bench/                Node: fetch, record on real Harlowe, compare, verify
  cases/                our snippets, and what real 3.3.9 rendered for them
  .cache/               gitignored: fetched inputs, transcripts, our outputs
```

- **One probe.** `Twinelike.Probe` replaces the seven projects with subcommands (`markup`, `clicks`, `live`, `nav`, `walks`, `specs`, `fuzz`, `stack`). It uses a project reference, so it always runs the current source. It's in the solution, so CI keeps it compiling, but CI doesn't run the suites.
- **What's committed.** Our own snippet cases and real Harlowe's output for them, which is test data we wrote and a result we measured, plus the scripts and pins.
- **What's fetched and never committed.** Harlowe's `format.js` (Zlib, from `klembot/twinejs` at tag 2.12.1), the reference archive and anything extracted from its specs, and the stories with their transcripts. Each is checked against its SHA-256. Sand-dancer and egg.exe stay out of default runs, as `TEST-BENCH-PLAN.md` says.
- **No absolute paths.** The browser comes from Playwright's own install location for the pinned `playwright-core`, with outside network blocked as now.

Steps, each verified before the next:

1. The probe, the case data and `compare` for the suites that need no browser or download: markup snippets, lists, collapse, custom macros, clicks, navigation, live, `(after:)`, revisions, datamap lists, unset and move. **Done when** `verify` prints the counts the scratchpad run prints on the same build.
2. Fuzzing, the case that used to hang, and recursion on small stacks, as probe subcommands. **Done when** they pass as in the scratchpad.
3. `fetch` and `pins.json`, then walks of harlowe-demo, Service Suspended and Files-005 recorded on real 3.3.9 into the cache and replayed here. **Done when** all 810 demo steps and all 41 Service Suspended passages match, and Files-005 matches 22 of 28 steps.
4. The reference specs: extracted from the pinned archive, run on real 3.3.9 and here. **Done when** the pass counts match the scratchpad's.
5. Recorders for each case kind, so new cases can be added. **Done when** re-recording one committed set reproduces it.
6. `tools/README.md`, and `TEST-BENCH-PLAN.md` updated to say what's now in the repository.

Left behind: one-off research and triage scripts, the 156-story GitHub scan, and the per-slice baselines. `verify` keeps its own copy of the last run's outputs to show what changed.

## 2. Documentation

- Finished plans move to `docs/plans/`: lists, revisions, render output, save/load and compatibility. Git history keeps the rest.
- `ARCHITECTURE.md` takes over `CLAUDE.md`'s architecture and file map, and catches up: custom macros, `(show:)`/`(hide:)`, revisions rendered in place, `startup`, timed hooks and `Tick`, collapsing each later render, lists, transitions, datasets, datamap names, and commands as values.
- `CLAUDE.md` keeps rules and pointers: build, reference sources, version policy, conventions, error policy and documentation rules. It's loaded into every AI session, so the aim is under a quarter of its 63 KB.

## 3. Muse code review

From a copy with no `.git`, `SECURITY.md`, `bin` or `obj`, plus the diff against `HEAD`. The review covers the API and doc-comment changes, whether the docs match the code, and the tools: correct comparisons, no absolute paths, and no third-party content committed. Findings are fixed and Muse confirms the final state.

## Result

Done 2026-11-26. The bench in `tools/` reproduces every count the scratch scripts gave, and its fuzzer, now seeded from every committed case, found a `(show:)` that stalled the host for over half a minute, now fixed. Muse's code review found 25 issues, most of them places where the probe and the recorder normalised differently. Fixing them moved one count: the clicks suite rose from 84 to 86 of 88, because the old probe had a passage the recorder's page didn't. Checking another showed that the snippet suite's `<n|` skip leaves out a browser artifact, now filed as divergence #45. 23 findings were fixed and 2 declined with reasons, and Muse's confirmation ended **ready**.

## Decided while the owner was away

- Finished plans are moved, not deleted, because moving is reversible.
- No release is cut. The changelog's Unreleased section is ready for one.
