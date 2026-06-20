# Save / Load Slice — Implementation Plan

The finalized plan for the save/load slice: a `Moment[]` timeline replacing the
undo-stack snapshot model, `(save-game:)`/`(load-game:)`/`(saved-games:)`, a
host-supplied storage backend, a seedable reference-compatible PRNG so
`(random:)`/`(either:)` reproduce across save/load, and **source-based value
serialisation** matching reference Harlowe.

**Decisions locked:** full slice (PRNG + Moment/timeline + redo + serializer +
storage + the three headline macros); reference-exact mulberry32 PRNG;
source-based serialisation (`toSource` + re-eval on load, not tagged JSON);
visit counts derived from the timeline (not stored per-Moment).
`(delete-save:)`/`(seed:)`/`(redo:)`/`(forget-undos:)` are deferred follow-ups.

## What already landed (do not redo)

The slice was scoped before two things changed, so part of the groundwork is
done:

- **Delta-compressed undo.** `StorySession._undoStack` is a
  `List<SessionSnapshot>` whose entries carry a *forward delta* of changed
  `$`-vars (`StoreDelta`), not a full store clone —
  `HarloweVariableStore.TakeStoryDelta`/`ResetStoryVars` + `StorySession.Flatten`.
  Already the per-turn delta shape reference uses (its Moment `VarScope`), so the
  timeline is half-built. The in-memory delta stays `HarloweValue`-based;
  source conversion happens only at the save/load boundary.
- **A seedable session RNG** (`new StorySession(story, int seed)`), but it's
  `System.Random` — deterministic *within* a session, not serializable, not
  reference-compatible. Step 1 replaces it.

## Reference findings

Grounded in `ts/state.ts`, `ts/state/moment.ts`, `ts/state/prng.ts`,
`ts/state/valueref.ts`, `ts/datatypes/changer.ts`, `ts/utils/operationutils.ts`,
and the macro bodies in `ts/macrolib/commands.ts` (navigation) and
`ts/macrolib/values.ts` (`(random:)`/`(either:)`) (fetched from the
`Codaea/harlowe-branch-default-2` mirror):

- **Serialisation is source-based, not value-JSON.** `valueref.ts` saves each
  variable as Harlowe **source** (`toSource(value)`) and re-`eval`s it on load.
  `operationutils.ts`'s `toSource` is universal: anything with a `toSource`
  method delegates to it, arrays → `(a:…)`, maps → `(dm:…)`, primitives →
  `JSON.stringify`. (Reference also has `at`/`via`/hash source-span ValueRefs
  for blob-size delta-compression and cross-version resilience — an optimisation
  we **defer**; we save resolved-value source directly.)
- **Changers carry their call-shape and regenerate source.**
  `ts/datatypes/changer.ts` keeps `macroName` + `params` and
  `TwineScript_ToSource` returns `` `(${macroName}:${params.map(toSource)})` ``,
  chaining composed changers with `+`. This is why reference changers round-trip
  and ours (a flat patch list with no call-shape) currently can't.
- **Visit counts are derived, not stored.** `state.ts`'s `passageNameVisited`
  sums `name === Current.history[i]` over the flattened timeline — no per-Moment
  visit dict.
- **Moment is per-turn, not per-redirect.** `moment.ts` stores one Moment per
  turn with a `visits?: string[]` array holding intra-turn `(redirect:)`
  targets. Our per-turn + `Visits`-trail shape matches. `(history:)` flattens
  past Moments' passage names + their `visits` sub-arrays.
- **PRNG state is two scalars.** `(seed: string, seedIter: number)`;
  `mulberryMurmur32` reconstructs the generator in O(1)
  (`h = murmur(seed) + 0x6D2B79F5 * iter`). `state.ts` keeps `present.seedIter`
  live (updated each draw); past Moments freeze their end-of-turn value.
- **Failed load is atomic.** `State.deserialise` builds a fresh
  `reconstructedMoments` array and only swaps the timeline on full success.
- **Macro shapes.** `(save-game: String, [String]) -> Boolean` (false on storage
  failure); `(load-game: String) -> Command` — a navigating side-effect, for us
  the `(goto:)`/`PendingGoto` pattern; `(saved-games:) -> Datamap` of
  slot→filename. Reference prefixes the storage key with the IFID internally
  (`storagePrefix = "(Saved Game <ifid>) <slot>"`) and stores **blob and
  filename as separate keys** so `(saved-games:)` lists without parsing blobs.
  The infinite-loop guard is a `loadedGame` flag.

## Decisions

The six original prerequisites plus the forks settled by the findings:

