# Test bench plan

How we find out how much real Harlowe plays correctly, on every runtime we target, and stop it going backwards. Researched 2026-11-21. Phase 0a has run from prototype scripts, and nothing is in this repository yet.

## Where testing stands

The unit tests all pass, but they only check behaviour someone already thought to write down. Three outside measurements, taken against the current build:

| Measure | Result |
|---|---|
| Reference Harlowe's own specs that can run without a browser | 7,001 checks from 1,599 of its 2,699 tests. **3,632 pass (51.9%)** under the Harlowe 4 profile and 3,629 under Harlowe 3. Real Harlowe 3.3.9 passes 6,732 (96.2%) |
| Public Harlowe 3.3 stories on GitHub, counted by pattern | 156 stories, about 30,800 macro calls. **74 (47%)** call only macros we implement, and about 25% of calls hit a macro we lack |
| MIT/CC0 Harlowe 3.3.9 stories, played by seeded random walks | 8 stories. **2 play with no in-prose errors**, and those two make only 2 macro calls between them |
| Walks recorded on real Harlowe 3.3.9 and replayed on Twinelike | **All 41 Service Suspended passages match**. Files-005 matches its first passage and stops at timed macros in its second |
| Generated markup snippets on both engines | **99.2% of about 103,000 comparable snippets match** |

So no story with real macro use plays cleanly yet, a story with no errors can still show the wrong text, and the results change what to build next. Most errors in the playthroughs are `$x is not set`: Harlowe 3 reads an unset variable as `0` (`COMPATIBILITY.md` row 2), and we still use the Harlowe 4 error. After that come `(show:)`, `(visited:)`, `(box:)` and `(hide:)`. We also don't run `startup`, `header` or `footer` passages, which 3 of the 8 stories use. By number of stories, the most-used missing macros are `(box:)` 27, `(live:)` 23, `(prompt:)` 16, `(stop:)` 14, `(css:)` 11, `(passage:)` 11, `(show:)` 10 and `(track:)` 10.

**Not yet verified:** no Unity, Godot or Phaser host has been built or run. Phase 0b exists to settle that before anything depends on it.

## Design

Four layers share one story corpus, one scenario format and one transcript format.

```
story + seed ──► real Harlowe 3.3.9 ──► recorded scenario + reference transcript
                                                 │ replayed on
                          ┌──────────────────────┴───────────────────┐
                          ▼                                          ▼
                  Twinelike on .NET                       Unity / Phaser / Godot
                          └───────────► transcripts ◄────────────────┘
                                             └──► compare, then ratchet
```

| Layer | Question | Oracle | Runs |
|---|---|---|---|
| 1. Reference conformance | Do we match Harlowe's documented behaviour? | Reference specs, confirmed on real 3.3.9 | Every push |
| 2. Story corpus | Do real stories load and play without crashes, hangs or errors? | Invariants | Every push |
| 3. Differential | Does a playthrough show what real Harlowe shows? | Harlowe 3.3.9 in a browser | Nightly, and on runtime changes |
| 4. Engine benches | Does it behave the same inside each engine, through its real text system? | The .NET transcript | Nightly, with Unity local at first |

### The ratchet

