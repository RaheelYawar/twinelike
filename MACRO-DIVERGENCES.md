# Macro Semantics Divergences vs Reference Harlowe

Findings are the gaps
between our macro implementations and reference Harlowe's, scoped to
**user-visible behavioural differences** — cosmetic and internal-implementation
divergences are skipped.

Already-fixed or already-filed items are excluded from this list — see
`TODO.md` `Known TODOs` for the standing tracking list and `SAVE-LOAD-PLAN.md`
for the save-model slice (which lands `(history:)` semantics).

## Counts

- **High severity (3 active, 2 fixed)**: silent wrong result or breaks documented Harlowe idioms.
- **Medium severity (4 active, 6 fixed)**: error-message divergence, missing feature an author would expect, or rare-case wrong result.
- **Low severity (1 active, 1 fixed)**: documented as deliberate or marginal.

Numbers below are stable IDs (referenced from "Recommended ordering"); fixed
items are kept and marked rather than renumbered.

---

## High-severity divergences

### 1. `(goto:)` doesn't validate target exists — ✅ FIXED (2026-06-01)

**Resolved.** `GotoMacro` now checks `MacroContext.PassageExists` (wired by
`StorySession` to the story's passage lookup) before recording the goto; a
missing target surfaces `I can't (goto:) to the passage 'X' because it doesn't
exist.` instead of silently navigating to a blank result. The check is skipped
when no story is wired (standalone renderer tests leave `PassageExists` null),
preserving the bare record-the-goto behaviour there. Scoped to the `(goto:)`
*macro*; the host `StorySession.Goto(name)` API still returns an empty result
for an unknown name (that's the host's explicit request, not an authoring
mistake). See `GotoMacro.cs`, `MacroContext.PassageExists`, and the
`PendingGoto_MacroToMissingPassage_*` tests. Original finding below.

- **Ours**: `GotoMacro` records the requested target via `context.RequestGoto`
  with no existence check. `StorySession.RenderInternal` then calls
  `EnterPassage` and emits an empty `RenderResult` (no error) when the passage
  doesn't exist. See `HarloweParser\Runtime\Macros\GotoMacro.cs` and
  `HarloweParser\Runtime\StorySession.cs` (RenderInternal / EnterPassage paths).
- **Reference**: `go-to`'s `typeChecker` calls `Passages.hasValid(name)` and
  returns `TwineError('macrocall', "I can't (go-to:) to the passage 'X' because it doesn't exist.")`
  before navigating. See
  `ts/macrolib/commands.ts` (search `(`go-to`,` and `hasValid`).
- **Trigger**: `(go-to: "Nonexistent")`
- **User-visible**: Reference shows an inline error citing the missing passage.
  Ours silently navigates to an empty result with no diagnostic, so a typo
  becomes a blank screen.

### 2. `(elseif:)` / `(else-if:)` not registered — ✅ FIXED (2026-06-01)

**Resolved.** `ElseIfMacro` now ships, registered under both `else-if` and
`elseif`. It renders iff the preceding conditional hook was hidden AND its
Boolean argument is true, and — critically — preserves `LastConditional` when it
hides, so `(if:)…(else-if:)…(else:)` ladders chain correctly (mirrors reference's
`elseif` special-case in `section.ts`). A stray `(else-if:)` with no preceding
conditional surfaces an in-prose error. See `ElseIfMacro.cs`,
`BodyRenderer`'s `isConditional` check, and the `ElseIf_*` tests in
`BodyRendererTests.cs`. Original finding below for the record.

- **Ours**: Not registered. `StandardMacros.RegisterAll` wires
  `IfMacro`/`ElseMacro`/`UnlessMacro` only. See
  `HarloweParser\Runtime\Macros\StandardMacros.cs`.
- **Reference**: Registered as `elseif` (documented as `(else-if:)`) with the
  same shape as `(if:)` plus a check that the preceding hook was hidden. See
  `ts/macrolib/stylechangers.ts` (search `(`elseif`,`).
- **Trigger**: `(if: $a)[A](else-if: $b)[B]`
- **User-visible**: Emits "unknown macro elseif" wherever the standard
  if/else-if/else ladder appears. The single most common conditional-chain
  idiom doesn't work.

### 3. `(if:)` / `(unless:)` returns Bool instead of Changer

- **Ours**: Returns `HarloweValue.Bool`. `BodyRenderer.Visit(MacroNode)`
  special-cases conditional macros and renders the attached hook from the
  bool. Composition with another changer via `+` errors because the operand
  isn't a Changer. See `HarloweParser\Runtime\Macros\IfMacro.cs` and
  `BodyRenderer.cs` (the `isConditional` branch).
- **Reference**: Returns a Changer (`new Changer('if', [expr])`). The changer
  composition machinery handles `(if: ...) + (text-style: ...)` directly —
  `enabled` is AND-ed with the boolean. See
  `ts/macrolib/stylechangers.ts` (search `(`if`,` and `IfTypeSignature` /
  `d.enabled &&=`).
- **Trigger**: `(set: $c to (if: $cond) + (text-style: "bold"))$c[content]`
- **User-visible**: Reference renders bold-iff-condition. Ours errors at the
  `+` step because a Bool can't be added to a Changer. Any story that composes
  a conditional with styling fails to render.

### 4. `(change:)` / `(enchant:)` reject string targets and `via`-lambdas

- **Ours**: `EnchantmentMacroSupport.Validate` requires first arg `HookName`
  and second arg `Changer`. Rejects string targets and rejects `Lambda`
  values. See `HarloweParser\Runtime\Macros\EnchantmentMacroSupport.cs`.
- **Reference**: Signature
  `[either(HookSet, String), either(Changer, Lambda.TypeSignature('via'))]` —
  string targets and `via`-lambdas are both supported, matching documented
  examples. See `ts/macrolib/enchantments.ts` (search
  `[`enchant`, `change`].forEach`).
- **Trigger**: `(change: "gold", (text-colour: yellow))` — or —
  `(change: ?passage's chars, via (text-color:(hsl: pos * 10, 1, 0.5)))`
- **User-visible**: The headline `(change:)` / `(enchant:)` examples in the
  docs all use string-target or `via`-lambda. They all error in our impl.

### 5. `(click:)` family rejects string targets and second-arg changers

- **Ours**: `MinArgs=MaxArgs=1`, first arg must be `HookName` (string targets
  explicitly deferred per `InteractionChangers.Build`). No optional second
  changer/lambda. No `(click-rerun:)`. See
  `HarloweParser\Runtime\Macros\InteractionMacros.cs`.
- **Reference**: Signature
  `[either(HookSet, String), optional(either(Changer, Lambda.TypeSignature('via')))]`.
  `(click-rerun:)` is registered by the same factory with `once=false`. See
  `ts/macrolib/enchantments.ts` (search `interactionTypes.forEach`).
- **Trigger**: `(click: "gold")[…]` — or —
  `(click: ?hat, (text-style: "bold"))` — or — `(click-rerun: ?hat)[…]`
- **User-visible**: String-target click/hover errors with "requires a hook
  name", so `(click: "literal text")` (a primary documented use) doesn't
  work. Second-arg changers error as arg-count. `(click-rerun:)` is unknown.

---

## Medium-severity divergences

### 6. `(else:)` silently no-ops with no preceding conditional — ✅ FIXED (2026-06-01)

**Resolved.** `ElseMacro` now returns an in-prose error
("There's nothing before this to do (else:) with.") when `LastConditional` is
null — a stray `(else:)`, or one after an intervening non-conditional macro that
reset the pairing — instead of silently hiding the hook. Matches reference
Harlowe's "lastHookShown === undefined" check. (`(else-if:)` already shipped with
the analogous error in #2.) See `ElseMacro.cs` and the `Else_*Errors` tests.
Original finding below.

- **Ours**: `ElseMacro` returns `render = (LastConditional == false)`.
  `LastConditional` is `bool?` defaulting to null; `null == false` is false,
  so a stray `(else:)` silently no-ops. See
  `HarloweParser\Runtime\Macros\ElseMacro.cs` and `MacroContext.LastConditional`.
- **Reference**: Errors with "There's nothing before this to do (else:) with."
  See `ts/macrolib/stylechangers.ts` (search `(`else`,` and
  `lastHookShown === undefined`).
- **Trigger**: Passage beginning with `(else:)[content]` (no preceding `(if:)`).
- **User-visible**: Reference flags the structural mistake. Ours hides the
  hook with no error, harder for the author to spot.

### 7. `(text-size:) / (size:)` requires Number-then-em

- **Ours**: Requires a Number; appends `em`. Rejects measurement strings and
  the optional line-height second arg. See
  `HarloweParser\Runtime\Macros\TextSizeMacro.cs`.
- **Reference**: `(text-size: Measurement, [Measurement])` — accepts 1–2 CSS
  Measurement values (`20px`, `1.5em`), enforces positive, rejects w/h units,
  sets `font-size` and `line-height`. See `ts/macrolib/stylechangers.ts`
  (search `([`text-size`, `size`],` and `size.value <= 0`).
- **Trigger**: `(size: 20px)` — the very example in the macro doc
- **User-visible**: Documented examples error in ours (Number-only). When a
  Number is accepted, units other than `em` are unreachable; line-height arg
  is silently dropped.

### 8. `(dm:)` silently overwrites duplicate keys — ✅ FIXED (2026-06-01)

**Resolved.** `DmMacro` now errors when a key appears twice
("You used the same data name (the string "X") twice in the same (dm:) call.")
instead of last-write-wins, matching reference's `map.has(key)` check. Keys stay
case-sensitive, so `"HP"`/`"hp"` are distinct (no false positive). See
`DmMacro.cs` and the `Dm_DuplicateKey_*` tests. Original finding below.

- **Ours**: Duplicate keys silently overwrite (`map[key] = value`). See
  `HarloweParser\Runtime\Macros\DmMacro.cs`.
- **Reference**: Errors with "You used the same data name (X) twice in the
  same (datamap:) call." See `ts/macrolib/datastructures.ts` (search
  `([`datamap`,`dm`],` and `map.has(key)`).
