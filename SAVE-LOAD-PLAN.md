# Save / Load Slice — Implementation Plan

Captures the design plan plus the pressure-test critique that surfaced gaps in
the first pass. Not started yet — items 1–6 in [Prerequisite decisions](#prerequisite-decisions)
must be answered before step 1 begins.

> **Update (delta-compression landed early, review finding #5).** The per-turn
> undo record already carries a **forward delta** of changed `$`-vars rather
> than a full store snapshot (`HarloweVariableStore.TakeStoryDelta`/
> `ResetStoryVars`, `StorySession`'s `List<SessionSnapshot>` + `Flatten`). So
> prerequisite-decision 6 / the "delta compression" follow-up is **done**, and
> step 2's `Moment` should carry `StoreDelta` (changed vars) instead of a full
> `StoreSnapshot`; the eventual serializer writes per-turn deltas (smaller
> blobs, closer to reference). Redo, per-redirect Moments, PRNG-state capture,
> and the `IVariableStore` serialization surface remain this slice's scope.

## Prerequisite decisions

These block progress. Each needs an explicit answer before the slice starts.

1. **IFID namespacing for slot names** — Reference Harlowe prefixes every
   `localStorage` key with the story's IFID so saves from different stories
   don't collide on shared backends. Our `Harlowe.Ifid` field exists.
   Decision needed:
   - (a) Library prefixes slot names with IFID internally before calling
     `ISaveStorage`. What happens when `Ifid` is empty?
   - (b) Host's responsibility (consumer code can prefix however it likes).
     Must be documented loudly — a Unity consumer wiring one `PlayerPrefs`
     backend across two stories will collide silently.

2. **`(load-game:)` return type** — Reference's signature is
   `(load-game: String) -> Command`. We don't have a `Command` value type;
   it's on the missing-value-types TODO. Decision needed:
   - (a) Define a minimal `Command` placeholder type as part of this slice.
   - (b) Substitute return shape: `HarloweValue.OfBool` on success, error
     value on failure.
   - (c) Return `null` and let the macro be a side-effect-only call.

3. **PRNG seed/iter capture timing** — Two semantics with different
   determinism properties:
   - (a) **Capture at Moment creation (Goto time)**: present Moment's iter
     snapshots at entry; `NextDouble` advances the live iter; on
     `Undo`+`Load`, reseed from snapshot, replay deterministically — but
     the in-flight passage's RNG calls aren't reflected back into the saved
     Moment until the next Goto.
   - (b) **Capture eagerly per call**: each `NextDouble` updates the present
     Moment's iter — saves are always current.

4. **Moment-per-turn vs Moment-per-redirect** — The agent's plan has *one
   Moment per turn with a `Visits` list for intra-turn redirects*. Reference
   has *one Moment per redirect*. `(history:)`, `turns`, and `visits` all
   observe Moments. Compatibility with reference saves (if anyone ever wants
   it) requires per-redirect; ours is simpler. Decision needed.

5. **`IVariableStore` interface evolution strategy** — Adding
   `SerialiseToDict`/`RestoreFromDict` breaks binary compat for external
   implementers. Options:
   - (a) Default-interface-method (requires C# 8+; Unity 2018 doesn't ship
     that — likely a non-starter for our consumer audience).
   - (b) Add only to the concrete `HarloweVariableStore`, downcast inside
     `SaveSerializer`. Cleanest back-compat, but couples save to the
     concrete impl.
   - (c) Take the breaking-change hit and call it out in the version note.

6. **Atomicity of failed `LoadGame`** — Blob references a removed passage,
   load fails. What's the session state afterwards?
   - (a) **Pre-load** — no change visible to author code. Only safe answer.
   - (b) **Partial-load** — variables restored, passage unset (broken).
   - (c) **Best-effort** — closest available passage, log warning.
   Recommendation: (a). Confirm.

---

## Design summary

- **`SessionSnapshot` → `Moment`**, lifted to its own public file
  (`HarloweParser/Runtime/Moment.cs`). Extends current fields with `Visits`
  (in-turn redirect trail, null when no redirects), `Seed`/`SeedIter` (PRNG
  state), and reserved `MockVisits`/`MockTurns` fields for the debug slice.

- **Timeline shape**: `Stack<SessionSnapshot> _undoStack` →
  `List<Moment> _past` + `Moment _present` + `List<Moment> _future`. Lets us
  ship redo as part of this slice (`FastForward`/`Redo` symmetric to
  `Undo`/`Rewind`).

- **Serialisation**: extend the existing hand-rolled `JsonWriter`/
  `JsonReader` rather than dragging in System.Text.Json/Newtonsoft (preserves
  netstandard2.0 / Unity-friendliness). Per-`HarloweValueKind` encoding;
  `Changer`/`Lambda` serialise as source strings via `MarkupPrinter` and
  round-trip through the parser (matches reference's `toSource` approach).

- **`ISaveStorage` host interface** modeled after `IRenderOutput`:
  constructor-injected on `StorySession`, four `Try`-prefixed members
  (`TryRead`/`TryWrite`/`TryDelete`/`Enumerate`), no `HarloweValue` exposure.
  Ship `InMemorySaveStorage` as default. Unity/Godot/web consumers plug their
  own.

- **`IRng` + `MulberryRng`** matching reference's mulberry32 + MurmurHash3
  byte-for-byte → save files cross-compatible with reference Harlowe stories.
  Replaces raw `System.Random` in `RandomMacro`/`EitherMacro`.

## File touch list

**Modified:**
- `HarloweParser/Runtime/StorySession.cs` — replace `Stack<SessionSnapshot> _undoStack`
  with `List<Moment> _past` + `Moment _present` + `List<Moment> _future`;
  rename `Undo` to `Rewind`, add `FastForward` symmetric pair, add
  `SaveGame`/`LoadGame`/`SavedGames`/`DeleteSave` public methods the macros
  call into. Keep `Undo` as a thin wrapper over `Rewind` for back-compat (one
  release deprecation cycle). Rewrite `History` getter to walk `_past` +
  `_present.Visits` per the corrected semantics. Inject `IRng` +
  `ISaveStorage` via two new optional constructor parameters.
- `HarloweParser/Runtime/HarloweVariableStore.cs` — add `SerialiseToDict()`
  returning `Dictionary<string, object>` for save-time, plus
  `RestoreFromDict(Dictionary<string,object>)` for load-time. The existing
  opaque-`object` `Snapshot`/`Restore` stay as the in-memory fast path used
  by `Goto`'s per-turn snapshot.
- `HarloweParser/Runtime/MacroContext.cs` — retype `Rng` field from `Random`
  to `IRng`.
- `HarloweParser/Runtime/Macros/RandomMacro.cs` — call `IRng.NextDouble()`
  and scale, instead of `Random.Next(lo, hi+1)`. Plus the existing
  bound-validation stays.
- `HarloweParser/Runtime/Macros/EitherMacro.cs` — same swap.
- `HarloweParser/Runtime/Macros/HistoryMacro.cs` — no code change, but the
  `IEvaluationContext.History` value it returns now reflects
  redirect-within-turn entries (because the session's History getter
  changes).
- `HarloweParser/Runtime/IVariableStore.cs` — see decision (5); shape
  depends on the choice.
- `HarloweParser/Runtime/Macros/StandardMacros.cs` — register the four new
  macros.
- `HarloweParser/Twee/JsonWriter.cs` — add `WriteRaw(string)`; minor.
- `TODO.md` — strike the `(history:)` TODO entry. `CLAUDE.md` — document
  the Moment timeline as the fifth load-bearing pivot in `Status`.

**New files:**
- `HarloweParser/Runtime/Moment.cs` — the Moment data class (public fields
  per house style: `PassageName`, `StoreSnapshot`, `VisitCounts`, `Visits`
  (List<string> nullable), `Seed`, `SeedIter`, `MockVisits`, `MockTurns`).
- `HarloweParser/Runtime/IRng.cs` — the PRNG interface.
- `HarloweParser/Runtime/MulberryRng.cs` — mulberry32 + MurmurHash3
  implementation, matching reference's `ts/state/prng.ts`.
- `HarloweParser/Runtime/Saving/ISaveStorage.cs` — the host-supplied save
  backend interface.
- `HarloweParser/Runtime/Saving/SavedGameInfo.cs` — DTO for `Enumerate`.
- `HarloweParser/Runtime/Saving/InMemorySaveStorage.cs` — default, used by
  tests and by consumers who only need session-lifetime saves
  (`Dictionary<string, (string blob, string filename)>` under the hood).
- `HarloweParser/Runtime/Saving/SaveSerializer.cs` —
  `Serialise(Moment[] past, Moment present, Moment[] future, Dictionary<string,int> visitCounts) → string`
  + `Deserialise(string blob, Harlowe story) → DeserialiseResult` (a small
  DTO with the restored timeline + an error string if the blob references a
  missing passage).
- `HarloweParser/Runtime/Saving/SaveBlobVersion.cs` —
  `public const int Current = 1` constant + the version-mismatch error
  string.
- `HarloweParser/Runtime/Macros/SaveGameMacro.cs`, `LoadGameMacro.cs`,
  `SavedGamesMacro.cs`, `DeleteSaveMacro.cs` — one class per file per house
  style. (See decision (1) re `DeleteSave` inclusion.)
- `HarloweParser.Tests/Runtime/Saving/SaveSerializerTests.cs`,
  `StorySessionSaveLoadTests.cs`, `MulberryRngTests.cs`,
  `Macros/SaveGameMacroTests.cs`.

## Sequenced steps

Each step is a landable, test-green commit.

1. **PRNG abstraction.** Introduce `IRng` + `MulberryRng`, swap
   `MacroContext.Rng` to `IRng`, update `RandomMacro`/`EitherMacro`. New
   tests: `MulberryRngTests` (known-seed → known-sequence vectors generated
   by running reference's `ts/state/prng.ts` against the same seeds). No
   save/load surface yet; the slice is just "deterministic seedable PRNG."
   Doesn't touch session API. ~150 LoC.

2. **Moment + timeline refactor** (no serialisation yet). Rename
   `SessionSnapshot` to `Moment`, lift to public file, extend with
   `Seed`/`SeedIter`/`Visits`. Replace `Stack<SessionSnapshot>` with
   `List<Moment> _past` + `Moment _present` + `List<Moment> _future`.
   Implement `FastForward` (redo). Keep `Undo` as alias for `Rewind`. Wire
   PRNG state through `Goto`. Update `History` getter to walk Moments.
   Strike the `(history:)` TODO entry. Existing tests should pass unchanged;
   new tests for `FastForward`, multi-redirect `(history:)`. ~300 LoC.

   **Hoisted from step 4 risk:** also do the `Changer`/`Lambda` source
   round-trip audit here. For every `IChangerPatch` type, verify
   `MarkupPrinter`→re-parse produces an equivalent AST. ~7 patch types,
   ~20 minutes. Catching a failure here saves grief in step 4.

3. **`IVariableStore.SerialiseToDict`/`RestoreFromDict`.** Per decision (5).
   Round-trip unit tests (`HarloweVariableStoreTests`). ~200 LoC.

4. **`SaveSerializer` + Moment JSON shape.** Walks a timeline into the blob
   object graph; reads it back. Hand-rolled JSON via the extended
   `JsonWriter`/`JsonReader`. Version-1 schema. Errors return a
   `DeserialiseResult` with an error string (missing-passage detection) — no
   exceptions. `SaveSerializerTests`. ~400 LoC.

5. **`ISaveStorage` + `InMemorySaveStorage`.** Interface, default impl,
   constructor-injection on `StorySession`. Empty session API for it (no
   macros yet). ~100 LoC.

6. **Macros `(save-game:)`, `(load-game:)`, `(saved-games:)`, plus
   `LoadedGame` infinite-loop guard.** Hook macros into
   `StorySession.SaveGame`/`LoadGame`/`SavedGames`. `MacroContext` gains a
   `LoadedGame` flag set on successful load and cleared per decision
   on flag-clearing timing (see [Smaller follow-ups](#smaller-follow-ups)).
   End-to-end session tests: save → mutate → load → assert state restored.
   ~250 LoC.

7. **(optional) `(delete-save:)` if confirmed in scope** plus parity checks
   against reference Harlowe save blobs (one fixture file). ~100 LoC.

8. **Docs.** Update CLAUDE.md: add Moment-timeline pivot to the
   load-bearing list and add a "Save/load" section under Architecture
   documenting the `ISaveStorage` contract for consumers. Strike the
   `(history:)` TODO from `TODO.md`.

Total roughly 1500 LoC + tests, split into 7–8 commits.

---

## Smaller follow-ups

These can be locked in during their respective steps rather than answered
up front.

- **Render tree on load is rebuilt, not deserialised.** `_liveRoot` and
  `_liveContext` are not part of the save blob. After load, the session
  re-renders the saved passage to rebuild them. Enchantments, click
  handlers, hook resolutions all re-derive. Author-visible cost: any
  dispatch-state from before save (an unfired `(click:)` handler the player
  hadn't clicked yet) is lost on load — only *story state* (variables,
  passage, turn count) restores. Document in macro docstrings so authors
  don't expect "save mid-puzzle, load, click resumes mid-puzzle."

- **`(save-game:)` called mid-render captures *current* in-progress state**,
  not entry-state. Matches reference. Pin in step 6.

- **`LoadedGame` infinite-loop flag clearing timing.** Reference clears on
  next user input. Our equivalent is `DispatchEvent`; auto-`(goto:)` chains
  do NOT count. Spell out exactly which call clears the flag, with a test
  for the auto-`(goto:)` infinite-loop case.

- **`VisitCounts` duplication across Moments.** Each Moment carries its own
  `VisitCounts` dict. With N turns and M distinct passages, memory is
  O(N×M). Reference rebuilds VisitCounts by walking past Moments — slower
  per query but constant memory. For typical stories (50–100 turns,
  20 passages) duplication is negligible; for replay-heavy stories it
  matters more for save-blob size than memory. Decide in step 2.

- **`ISaveStorage.TryWrite(slot, blob, fileName)` — separate vs combined
  storage of blob and filename.** Reference stores them separately so
  `(saved-games:)` can list metadata without parsing every blob. Options:
  - Treat filename as opaque associated metadata; require `Enumerate()` to
    be cheap (filename without blob read). Matches reference's perf.
  - Filename is part of the blob; `Enumerate()` reads + parses every blob.
    Simpler interface.

- **Default `InMemorySaveStorage` for production silent-data-loss risk.** A
  consumer who ships without wiring a backend gets in-memory saves that die
  at session end. Options:
  - `(save-game:)` succeeds silently — risk silent data loss.
  - Emit a one-time warning channel call so the host can log "you didn't
    wire persistent storage."
  - Null backend → `(save-game:)` returns false (matches reference's
    storage-unavailable path).

- **`(history:)` past-only vs past-plus-future** with redo landing.
  Reference is past-only. Match.

- **`(save-game:)` and `LastConditional` flow.** Confirm
  `(if: (save-game: "A"))[saved](else:)[failed]` correctly routes through
  the existing conditional-state mechanism. Should work by virtue of
  returning Bool, but pin with explicit test in step 6.

- **PRNG fixture file.** Generate by running reference's `prng.ts` against
  ~10 known seeds in a Node script; check into
  `references/prng-fixtures.json`. Do this before step 1 so MulberryRng
  tests can be written against the fixtures from the start.

---

## Open design questions (from initial Plan agent output)

These were raised in the first plan pass and folded into the prerequisites
above; preserved for traceability.

1. **`(delete-save:)` in or out?** Reference doesn't expose the macro
   (deletion is dialog-driven inside `(load-game:)` on load failure). User
   brief listed it. Recommendation: ship `ISaveStorage.TryDelete` so
   consumer code can implement deletion outside Harlowe markup, and skip
   the macro. Confirm.

2. **`(seed:)` macro.** Reference has it (near `setSeed` in
   `ts/macrolib/commands.ts`). Natural sibling of the save/load slice
   (deterministic story slot), ~20 LoC. Bundle in or defer?

3. **Save-blob version mismatch policy.** Reference shows a dialog; we're
   headless. Recommendation: `(load-game:)` returns false; session exposes
   `LastLoadError` for the host to display.

4. **Backward-compat for `Undo` name.** Today `bool Undo()` returns
   true/false. Reference uses `rewind(steps=1)`. Recommendation: keep
   `Undo` as the public face (Unity/Godot dev convention), add `Redo` as
   symmetric pair, internally rename to `Rewind`/`FastForward` only if it
   improves readability. Confirm.

5. **`Changer`/`Lambda` AST-vs-source serialisation.** Proposal:
   source-string round-trip via `MarkupPrinter`, robust against AST schema
   bumps. Reference does the same via `toSource`. Cost: a saved
   `(text-style: "bold")` re-parses on load. Acceptable?

6. **Per-Moment store size / delta compression.** Reference uses delta
   compression (`valueRef.ts`) to keep save files small. Defer entirely as
   a follow-up slice — until then, document that save blobs are
   O(turns × variables) and recommend `(forget-undos:)` (which we'd
   implement in the next slice that touches this code).

---

## Risks

- **PRNG byte-compat with reference Harlowe.** Tested via fixture vectors;
  if any C# integer-math quirk diverges from JS `Math.imul`, the tests
  catch it. Worst case wrap with `unchecked` blocks and explicit `(int)`
  casts to match JS semantics.

- **Changer patches may not be uniformly AST-bearing.** (The `Changer.Layers`
  accessor was removed as dead weight — serialization is source-string via
  `MarkupPrinter`, not layer introspection.) Audit hoisted into step 2 (see
  above): if any patch type stores a closure rather than the AST,
  source-round-trip won't work and we'd need either (a) regenerate source from
  the patch's typed fields or (b) reject changers in saved variables as
  unsupported (reference does store them, so we should match).

- **Per-Moment full snapshots inflate save blobs.** Mitigated by deferring
  delta compression to a later slice; until then, document that save blobs
  are O(turns × variables) and recommend `(forget-undos:)` (next slice).

- **`IRender­Output`-style breaking change on `IVariableStore`.** See
  decision (5). If we take option (c), the version note must call this
  out loudly — anyone implementing the interface externally needs to know.

## Follow-up slices unlocked by this work

- **`(forget-undos:)` and `(forget-visits:)`** — ride the timeline shape
  directly; trivial after step 2.
- **`(redo:)` macro** — one-liner over `FastForward`.
- **`(mock-visits:)`/`(mock-turns:)`** — the reserved fields on `Moment`
  let these land cleanly.
- **`(seed:)` macro** — if not bundled into this slice per decision (2).
- ~~**Delta compression** for save-blob size, per reference's `valueRef.ts`.~~
  **Done early** (review finding #5) — the in-memory undo record is now a
  forward delta; see the note at the top.

---

## Critical files (quick reference)

For someone picking this up cold:

- `E:\Git\twinelike\HarloweParser\Runtime\StorySession.cs` — primary touch
- `E:\Git\twinelike\HarloweParser\Runtime\Moment.cs` — new
- `E:\Git\twinelike\HarloweParser\Runtime\Saving\SaveSerializer.cs` — new
- `E:\Git\twinelike\HarloweParser\Runtime\Saving\ISaveStorage.cs` — new
- `E:\Git\twinelike\HarloweParser\Runtime\MulberryRng.cs` — new
- `ts/state.ts` (reference timeline model) and `ts/macrolib/commands.ts`
  (reference macro implementations + doc comments) — fetch from the
  `Codaea/harlowe-branch-default-2` GitHub mirror via `gh api` (Heptapod is
  Anubis bot-walled; see CLAUDE.md), or unzip a local
  `references/harlowe-branch-default.zip` snapshot for grep access (**not**
  committed, gitignored)