1. **Serialisation model** — source-based: a new `HarloweValue.ToSource()` for
   every kind; load re-lexes + re-evaluates each value's source. The in-memory
   timeline stays `HarloweValue`-based (fast undo); conversion is confined to the
   blob boundary. (Replaces the original tagged-JSON design.)
2. **Changers in saved variables** — supported, the reference way: the evaluator
   stamps `(macroName: args.ToSource…)` source onto each `Changer` value at its
   macro-call site; `Compose` concatenates with `+`. A stored changer value is
   closure-free (the `renderHook` is an Apply-time parameter, not part of the
   patches), so the stamp covers style/revision/interaction kinds uniformly.
3. **Visit counts** — derived by walking the timeline (drop per-Moment
   `VisitCounts`). Matches reference, shrinks the blob.
4. **IFID namespacing** — library prefixes the slot key internally with
   `Harlowe.Ifid` (matches reference). Empty IFID → unprefixed key; the
   cross-story-collision contract is documented on `ISaveStorage`.
5. **`(load-game:)`** — side-effecting navigation (no `Command` type): stages the
   deserialised timeline, sets a pending-load the session acts on after render,
   aborting the current render and navigating like `(goto:)`.
6. **Atomicity of failed load** — pre-load; no state change on failure. The
   session exposes `LastLoadError` for the host.
7. **Navigation/turn semantics** — reference has `State.play()` (new Moment —
   `(go-to:)` + link navigation) and `State.redirect()` (same Moment, appends the
   departing passage to history + the target to `present.visits` — `(redirect:)`);
   both validate the target, defer, and halt the render (`{blocked:true}`). Our
   auto-followed in-render `(goto:)` maps to **`redirect()`**: same turn, no new
   Moment, record the trail so derived `(history:)`/`visits` include it.
   Host-driven `StorySession.Goto`/`DispatchEvent` (a link click, or a `(goto:)`
   in a click hook) is the new-turn boundary (= `play()`). Matches our
   architecture and avoids the `(go-to:)` undo footgun reference itself warns
   against ("use `(redirect:)` in place of `(go-to:)`"). A faithful separate
   new-turn `(go-to:)` vs `(redirect:)` macro split is a deferred follow-up.

Also locked: **blob interchange with browser Harlowe is a non-goal** (so a
`SaveBlobVersion` wrapper is fine; mulberry32's value is the clean
`(seed, seedIter)` serialisable state + future `(seed:)`, not blob interchange).
**Non-finite numbers** (NaN/±Inf) have no Harlowe source form → `(save-game:)`
fails loudly with `LastSaveError`. Blob + filename stored separately;
`InMemorySaveStorage` is the session-lifetime default (documented non-persistent;
a null backend makes `(save-game:)` return false). `(history:)` is past-only;
`_future` is neither in history nor serialised. A new `Goto` clears `_future`.
`LoadedGame` clears on the next user-driven `Goto`/`DispatchEvent`, never on the
auto-`(goto:)` follow path. `(redo:)` the macro is a follow-up; this slice ships
the session-level `Redo`/`FastForward`.

## Design summary

- **`HarloweValue.ToSource()`** — value → Harlowe source. Number→`FormatNumber`,
  String→quoted literal, Bool→`true`/`false`, Array→`(a:…)`, Datamap→`(dm:…)`,
  Lambda/HookName→`MarkupPrinter` on their AST, Changer→its stamped source. The
  serialisation primitive; load re-evaluates the string back to a value via the
  existing tokenizer + `HarloweExpressionParser` + `ExpressionEvaluator` (macro
  registry only — resolved-value source is self-contained, no store needed).