- **Trigger**: `(dm: "hp", 10, "hp", 5)`
- **User-visible**: Reference flags the bug; ours produces `{hp:5}`, masking
  the lost first value.

### 9. `(text:)` is unary; `(string:)` alias missing — ✅ FIXED (2026-06-01)

**Resolved.** `TextMacro` is now variadic (`zeroOrMore`): it concatenates every
argument's string form with no separator, so `(text: "You have ", $hp, " HP")`
works and `(text:)` is `""`. Only String/Number/Boolean/Array are accepted (a
Datamap/Changer/Lambda errors), matching reference's
`either(String, Number, Boolean, Array, CodeHook)` signature (we have no CodeHook
type); an Array stringifies to its comma-joined elements. Registered under
`text`, `str`, and `string`. See `TextMacro.cs`, `StandardMacros.cs`, and the
`Text_*`/`String_*`/`Str_*` tests. Original finding below.

- **Ours**: `MinArgs=MaxArgs=1`; `ToHarloweString` of any kind. No `(string:)`
  alias registered. See `HarloweParser\Runtime\Macros\TextMacro.cs` and
  `StandardMacros.cs`.
- **Reference**: Variadic over `String|Number|Boolean|Array|CodeHook` (rejects
  e.g. Changer/Lambda) and joins all args with empty separator. Registered
  under `text`, `str`, and `string`. See `ts/macrolib/values.ts` (search
  `([`str`, `string`, `text`],`).