We can't require everything to pass at 52%, so a committed baseline records each check as `pass`, `fail` (doesn't work yet), `intended` (a deliberate difference, citing its `COMPATIBILITY.md` or `MACRO-DIVERGENCES.md` row), `unsupported` (can't be compared yet) or `quarantined` (flaky, with an expiry date). CI fails when a `pass` stops passing. New passes are recorded in the commit that earns them. Giving up a pass takes `--accept-regression "reason"`, and CI rejects a baseline diff that loses one without a reason.

Check IDs survive a re-pin: spec file, `describe` path, test title, ordinal and operation in Layer 1, and story, scenario and step elsewhere. Each baseline is stamped with the spec archive hash, extractor version, `format.js` commit, corpus pins and profile, and a mismatch stops the run rather than comparing against stale data. Story results ratchet per story and per passage, so one story's gain can't hide another's loss, and Layer 3 also ratchets how many steps match before the first divergence.

### Layer 1: reference conformance

The reference suite is 48 Jasmine spec files. A prototype runs them against recording stubs, with no browser, captures each test's steps up to the first point that needs a real DOM, and replays them through `StorySession` in about half a second.

The specs come from the pinned 4.0-unstable node while our target is 3.3.9, so every check runs under both profiles and only counts as a 3.3.9 requirement once it also passes on real 3.3.9. A check that holds for only one major is tagged with it, so adding Harlowe 3's unset-variable behaviour can't read as a regression in a Harlowe 4 check. `(live:)` and `(stop:)` have no reference specs, so their contracts come from observing real 3.3.9 in Layer 3.

The replay has to run a test the way the reference does: `clearState()` resets the session, the debug flag follows each suite, and `print` compares text as `markupToPrint` does. The specs are third-party Zlib source, so the extracted data is regenerated from the pinned archive and verified by SHA-256. Only check IDs and statuses are committed. The 1,100 tests with no portable check go in a committed ledger of why each stopped, mostly at a DOM query (887) or a timer (97), and those needing only clicks and visible text become Layer 3 scenarios.

### Layer 2: story corpus

Every story file is pinned to a commit and a SHA-256, recorded with its licence and copyright notice in `TestBench/corpus.json`, and fetched into a gitignored cache, so third-party content stays out of this repository. All 11 files below were checked byte for byte against GitHub at these commits.

| Story | Repo @ commit | Licence | Files | Passages | Macro calls | Reached / clean | Blocked by |
|---|---|---|---|---|---|---|---|
| **Files-005** (first target) | `nuuj-lord/Files-005` @ `ef9cca363164` | MIT | `index.html`, `source-code.twee` | 97 | 312 | 15 / 12 | Unset variables, `(after:)`, `(input:)`, `(box:)` |
| **egg.exe** (finish target) | `henriquek-btdg/egg-exe` @ `31db173ae3f5` | MIT | `index.html`, `src/index.twee` | 75 | 326 | 1 / 0 | `(box:)`, then `(track:)` |
| Service Suspended | `Znboston/service-suspended` @ `658b967ca7d1` | MIT | `project-1/docs/Service Suspended.html`, `project-1/src/Service Suspended.twee` | 41 | 2 | 41 / 41 | Nothing |
| Early in the Morning | `CristopherB14/createYourStory` @ `a952615872d4` | MIT | `earlyInTheMorning.html` | 34 | 0 | 34 / 34 | Nothing |
| Ethics in Science | `PyrrhicShadow/Pyrrhic-s-Twinery` @ `131fb674d68c` | MIT | `twine/EthicsInScience.html` | 19 | 57 | 7 / 5 | `(event:)`, `(visited:)`, parse errors |
| Lamplight | `IkeaSourPatches/Lamplight` @ `9f3915f69a81` | MIT | `index.html` | 11 | 301 | 1 / 0 | `(button:)`, `(input-box:)`, `(css:)` |
| harlowe-demo | `jreamy/harlowe-demo` @ `5cecb9546973` | MIT | `game.twee` | 9 | 311 | 4 / 1 | Unset variables |
| Sand-dancer, redux | `jgheithcock/sand-dancer` @ `fc7847d909c4` | CC0, see below | `Sand-Dancer.twee` | 76 | 1,414 | 7 / 0 | Unset variables, `(show:)`, parse errors |

"Reached" counts passages the walks visit, and "clean" those that never showed an error. Counts come from the published HTML where a story has both forms, and its Twee source stays a second corpus entry that exercises `TweeReader`.

Files-005 is the first target because it has the most macro-using passages we can already run: 31 of its 58 call only macros we implement. egg.exe is the goal, a finished game with source and export. `(box:)` stops it at the first passage, and 71 of its 74 macro-using passages call `(track:)`. The two clean stories only prove prose and links, so they're a regression floor. Sand-dancer is the heaviest macro user, but it ports a game by other authors, so the port's CC0 may not cover the original prose. It stays fetch-only, out of default CI, and is never redistributed. egg.exe deals with mental health and self-harm, so its text stays out of logs and reports we publish.

The crawler plays seeded walks through links and click regions, plus a hand-written walkthrough of each story's main path, because random walks rarely finish one. It fails on an exception, a stuck session, or a hang measured by a step budget rather than wall-clock time. Per story and passage it records load-time parse errors and broken links, passages reached and clean, and error kinds, and an inventory of each story's setup passages and missing macros marks what can't be compared yet. It also runs undo, redo and a save/load round trip at each step, which real content has never exercised. These are invariants and can't tell wrong text from right, which is Layer 3's job.

### Layer 3: differential against real Harlowe

A Node harness builds each story with Twine's own `harlowe-3.3.9/format.js`, pinned by commit, and plays it in headless Chromium through Playwright with outside network access blocked. Scenarios are recorded on real Harlowe, because a walk recorded on Twinelike would carry our own errors into the expected result. The transcripts are compared step by step, and the report stops at the first divergence unless asked for a full trace. A second check renders tens of thousands of generated markup snippets on both engines, which is how the pairing rules were validated.

```json
{ "story": "files-005", "seed": "42", "steps": [
  { "do": "start" }, { "do": "choose", "label": "Begin", "nth": 0, "event": "click" },
  { "do": "choose", "label": "the door", "nth": 0, "event": "mouseover" },
  { "do": "undo" }, { "do": "redo" } ] }
```

```json
{ "step": 2, "passage": "Lab", "errors": 0, "segments": [
  ["text", "You wake.\n"], ["choice", "Begin", "click"], ["text", " or "], ["choice", "the door", "mouseover"] ] }
```

- **Text** is the text inside `tw-passage`, minus the sidebar, with `<br>` as a newline and `script` removed, the same normalisation as the reference's `markupToPrint`, with errors removed. Our `Html` entries reduce to text the same way.
- **Choices** are anything the player can trigger, as one segment kind in document order that carries its event (`click`, `mouseover` or `mouseout`), and a step fires that event. On the page that's `tw-link` and elements with an `enchantment-link`, `-clickblock`, `-mouseover`, `-mouseoverblock`, `-mouseout` or `-mouseoutblock` class, and in `RenderResult` it's `Link` and `BeginInteractive`. One kind is deliberate: Harlowe draws `(link:)` as `tw-link`, where we use an interactive region. External URLs compare as text, and a choice nested inside another is `unsupported`.
- **Broken links** are their own segment: `tw-broken-link` on the page, and our label-plus-error pair on our side, which doesn't count as an error.
- **Destinations** aren't in the DOM, because 3.3.9 keeps a link's target in internal data. A target is checked when a scenario follows the link, by the next step's passage name.
- **Passage name** comes from 3.3.9's internal `State` object. Harlowe evaluates story scripts inside its own module scope, so the test build adds one story script that hands `State` to the harness, and `format.js` and the story's passages stay unchanged. `sessionStorage` won't do, because `(redirect:)` doesn't update it.
- **Errors** are compared by count, not wording, because our messages differ on purpose.
- **Randomness.** `MulberryRng` matched the generator in the 3.3.9 build on all 200 draws compared. `new StorySession(story, 42)` is seed `"42"` at position 0, and the same story script calls `State.setSeed("42")` before the first passage, so no library change is needed. Stories that call `(seed:)` themselves are `unsupported` until we implement it. `(shuffled:)`, which we lack, and the per-match rewind in divergence #23 consume draws differently and shift everything after them, so they're `unsupported` until aligned.
- **Time.** Playwright's clock API controls the browser's, stepping in increments of 16 ms or less so animation-frame loops run. `time` needs an injectable clock on our side, reset on each passage, and every step pumps frames until the passage, turn count and page stop changing, because `(go-to:)`, `(redirect:)`, `(undo:)` and `(load-game:)` navigate on a later frame and can chain. A scenario records a `wait` step where a story reveals its choices on a timer, and our replay stops there until we have timed macros.
- **Unsupported at first:** any passage calling a macro we lack, stories with their own `startup`, `header` or `footer` passages until we run them, and locale-dependent sort order.

### Layer 4: engine benches

One small `netstandard2.0` library, `Twinelike.TestBench.Core`, plays a scenario and writes a transcript. It takes and returns strings, so each host handles its own file access, and only the hosts target their engine's runtime. Hosts use the merged Release `twinelike.dll`, which is what consumers ship, copied by script with a hash check.

A bench asserts, in order: its normalised transcript equals .NET's; text and links set through the engine's real text component read back through that engine's own API; a real input event on a link navigates; and saves, structured link labels and undo-then-click work through the host. Load time, render time and allocations are recorded, and get budgets once there's a stable history. Screenshots wait for a GPU runner, because headless ones come out blank.

| | Unity | Phaser | Godot |
|---|---|---|---|
| Version | 6000.5.7f1 (installed), see decisions | 4.2.1 (npm) | 4.7.2 .NET (not installed) |
| How it runs the library | Plugin DLL, with `link.xml` and a pinned stripping level | .NET WebAssembly with `[JSExport]` | C# project, built before every run |
| Text system | TextMeshPro rich text and link IDs | One `Text` per fragment, with `Zone` hit areas on links | `RichTextLabel` BBCode and `meta_clicked` |
| File access | `StreamingAssets` in, a file or the player log out | `fetch` in, Playwright reads the result | `res://` in, `user://` out |
| Run headless | IL2CPP Windows player in batchmode. EditMode is only a smoke test | Playwright | `--headless` scene that exits with a status code |
| Main risk it covers | IL2CPP and code stripping | The WebAssembly build and its download size | The Godot .NET host |
| CI | Local at first, because `game-ci/unity-test-runner` needs Unity licence secrets | Node + Playwright | `chickensoft-games/setup-godot` |

Phaser 4 has no rich-text or link primitive, so the adapter is itself part of what's tested. It needs `dotnet workload install wasm-tools`. Godot's test framework, gdUnit4, is skipped at first because a plain scene with an exit code has no version-compatibility risk.

## Phases

Each phase ends in something usable, so the work can stop after any of them. About 21 working days remain.

| # | Work | Done when | Size |
|---|---|---|---|
| 0a | Browser feasibility | **Done 2026-11-21.** Gates 1 to 3 passed | 2 days |
| 0b | Engine feasibility | Gates 4 to 6 pass, or the plan changes. The spikes are kept as each host's skeleton | 3 days |
| 1 | Conformance runner, ratchet, and tests and CI on .NET 10 | A deliberately broken macro fails CI, and so does a baseline that loses a pass without a reason | 3 days |
| 2 | Corpus, fetcher, crawler and walkthroughs | All 8 stories run from verified pins with per-story metrics | 2 days |
| 3a | Differential harness and recorded scenarios | All 41 Service Suspended passages and at least 3 macro-using Files-005 passages, from their HTML entries, match real Harlowe. Filed differences don't count, and each names its blocking macro or divergence | 3 days |
| 3b | Injectable clock and `(seed:)` | A hand-written timed scenario and `(seed:)` story match on both engines | 1 day |
| 4 | Bench core and Unity host | Corpus transcripts match in an IL2CPP player | 3 days |
| 5 | Phaser host on WebAssembly | Corpus transcripts match in Chromium, and the bundle size is recorded | 3 days |
| 6 | Godot host | Corpus transcripts match headless | 2 days |
| 7 | Nightly workflow and triage report | One page ranks the next failures by how many stories they block | 1 day |

**Phase 0a gates**, all on real Harlowe 3.3.9:

1. Two runs of the same scenario give identical, non-empty transcripts for Service Suspended and Files-005. The passage name matches the known passage at the start and after a link, undo, redo, `(go-to:)`, `(redirect:)` and `(load-game:)`. A hand-written page with a `[[link]]`, a `(link:)`, a `(click:)` region, a `(mouseover:)` region and a broken link produces the expected segments on both engines.
2. A 50-draw `(random:)` story seeded through the test build's story script prints the same values on both engines, with no errors on either.
3. At least 90% of the extracted checks replay correctly. A sample of the failures is triaged into stub bugs and real 4.0 differences, each with a citation, and stub bugs are fixed before Phase 1 takes a baseline.

**Phase 0a result.** All three gates passed in Chromium. Every recorded walk replayed identically, 10 on Service Suspended and 6 on Files-005, and the 50 seeded draws matched with no errors. Real Harlowe passes 6,732 of the 7,001 extracted checks (96.2%). Of the 269 failures, 268 trace to entries in Harlowe's 4.0.0 change list, and one `(dropdown:)` spacing case has no entry. The triage found one harness bug, a swallowed debug flag, now fixed.

**Phase 0b gates** prove the plumbing, each playing one Service Suspended and one Files-005 scenario and matching the .NET transcript:

4. A Unity IL2CPP player with code stripping on.
5. `twinelike.dll` on .NET WebAssembly, driven from a Phaser scene in Chromium.
6. Godot 4.7.2 .NET, built and then run under `--headless`.

## Risks

- **.NET 8 reached end of support on 2026-11-10.** `ci.yml` and the test project still target it, so Phase 1 moves them to .NET 10, supported to November 2028. The library stays `netstandard2.0`, and the Godot host stays on `net8.0`, which is what Godot 4.7 uses.
- **Corpus repos can disappear.** The cache protects a local run but not a fresh CI runner. A missing story is reported as skipped rather than failed, and a private archive of the MIT stories, with their notices, would close the gap.
- **The reference mirror is unofficial.** It's pinned by content hash, and `CLAUDE.md` already documents the local-archive fallback.
- **Flaky browser tests would undermine the ratchet.** Layer 3 uses a virtual clock and seeded randomness. A case that differs between two identical runs is quarantined, and returns after several consecutive nightly passes.

## Decisions needed

1. **Unity version:** bench on the installed 6000.5.7f1, or install 6.3 LTS, supported until December 2027? Recommendation: the version the game will ship on, and the installed one until that's known.
2. **Unity in CI:** keep it local, or add Unity licence secrets to GitHub? Recommendation: local until the bench is stable.
3. **.NET toolchain:** the .NET 10 SDK at Phase 1 and `wasm-tools` at Phase 5 change this machine's global .NET setup. Recommendation: approve each at its phase.
4. **Third-party data:** keep stories and extracted specs out of the repository and fetch them by hash? Recommendation: yes.
