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

- **Serialisation is source-based, not value-JSON.** `varscope.ts`'s
  `#serialiseVariableStore` saves each variable as Harlowe **source**
  (`ret[prop] = … toSource(value)`) and re-`eval`s it on load.
  `operationutils.ts`'s `toSource` is the universal value→source function:
  anything with a `toSource` method delegates to it, arrays → `(a:…)`, maps →
  `(dm:…)`, primitives → `JSON.stringify`. (Reference also has `at`/`via`/hash
  source-span ValueRefs (`valueref.ts`)
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
- **PRNG state is two scalars, recorded sparsely.** `(seed: string, seedIter:
  number)`; `mulberryMurmur32` reconstructs the generator in O(1)
  (`h = murmur(seed) + 0x6D2B79F5 * iter`). `State.random()` sets
  `present.seedIter` *only on a draw*; `setSeed` sets `present.seed` *only on
  `(seed:)`* — a fresh moment leaves both `undefined`, so `moment.ts`'s
  `#isEmpty()` (which counts `seed`/`seedIter`) lets a no-draw turn compress.
  Undo restore flattens the timeline — `reconstruct`'s `setPRNG(Current.seed,
  Current.seedIter)`, restoring to the restored turn's *start* (see the Timeline
  note for the boundary) — and reference explicitly keeps **no** per-moment copies
  ("Current does not maintain copies of these properties").
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
   macro-call site; `Compose` concatenates with `+` (see the `Changer.Source`
   note). A stored changer value is closure-free (the `renderHook` is an
   Apply-time parameter, not part of the patches), so the stamp covers style,
   iteration (`(for:)`), revision, and interaction kinds uniformly. (A `(for:)`
   changer's source is its lambda + the *evaluated* items — the `...`-spread
   already flattened by `LambdaArgs.ExpandItems`.)
3. **Visit counts** — derived by walking the timeline (drop per-Moment
   `VisitCounts`). Matches reference, shrinks the blob.
4. **IFID namespacing** — library prefixes the slot key internally with
   `Harlowe.Ifid` (matches reference). Empty IFID → unprefixed key; the
   cross-story-collision contract is documented on `ISaveStorage`.
5. **`(load-game:)`** — side-effecting navigation (no `Command` type): stages the
   deserialised timeline + a pending-load flag; the body renderer halts further
   nodes on it as for `(goto:)`/`PendingGoto` (BodyRenderer.cs:74), then the
   session installs the timeline and navigates after `Render()` returns (not a
   mid-`Render` discard — already-rendered content stands).
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
The `LoadedGame` guard is session-held (seeded into each per-render
`MacroContext`) and clears at the next `EnterPassage` *away* from the loaded
passage — auto-`(goto:)`/redirect included, matching reference's
per-`showPassage` `section.loadedGame` (engine.ts re-renders via `showPassage`
without the `loadedGame` option on any navigation). It still catches the real
infinite loop (a passage's *direct* re-load) while permitting load→`(goto:)`→load. `(redo:)` the macro is a follow-up; this slice ships
the session-level `Redo`/`FastForward`.

## Design summary

- **`HarloweValue.ToSource()`** — value → Harlowe source. Number→`FormatNumber`,
  String→a quoted literal via `MarkupPrinter`'s string-literal escaping (quotes,
  backslashes, and newlines re-lex safely), Bool→`true`/`false`, Array→`(a:…)`, Datamap→`(dm:…)`
  (keys sorted, matching reference's `mapEntriesSorter` — fidelity/determinism;
  round-trip is order-independent), Lambda→`MarkupPrinter` on its retained
  `LambdaNode`; HookName→synthesise a `HookRefNode` from the value's
  `Name`+`Steps` (it carries no node; copy the `IReadOnlyList<HookRefStep>` into
  `HookRefNode`'s `List`) then `MarkupPrinter`; Changer→its stamped source. The
  serialisation primitive; load re-evaluates the string via the existing
  tokenizer + `HarloweExpressionParser` + `ExpressionEvaluator` against a
  `MacroContext` assigned to `registry.Context` — its `Store` may be null
  (resolved source has no var refs), but the Context must be non-null: collection
  source like `(a:…)`/`(dm:…)` dispatches through `MacroRegistry.Invoke`, which
  throws if `Context` is unset.
- **`Changer.Source`** — stamped at two coordinated `ExpressionEvaluator` sites
  (not one chokepoint): `Visit(MacroCallNode)` sets `Source =
  "("+node.Name+":"+evaluatedArgs.ToSource…+")"` when the result is a Changer
  (name from the node, arg source from each *evaluated* arg's `ToSource` —
  matching reference's resolved `params`); `OpAdd`'s Changer case sets
  `composed.Source = left.Source+"+"+right.Source`. `Source` travels with the
  value, so a Changer read from a `$var` keeps its creation-time source —
  closing the empty-source gap. The one cross-cutting addition.
- **`Moment`** (public, `Runtime/Moment.cs`) — `PassageName`, `StoreDelta`
  (changed `$`-vars as `HarloweValue`), `Visits` (nullable `List<string>`
  redirect trail), **nullable** `Seed`/`SeedIter` (recorded sparsely —
  `SeedIter` only on a draw, `Seed` only on init/`(seed:)`; both null otherwise,
  so an empty turn compresses), reserved `MockVisits`/`MockTurns`/`ForgetVisits`.
  No `VisitCounts` (derived).
- **Timeline** — `List<Moment> _past` + `Moment _present` + `List<Moment>
  _future`. `Goto` pushes present→past, clears future; `Undo`/`Rewind` moves
  present→future and pops past→present; `Redo`/`FastForward` is symmetric.
  `Undo` stays a back-compat alias for `Rewind`. RNG state is **not** frozen per
  Moment: a draw stamps `_present.SeedIter` from `_rng` (the session reconciles it
  post-render/at-save, since macros see only `MacroContext`), the first Moment
  carries the session's initial seed, and undo/redo/load restore the RNG to the
  restored turn's *start*: `SeedIter` = the most-recent recorded **strictly
  before** the restored moment (flatten `_past`, *excluding* `_present`; `0` if
  none); `Seed` = the most-recent **at-or-before** it (inclusive — this slice
  always the session-initial seed on the first Moment, so restoring *to* it still
  finds the seed). The re-render then reproduces that turn's draws.
  (Strictly-before on the Seed would reseed the first moment empty; the moment's
  own `SeedIter` is the other off-by-one.)
- **PRNG** — `IRng` (`NextDouble()`, `Seed`/`SeedIter`, `SetSeed(seed, iter)`) +
  `MulberryRng` porting reference's mulberry32 + MurmurHash3 with `unchecked`
  32-bit math (`Math.imul` → `unchecked(a*b)` on int; `>>>` → `(uint)x >> n`).
- **Blob** — JSON array of moments via the existing `JsonWriter`/`JsonReader`;
  each var value a source string; a moment with no var changes, redirects, or
  recorded `Seed`/`SeedIter` compresses to a bare passage-name string (mirroring
  `#isEmpty()`). `SaveBlobVersion.Current = 1`.