- **Trigger**: `(text: "You have ", $hp, " HP")` — or — `(string: "x")`
- **User-visible**: Author-friendly join idiom fails in ours with an
  arg-count error. `(string:)` call is reported as an unknown macro.

### 10. `(for:)` requires ≥1 item; `(loop:)` alias missing — ✅ FIXED (2026-06-01)

**Resolved.** `ForMacro` now has `MinArgs=1`, so a lambda with zero trailing
items (`(for: each _x)[…]`, or `...` over an empty array) is accepted and the
hook is simply not printed — no arg-count error — letting authors loop over a
possibly-empty array unguarded. Registered under both `for` and `loop`. See
`ForMacro.cs`, `StandardMacros.cs`, and the `For_ZeroItemArgs_*`/`Loop_*` tests.
Original finding below.

> **Related remaining gap (not part of #10, still open):** reference `(for:)`
> also accepts a `where`-filter lambda — its own documented example is
> `(for: _ingredient where it contains "petal", ...$reagents)` — filtering the
> iterated items. Ours requires an `each`-form lambda and rejects the `where`
> form (pinned by `ForMacroTests.For_NonEachLambda_Errors`). Supporting it means
> applying the lambda's `where` clause as a filter inside the iteration loop
> (`Changer.RunIteration` / `IterationSpec`). File as its own slice if wanted.

- **Ours**: `MinArgs=2` (lambda + at least one item). No `loop` alias. See
  `HarloweParser\Runtime\Macros\ForMacro.cs` and `StandardMacros.cs`.
- **Reference**: `[Lambda.TypeSignature('where'), zeroOrMore(Any)]` —
  accepts zero items and silently doesn't print the hook (explicitly
  documented). Aliased as `loop`. See `ts/macrolib/stylechangers.ts` (search
  `([`for`, `loop`],`).