- **`Changer.Source`** — stamped at the evaluator's macro-call/compose chokepoint
  (`ExpressionEvaluator`), mirroring reference's `changer.ts` `(macroName:
  params)`. The one cross-cutting addition.
- **`Moment`** (public, `Runtime/Moment.cs`) — `PassageName`, `StoreDelta`
  (changed `$`-vars as `HarloweValue`), `Visits` (nullable `List<string>`
  redirect trail), `Seed`, `SeedIter`, reserved
  `MockVisits`/`MockTurns`/`ForgetVisits`. No `VisitCounts` (derived).
- **Timeline** — `List<Moment> _past` + `Moment _present` + `List<Moment>
  _future`. `Goto` pushes present→past, clears future; `Undo`/`Rewind` moves
  present→future and pops past→present; `Redo`/`FastForward` is symmetric.
  `Undo` stays a back-compat alias for `Rewind`. The present reflects live RNG
  state (read from `_rng` at save/finalise time); past Moments freeze theirs.
- **PRNG** — `IRng` (`NextDouble()`, `Seed`/`SeedIter`, `SetSeed(seed, iter)`) +
  `MulberryRng` porting reference's mulberry32 + MurmurHash3 with `unchecked`
  32-bit math (`Math.imul` → `unchecked(a*b)` on int; `>>>` → `(uint)x >> n`).
- **Blob** — JSON array of moments via the existing `JsonWriter`/`JsonReader`;
  each var value a source string; empty moment compresses to a bare passage-name
  string. `SaveBlobVersion.Current = 1`.
- **`ISaveStorage`** — host interface modeled on `IRenderOutput`,
  constructor-injected: `TryRead`/`TryWrite`/`TryDelete`/`Enumerate`, no
  `HarloweValue` exposure. `InMemorySaveStorage` default.

## Sequenced steps

Each step is a landable, test-green commit.

1. **PRNG (mulberry32).** `IRng` + `MulberryRng`; swap `MacroContext.Rng`
   (`Random` → `IRng`); update `RandomMacro`/`EitherMacro` to reference's exact
   formula (`values.ts`): `(int)(NextDouble() * (hi - lo + 1)) + lo`, both ends
   inclusive (one-arg → `[0,a]`, two-arg → `[min,max]`), bounds truncated as today.
   Compute the range as `double`/`long` to avoid int overflow — which lets the
   current `hi == int.MaxValue` guard be dropped. `(either:)` →
   `args[(int)(NextDouble() * count)]`. `StorySession`'s `int seed` ctor maps to
   a seed string.
   Fixture `references/prng-fixtures.json` from a Node script run against the
   reference `prng.ts` (independent vectors — not our own port; confirm Node is
   available). Tests: `MulberryRngTests` (known seed→sequence; `(seed,iter)` O(1)
   restore == replay). ~200 LoC.

2. **Moment + timeline + redo.** Lift `SessionSnapshot` → public `Moment.cs`
   (+ `Visits`/`Seed`/`SeedIter`, no `VisitCounts`). Replace `_undoStack` with
   `_past`/`_present`/`_future`; add `Redo`/`FastForward`; keep `Undo` as a
   `Rewind` alias. Thread live RNG `(seed, seedIter)` (present reads live; past
   freezes on finalise). Derive `Visits`/`(history:)` and the `visits` keyword
   from the flattened timeline; record each auto-`(goto:)`'s departing passage
   into the present turn's trail (redirect semantics, decision 7 — no new Moment,
   mirroring `State.redirect()`). Strike the `(history:)` TODO.
   Watch the redirect-chain delta accounting (all hops accrue to one turn's
   delta) and undo/redo RNG-state symmetry. Existing undo tests stay green; add
   `FastForward` + multi-redirect-history + visit-derivation tests. ~350 LoC.

3. **Source serialisation.** `HarloweValue.ToSource()` for all nine kinds;
   `Changer.Source` stamped at the `ExpressionEvaluator` macro-call/compose
   chokepoint; `SaveSerializer` value↔source (`Serialise` → source string,
   `Deserialise` → re-lex+parse+eval). Verify `MarkupPrinter` round-trips Lambda
   and HookName ASTs; non-finite numbers and `Error` values fail the save.
   Round-trip unit tests over every kind incl. composed changers. ~350 LoC.

4. **Blob serialiser (timeline).** `Serialise(past, present)` → blob (JSON moment
   array, per-Moment compression, `SaveBlobVersion` wrapper); `Deserialise(blob,
   story)` → `DeserialiseResult { timeline, error }` with missing-passage
   detection (atomic, no partial install). `_future` not serialised.
   `SaveSerializerTests`. ~250 LoC.

5. **`ISaveStorage` + `InMemorySaveStorage`.** Interface (blob + filename stored
   separately, cheap `Enumerate`), default impl, optional constructor param on
   `StorySession`. Library prefixes slot keys with `Harlowe.Ifid`. ~120 LoC.

6. **Macros + loop guard.** `(save-game:)` → Bool; `(load-game:)` →
   stage-timeline + pending-load, session installs + navigates to the loaded
   present passage + sets `LoadedGame`; `(saved-games:)` → Datamap from
   `Enumerate`. `MacroContext.LoadedGame` cleared on the next user-driven
   `Goto`/`DispatchEvent` (not auto-`(goto:)`/auto-load). End-to-end tests
   (save→mutate→load→assert; loop-guard incl. auto-goto case; `LastConditional`
   flow through `(save-game:)`'s Bool). ~300 LoC.

7. **Docs.** CLAUDE.md: Moment-timeline as the fifth load-bearing pivot + a
   Save/load Architecture section (source-serialisation model, `ISaveStorage`
   contract, IFID namespacing, what does/doesn't persist). Strike the
   `(history:)` TODO.

Total ~1550 LoC + tests across 7 commits.

## File touch list

**Modified:** `Runtime/StorySession.cs` (timeline + `Rewind`/`FastForward` +
`SaveGame`/`LoadGame`/`SavedGames` + `IRng`/`ISaveStorage` injection + derived
`History`/`Visits`); `Runtime/MacroContext.cs` (`Rng` → `IRng`, add `LoadedGame`,
pending-load); `Runtime/HarloweValue.cs` (add `ToSource()`); `Runtime/Changer.cs`
(+ `Source` field); `Runtime/ExpressionEvaluator.cs` (stamp changer source at the
macro-call/compose chokepoint); `Runtime/Macros/RandomMacro.cs` + `EitherMacro.cs`
(use `NextDouble()`); `Runtime/Macros/StandardMacros.cs` (register three macros);
`Runtime/Macros/HistoryMacro.cs` (no change — rides the derived `History`).

**New:** `Runtime/Moment.cs`; `Runtime/IRng.cs`; `Runtime/MulberryRng.cs`;
`Runtime/Saving/ISaveStorage.cs`; `Runtime/Saving/SavedGameInfo.cs`;
`Runtime/Saving/InMemorySaveStorage.cs`; `Runtime/Saving/SaveSerializer.cs`;
`Runtime/Saving/SaveBlobVersion.cs`; `Runtime/Macros/SaveGameMacro.cs`,
`LoadGameMacro.cs`, `SavedGamesMacro.cs`; `references/prng-fixtures.json`; tests
under `HarloweParser.Tests/Runtime/Saving/` + `MulberryRngTests`.

## Smaller follow-ups (settle during their step)

- **Render tree rebuilt on load, not deserialised.** `_liveRoot`/`_liveContext`
  aren't in the blob; load re-renders the present passage to rebuild
  enchantments/click handlers/hook resolutions. Only *story state* (vars,
  passage, turn count) restores — an unfired `(click:)` from before the save is
  lost. Document in the macro docstrings.
- **`(save-game:)` mid-render captures in-progress state**, not entry-state.
  Matches reference. Pin in step 6.
- **`LoadedGame` flag clearing** — must survive auto-`(goto:)` but clear on the
  next user-driven `Goto`/`DispatchEvent`. Test the auto-`(goto:)` loop case.
- **Deferred reference optimisations** — `at`/`via`/hash source-span ValueRefs
  (blob-size delta-compression + cross-version resilience) and per-value
  `seed`/`seedIter`. Not needed while we save resolved-value source.

## Risks

- **Source round-trip fidelity.** `HarloweValue.ToSource()` → re-eval must be an
  identity for every kind; composed changers (`(a:)+(b:)`) and nested
  collections are the sharp edges. Covered by per-kind round-trip tests; an
  un-sourceable value (non-finite number, `Error`) fails the save loudly rather
  than corrupting the blob.
- **Changer source-stamp coverage.** Every path that yields a `Changer` value
  must flow through the evaluator chokepoint that stamps `Source` — verify
  conditional changers (`(if:)`), `(for:)`, and `+`-composition all do. A missed
  path serialises a changer with an empty source.
- **PRNG byte-compat.** A C# integer-math quirk diverging from JS `Math.imul`/
  `>>>` is caught by independent fixture vectors; `unchecked` + explicit
  `(uint)x >> n` on netstandard2.0 (no C# 11 `>>>`). `h` accumulated as a JS
  double vs C# `uint` wrap diverges only past ~5M draws/session — document, don't
  fix.
- **Timeline refactor regresses working undo.** Steps 1–2 touch live navigation
  code; keep the undo suite green and land step 2 as its own reviewable commit.
  Visit-count derivation changes the working visit path — re-test `visits`.

## Critical files (quick reference)

- `HarloweParser/Runtime/StorySession.cs` — primary touch
- `HarloweParser/Runtime/HarloweValue.cs` — new `ToSource()`; serialisation hinges
  on it
- `HarloweParser/Runtime/ExpressionEvaluator.cs` + `Changer.cs` — changer
  source-stamp
- `HarloweParser/Runtime/HarloweVariableStore.cs` — `Flatten`/`ResetStoryVars`
  already supply load's install path
- `HarloweParser/Runtime/Moment.cs`, `MulberryRng.cs`,
  `Saving/SaveSerializer.cs`, `Saving/ISaveStorage.cs` — new
- Reference: `ts/state.ts`, `ts/state/moment.ts`, `ts/state/prng.ts`,
  `ts/state/valueref.ts`, `ts/datatypes/changer.ts`, `ts/utils/operationutils.ts`,
  `ts/macrolib/commands.ts`, `ts/macrolib/values.ts` — `gh api` against the
  `Codaea/harlowe-branch-default-2` mirror (see CLAUDE.md).