- **`ISaveStorage`** — host interface modeled on `IRenderOutput`,
  constructor-injected: `TryRead`/`TryWrite`/`TryDelete`/`Enumerate`, no
  `HarloweValue` exposure. `InMemorySaveStorage` default.

## Sequenced steps

Each step is a landable, test-green commit.

1. **PRNG (mulberry32).** `IRng` + `MulberryRng`; swap `MacroContext.Rng`
   (`Random` → `IRng`); update `RandomMacro`/`EitherMacro` to reference's formula
   (`values.ts`), both ends inclusive (one-arg → `[0,a]`, two-arg → `[min,max]`),
   bounds truncated as today — but **without a narrowing cast**:
   `OfNumber(lo + (long)(NextDouble() * range))`, `range = (double)hi - lo + 1`.
   A `(int)` cast of a product ≥ 2³¹ is unspecified in C# (unlike JS `~~`'s
   mod-2³² wrap) and would *regress* `(random: -2e9, 2e9)`, which `Random.Next`'s
   large-range path handles correctly today; staying in `long`/`double` (Harlowe
   numbers are doubles) is correct for all spans and lets the `hi == int.MaxValue`
   guard be dropped — the rare span > 2³¹ then diverges from reference's `~~` wrap
   toward the uniform result (a MACRO-DIVERGENCES note). Branch one-arg-vs-two on
   **arg count**, not reference's `!b` falsy quirk (so `(random: -3, 0)` is
   `[-3,0]`, not garbage). `(either:)` → `args[(int)(NextDouble() * count)]`
   (count is small, the cast is safe). `StorySession`'s `int seed` ctor maps to
   a seed string. Note: `MacroContext.Rng` is a public field, so the `IRng`
   retype is a source-breaking change (version log) — and more than one site:
   the `MacroContext.Rng = new Random()` field initializer, both `StorySession`
   ctors (`new Random()` / `new Random(seed)`), the `_rng` field, the macros'
   `?? new Random()` fallbacks, and `V1MacroTests.Setup:17` (`new Random` →
   `new MulberryRng`) all migrate. The seeded `StorySession`-level tests survive
   (they assert determinism, not exact sequences).
   Fixture `references/prng-fixtures.json` from a Node script run against the
   reference `prng.ts` (independent vectors — not our own port; confirm Node is
   available). Tests: `MulberryRngTests` (known seed→sequence; `(seed,iter)` O(1)
   restore == replay). ~200 LoC.