- **Trigger**: `(for: each _x, ...$emptyArr)[…]` — or — `(loop: each _x, 1, 2)`
- **User-visible**: Reference no-ops cleanly on empty; ours errors arg-count,
  breaking guarded iteration over possibly-empty arrays. `(loop:)` is
  unknown-macro.

### 11. `(replace:) / (append:) / (prepend:)` are unary

- **Ours**: `MinArgs=MaxArgs=1` — single hook-name-or-string target. No
  empty-string check (no matches, no error). See
  `HarloweParser\Runtime\Macros\ReplaceMacro.cs` and `RevisionChangers.cs`.
- **Reference**: `rest(either(HookSet, String))` — variadic; explicit
  `!scopes.every(Boolean)` check produces "A string given to this (replace:)
  macro was empty." See `ts/macrolib/enchantments.ts` (search
  `revisionTypes.forEach`).
- **Trigger**: `(replace: ?a, ?b)[…]` — or — `(replace: "")[…]`
- **User-visible**: Multi-target form errors with arg-count in ours.
  Empty-string target is silent in ours instead of producing a diagnostic.

### 12. `(num:)` accepts Number passthrough; `(number:)` alias missing — ✅ FIXED (2026-06-01)

**Resolved.** `NumMacro` now takes a single `String` and rejects a `Number`
argument (matching reference's `[String]` signature), and converts with JS-style
`+expr` coercion: `""`/whitespace → `0`, leading/trailing whitespace ignored,
decimals / `"1e3"` / signs accepted, `"Infinity"`/`"-Infinity"` → ±∞, and an
unparseable string errors with `I couldn't convert the string "X" to a number.`.
Registered under both `num` and `number`. (One JS edge deliberately skipped:
`0x`/`0o`/`0b` integer literals.) See `NumMacro.cs`, `StandardMacros.cs`, and the
`Num_*`/`Number_*` tests. Original finding below.

- **Ours**: Accepts Number unchanged AND String-parses. Uses
  `double.TryParse` with `NumberStyles.Float` (rejects empty string,
  whitespace-only, "Infinity"). No `(number:)` alias. See
  `HarloweParser\Runtime\Macros\NumMacro.cs`.
- **Reference**: Signature `[String]`; rejects Number input. Uses JS `+expr`,
  so empty string → 0, whitespace → 0, "Infinity" → Infinity, "1e3" → 1000.
  Registered as both `(num:)` and `(number:)`. See `ts/macrolib/values.ts`
  (search `([`num`, `number`], `Number`,`).
- **Trigger**: `(num: "")` — or — `(num: 5)` — or — `(number: "3")`
- **User-visible**: `(num: "")` returns 0 in reference but errors in ours.
  `(num: 5)` is a type error in reference but a passthrough in ours.
  `(number:)` is unknown-macro in ours.

### 13. `(align:)` only recognises four arrow forms — ✅ FIXED (2026-06-02)

**Resolved.** `AlignMacro` now validates arrows with reference's regex
`^(==+>|<=+|=+><=+|<==+>)$`, so any-length arrows work and map by shape to
left/right/centre/justify. Off-centre centre arrows (e.g. `=><==`, `==><=`) carry
a calibrated margin-left percentage in the new `StyleSpec.AlignCenterOffsetPercent`
(reference's `round(centerIndex / (length - 2) * 50)`; the balanced 25% case is
true centre and leaves it null); `HtmlRenderOutput` renders off-centre as a
half-width centred block with `margin-left`/`max-width`/`display:block`. See
`AlignMacro.cs`, `StyleSpec.cs`, `HtmlRenderOutput.cs`, and the `Align_*` /
`OffCentreAlignment_*` tests. Original finding below.

- **Ours**: Recognises exactly four spellings: `<==`, `==>`, `=><=`, `<==>`.
  Off-centre and longer arrows error (acknowledged in code comment). See
  `HarloweParser\Runtime\Macros\AlignMacro.cs`.
- **Reference**: Regex `^(==+>|<=+|=+><=+|<==+>)$` accepts arbitrary-length
  and off-centre arrows; off-centre produces a calibrated margin-left
  percentage. The doc's own usage example is `(align: "=><==")`. See
  `ts/macrolib/stylechangers.ts` (search `(`align`,` and `arrow.indexOf`).
- **Trigger**: `(align: "=><==")`
- **User-visible**: The reference's documented example errors in ours;
  off-centre alignment is unreachable.

### 14. `(sorted:)` rejects mixed number+string input

- **Ours**: Requires every value to share the first value's kind; a mixed-kind
  input errors with "(sorted:) can't compare values of different types".
  Numbers sort numerically, strings via `string.CompareOrdinal`. The docstring
  claims "Matches stock Harlowe 3.3.8" — that claim is **false** for the
  mixed-type case. See `HarloweParser\Runtime\Macros\SortedMacro.cs` (the
  `item.Kind != kind` check). (The ordinal-vs-alphanumeric string ordering is a
  separate, deliberately-documented divergence — see CLAUDE.md.)
- **Reference**: Sorts mixed arrays, numbers ahead of strings. The doc's own
  example `(sorted: ...$a)` over `(a:'A','C','E','G',2,1)` produces
  `(a:1,2,"A","C","E","G")`. Also accepts an optional leading `via` key-lambda
  (`(sorted: via its name, ...$creatures)`), which ours doesn't implement at
  all. See `ts/macrolib/datastructures.ts` (the `(sorted: [Lambda], ...Any)`
  signature and its mixed-value example).
- **Trigger**: `(sorted: 'C', 2, 'A', 1)`
- **User-visible**: The reference's own documented mixed-value example errors in
  ours; any array mixing numbers and strings can't be sorted.

### 15. `(folded:)` ignores a `where` filter clause

- **Ours**: `EvalFold` honours only `making` + `via` and never consults
  `WhereClause`. Worse, the parser can't even produce a fold lambda carrying a
  `where`: `ParseLambdaTail` requires `via` immediately after `making _acc` and
  doesn't accept a trailing `where`, so
  `(folded: _item making _total via _total + _item where _item > 0, …)` leaves
  the `where` stranded and `ParseBinary` re-enters `ParseLambdaTail` with the
  lambda as `leftAsParam`, throwing "lambda parameter must be a variable". See
  `HarloweParser\Runtime\LambdaInvoker.cs` (`EvalFold`) and
  `HarloweParser\Parsing\HarloweExpressionParser.cs` (`ParseLambdaTail` clause
  ordering). (This is the missing-`where` aspect; the lambda family's arg-shape
  and `pos` binding were verified separately — see Confirmed non-divergences.)
- **Reference**: A `where` clause filters the fold — a filtered item leaves the
  accumulator unchanged (the lambda returns `null` and the reduce keeps the
  prior `making` value). `(folded: _item making _total via _total + _item where
  _item > 0, 0, ...$arr)` sums only the positive items. Note: as of 3.3.6 the
  `where` clause does not apply to the first (seed) value. See
  `ts/macrolib/datastructures.ts` (the `(folded:)` doc's "where" paragraph and
  the `null`-filter branch in its `reduce`).
- **Trigger**: `(folded: _item making _total via _total + _item where _item > 0, 0, ...$arr)`
- **User-visible**: A `where`-filtered fold (a documented idiom) raises an
  in-prose parse error in ours instead of summing the filtered subset.

---

## Low-severity divergences

### 16. `(random:)` errors on fractional bounds — ✅ FIXED (2026-06-02)

**Resolved.** `TryAsBound` now truncates a fractional bound toward zero instead
of rejecting it, matching reference's `parseInt` argument coercion — so
`(random: 1.5, 6.5)` behaves like `(random: 1, 6)` and `(random: -1.9, -1.1)`
yields `-1` (toward zero, not floor). NaN/infinity and out-of-Int32 bounds still
error (messages reworded "whole-number" → "number"). See `RandomMacro.cs` and the
`Random_*Fractional*` / `RandomFractionalBound_*` tests. (Reference's `!b===0`
one-arg quirk for a literal `0` second bound is intentionally not reproduced; our
`args.Count` distinction is cleaner and isn't part of this finding.) Original
finding below.

- **Ours**: `TryAsBound` rejects fractional values explicitly
  (`d != Math.Truncate(d) → error`). See
  `HarloweParser\Runtime\Macros\RandomMacro.cs`.
- **Reference**: Coerces each bound via JS `parseInt` — silently truncates
  fractional bounds to integers. See `ts/macrolib/values.ts` (search
  `random: [(a:number, b:number) =>` and `parseInt`).
- **Trigger**: `(random: 1.5, 6.5)` — ours errors; reference returns an
  integer in [1,6].
- **User-visible**: Author who passes fractional bounds gets an in-prose
  error in ours, where reference would just truncate. Docs say "whole
  number", so ours is arguably stricter rather than wrong.

---

### 17. `(load-game:)` loop guard isn't cleared by an in-passage `(goto:)`

- **Ours**: the infinite-loop guard (a `(load-game:)` reached while rendering a
  just-loaded passage errors with *"I can't use (load-game:) immediately after
  loading a game."*) clears on a navigation to a **new turn** — a host-driven
  `StorySession.Goto` (link click) or any undo/redo/load restore. An *in-passage*
  `(goto:)` is an intra-turn **redirect** in our model (decision 7 of
  `SAVE-LOAD-PLAN.md`: auto-followed `(goto:)` maps to reference's `redirect()`,
  not `play()`), so it does **not** clear the guard — `load → (goto:) → load`
  within one turn is suppressed (the second `(load-game:)` errors).
- **Reference**: `section.loadedGame` is scoped to a single `showPassage`
  (`ts/engine.ts`: set from the `{loadedGame:true}` display option on the load's
  re-show, then `section.loadedGame = false` right after the render). Reference's
  `(go-to:)` is a fresh `showPassage`, so it clears the guard and
  `load → (go-to:) → load` is permitted.
- **User-visible**: only differs in the narrow case of a just-loaded passage that
  reaches another `(load-game:)` through an in-passage `(goto:)` in the *same*
  turn — ours errors, reference reloads. Host navigation (the normal player flow)
  clears the guard identically. A direct consequence of the documented intra-turn
  `(goto:)` model, not an independent bug.

---

## Confirmed non-divergences

For future-me / future contributors — these were checked and match reference
in arg-count, type, error path, and edge cases. Don't re-audit unless
something changes.

- `(set:)`, `(put:)`, `(print:)`
- `(a:)` / `(array:)`
- `(either:)`
- `(modulo:)` (and the new `%` operator)
- `(opacity:)`, `(font:)`
- `(text-color:)` / `(text-colour:)` / `(color:)` / `(colour:)` family
- `(text-style:)` — reset semantics (`"none"`) and unknown-name error path
- Lambda-consuming family (`(find:)`, `(altered:)`, `(folded:)`, etc.) — arg
  shape and `pos` binding (already verified in the lambda-pos slice). Caveat:
  `(folded:)`'s missing `where`-clause filter is a real divergence — see #15.
- `(history:)` — known divergence filed for save-model slice in
  `SAVE-LOAD-PLAN.md`; not re-flagged here

---

## Recommended ordering

If tackled as individual slices, the high-severity items group naturally by
size:

**Small contained fixes** (1–2 hours each, isolated changes):

- ~~`(goto:)` validation (#1) — one method, one branch.~~ ✅ done.
- ~~`(elseif:)` registration (#2) — new macro class + register call.~~ ✅ done.
- ~~`(else:)` stray-use error (#6) — one branch in `ElseMacro`.~~ ✅ done.
- ~~`(dm:)` duplicate-key error (#8) — one branch in `DmMacro`.~~ ✅ done.
- ~~`(text:)`/`(string:)` variadic + alias (#9) — minor refactor + register.~~ ✅ done.
- ~~`(for:)` zero-item + `(loop:)` alias (#10) — `MinArgs` change + register.~~ ✅ done.
- ~~`(num:)` semantics + `(number:)` alias (#12) — refactor + register.~~ ✅ done.
- ~~`(align:)` regex-based parsing (#13) — replace the four-string lookup.~~ ✅ done.
- `(sorted:)` mixed-type ordering (#14) — relax the `item.Kind != kind` check;
  sort numbers ahead of strings in one comparer.
- ~~`(random:)` fractional truncation (#16) — relax the `TryAsBound` check.~~ ✅ done.

Total: ~10 small slices, sized appropriately for a single PR each, no shared
architecture.

**Medium architectural touches** (interact with deferred slices):

- `(if:)` / `(unless:)` returning Changer (#3) — touches `BodyRenderer`'s
  conditional path. Worth doing alongside the next conditional-related work.
- `(text-size:)` accepting Measurement (#7) — depends on whether we want a
  proper `Measurement` value type (parallel to the broader missing-value-types
  TODO) or just expand the macro to parse measurement strings inline.
- `(replace:)`/`(append:)`/`(prepend:)` variadic (#11) — small but touches
  `RevisionChangers.Build` interface for multiple targets.
- `(folded:)` `where`-clause support (#15) — `ParseLambdaTail` must accept a
  trailing `where` after `making … via …`, and `EvalFold` must skip filtered
  items (keep the prior accumulator), excluding the seed value per 3.3.6.

**Blocked on deferred slices**:

- `(change:)`/`(enchant:)` string targets + via-lambda (#4) — string-target
  enchantment is on the candidate-next-slices list ("String-target
  click/hover") and naturally extends to here.
- `(click:)` family expansion (#5) — same parent slice as #4. The
  `(click-rerun:)` macro is also part of this.

---

## Critical files (quick reference)

- `E:\Git\twinelike\HarloweParser\Runtime\Macros\` — all our macro
  implementations, one class per file.
- `E:\Git\twinelike\HarloweParser\Runtime\Macros\StandardMacros.cs` —
  macro registration; the `RegisterAll` site for new aliases.
- Reference macros live under `ts/macrolib/`, grouped by category
  (`commands.ts`, `datastructures.ts`, `stylechangers.ts`,
  `enchantments.ts`, `values.ts`, etc.). Fetch them from the
  `Codaea/harlowe-branch-default-2` GitHub mirror via `gh api` (Heptapod
  itself is Anubis bot-walled) — see CLAUDE.md for the command and the
  unofficial-mirror caveat. For local grep access instead, unzip a `branch
  default` snapshot to `references/harlowe-branch-default.zip` (**not**
  committed — third-party Zlib source, gitignored).