2. **Moment + timeline + redo.** Lift `SessionSnapshot` → public `Moment.cs`
   (+ `Visits`/`Seed`/`SeedIter`, no `VisitCounts`). Replace `_undoStack` with
   `_past`/`_present`/`_future`; add `Redo`/`FastForward`; keep `Undo` as a
   `Rewind` alias. Record RNG state sparsely — a draw stamps `_present.SeedIter`
   from `_rng` (first Moment carries the initial seed); undo/redo/load restore to
   the restored turn's *start*: `SeedIter` = most-recent recorded **strictly
   before** the restored moment (flatten `_past`, excluding `_present`; `0` if
   none); `Seed` = most-recent **at-or-before** (inclusive, so restoring to the
   first moment keeps the initial seed). Re-rendering then reproduces that turn's
   draws. Derive `Visits`/`(history:)`, the `visits` keyword, and `Turns`
   (`_past.Count + present`, excluding `_future` so it drops after `Undo`) from
   the timeline; record each auto-`(goto:)`'s **target** into the present turn's
   `Visits` trail (the departing passage feeds the derived history) — matching
   `State.redirect()`'s `present.visits.push(newPassageName)` and decision 7; no
   new Moment. Pin the present-inclusion split: `(history:)`
   is past-only, but `visits(name)` = occurrences across past trails **plus the
   present turn's trail** (the current passage and any redirect targets), so
   `(print: visits)` stays `1` on first entry — a naive past-only walk regresses
   today's `EnterPassage` increment (StorySession.cs:303). Strike the
   `(history:)` TODO.
   Watch the redirect-chain delta accounting (all hops accrue to one turn's
   delta) and the RNG restore boundary (restore to the restored turn's *start*,
   not its recorded end — off-by-one). Existing undo tests stay green; add
   `FastForward` + multi-redirect-history + visit-derivation tests. ~350 LoC.

3. **Source serialisation.** `HarloweValue.ToSource()` for all nine kinds;
   `Changer.Source` stamped at the `ExpressionEvaluator` macro-call/compose
   chokepoint; `SaveSerializer` value↔source (`Serialise` → source string,
   `Deserialise` → re-lex+parse+eval). Verify `MarkupPrinter` round-trips Lambda
   (its `LambdaNode`) and a `HookRefNode` synthesised from a HookName's
   `Name`+`Steps` (the value has no node); non-finite numbers and `Error` values
   fail the save.
   Round-trip unit tests over every kind incl. composed changers. ~350 LoC.

4. **Blob serialiser (timeline).** `Serialise(past, present)` → blob (JSON moment
   array, per-Moment compression, `SaveBlobVersion` wrapper); `Deserialise(blob,
   story)` → `DeserialiseResult { timeline, error }` with missing-passage
   detection (atomic, no partial install). `_future` not serialised.
   `SaveSerializerTests`. ~250 LoC.

5. **`ISaveStorage` + `InMemorySaveStorage`.** Interface (blob + filename stored
   separately, cheap `Enumerate`), default impl, optional constructor param on
   `StorySession`. Library prefixes slot keys with `Harlowe.Ifid`. ~120 LoC.

6. **Macros + loop guard.** `(save-game:)` → Bool; `(load-game:)` → on success
   stage-timeline + pending-load, session installs + navigates to the loaded
   present passage + sets `LoadedGame`; on a missing slot or failed deserialise
   return `HarloweValue.OfError("I can't find a save slot named '…'")` in-prose
   (our error policy + reference's `TwineError`) *and* set `LastLoadError` for the
   host; `(saved-games:)` → Datamap from `Enumerate`. The loop guard is **session-held** (`StorySession._loadedGame`),
   seeded into each fresh per-render `MacroContext` and cleared at the next
   `EnterPassage` away from the loaded passage (auto-`(goto:)`/redirect *and*
   user nav — matching reference's per-`showPassage` `section.loadedGame`) — a
   flag on `MacroContext` alone resets every render (a new `ctx` per
   `RenderInternal`, StorySession.cs:330) and would never fire. End-to-end tests
   (save→mutate→load→assert; loop-guard — direct re-load rejected,
   load→`(goto:)`→load permitted; missing-slot →
   in-prose error + `LastLoadError`; `LastConditional` flow through
   `(save-game:)`'s Bool). ~300 LoC.

7. **Docs.** CLAUDE.md: Moment-timeline as the fifth load-bearing pivot + a
   Save/load Architecture section (source-serialisation model, `ISaveStorage`
   contract, IFID namespacing, what does/doesn't persist). Strike the
   `(history:)` TODO.

Total ~1570 LoC + tests across 7 commits.

## File touch list

**Modified:** `Runtime/StorySession.cs` (timeline + `Rewind`/`FastForward` +
`SaveGame`/`LoadGame`/`SavedGames` + `IRng`/`ISaveStorage` injection + derived
`History`/`Visits`/`Turns` (excluding `_future`) + session-held `_loadedGame`/`LastLoadError`/`LastSaveError`
seeded into each `MacroContext`); `Runtime/MacroContext.cs` (`Rng` → `IRng`
[public break], `LoadedGame` slot + pending-load, both seeded/read per render);
`Runtime/HarloweVariableStore.cs` (add non-destructive `PeekStoryDelta()` so a
mid-render `(save-game:)` doesn't clear the dirty set the next undo needs);
`Runtime/HarloweValue.cs` (add `ToSource()`); `Runtime/Changer.cs`
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
- **`(save-game:)` mid-render captures in-progress state**, not entry-state
  (matches reference) — via the non-destructive `PeekStoryDelta()`: the present
  turn's delta lives in `_dirtyStoryVars`, which `TakeStoryDelta` *clears*
  (HarloweVariableStore.cs:145) for the next undo, so a mid-turn save must peek,
  not take. The RNG needs the same: `_present.SeedIter` is reconciled only
  post-render, so a mid-render save must read `_rng.SeedIter`/`Seed` *live* for
  the present (the PRNG analogue of `PeekStoryDelta`), not the stale stamped
  value. Pin in step 6.
- **`LoadedGame` is session-held**, seeded into each per-render `MacroContext`,
  set for the loaded passage's render and cleared at the next `EnterPassage`
  away (auto-`(goto:)`/redirect included — reference's per-`showPassage`
  semantics). Test that a *direct* re-load errors but load→`(goto:)`→load is
  permitted.
- **Deferred reference optimisations** — `at`/`via`/hash source-span ValueRefs
  (blob-size delta-compression + cross-version resilience) and per-value
  `seed`/`seedIter`. Not needed while we save resolved-value source.

## Risks

- **Source round-trip fidelity.** `HarloweValue.ToSource()` → re-eval must be an
  identity for every kind; composed changers (`(a:)+(b:)`) and nested
  collections are the sharp edges. Covered by per-kind round-trip tests;
  `ToSource` rejects an un-sourceable value (non-finite number, `Error`) — the
  check recurses into collections and runs *before* `FormatNumber`, so a `NaN`
  nested in `(a: 1, NaN)` fails the save loudly rather than emitting unparseable
  `NaN` source.
- **Changer source-stamp coverage.** Every path that yields a `Changer` value
  must flow through the evaluator chokepoint that stamps `Source` — verify the
  style (`(text-style:)`/`(align:)`/…), `(for:)`, revision, and interaction
  families plus `+`-composition all do. (`(if:)`/`(unless:)` are *not* changers
  here — `IfMacro` returns a Bool + sets `LastConditional` — so they don't
  apply.) A missed path serialises a changer with an empty source.
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
