# Macro Semantics Divergences vs Reference Harlowe

Audit output from the divergence pass that originally hit token limits in the
first attempt and was re-run with a narrower scope. Findings are the gaps
between our macro implementations and reference Harlowe's, scoped to
**user-visible behavioural differences** — cosmetic and internal-implementation
divergences are skipped.

Already-fixed or already-filed items are excluded from this list — see
`CLAUDE.md` `Known TODOs` for the standing tracking list and `SAVE-LOAD-PLAN.md`
for the save-model slice (which lands `(history:)` semantics).

## Counts

- **High severity (5)**: silent wrong result or breaks documented Harlowe idioms.
- **Medium severity (8)**: error-message divergence, missing feature an author would expect, or rare-case wrong result.
- **Low severity (1)**: documented as deliberate or marginal.

---

## High-severity divergences

### 1. `(goto:)` doesn't validate target exists

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

### 2. `(elseif:)` / `(else-if:)` not registered

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

### 6. `(else:)` silently no-ops with no preceding conditional

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

### 8. `(dm:)` silently overwrites duplicate keys

- **Ours**: Duplicate keys silently overwrite (`map[key] = value`). See
  `HarloweParser\Runtime\Macros\DmMacro.cs`.
- **Reference**: Errors with "You used the same data name (X) twice in the
  same (datamap:) call." See `ts/macrolib/datastructures.ts` (search
  `([`datamap`,`dm`],` and `map.has(key)`).
- **Trigger**: `(dm: "hp", 10, "hp", 5)`
- **User-visible**: Reference flags the bug; ours produces `{hp:5}`, masking
  the lost first value.

### 9. `(text:)` is unary; `(string:)` alias missing

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

### 10. `(for:)` requires ≥1 item; `(loop:)` alias missing

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

### 12. `(num:)` accepts Number passthrough; `(number:)` alias missing

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

### 13. `(align:)` only recognises four arrow forms

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

---

## Low-severity divergences

### 14. `(random:)` errors on fractional bounds

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
  shape and `pos` binding (already verified in the lambda-pos slice)
- `(history:)` — known divergence filed for save-model slice in
  `SAVE-LOAD-PLAN.md`; not re-flagged here

---

## Recommended ordering

If tackled as individual slices, the high-severity items group naturally by
size:

**Small contained fixes** (1–2 hours each, isolated changes):

- `(goto:)` validation (#1) — one method, one branch.
- `(elseif:)` registration (#2) — new macro class + register call.
- `(else:)` stray-use error (#6) — one branch in `ElseMacro`.
- `(dm:)` duplicate-key error (#8) — one branch in `DmMacro`.
- `(text:)`/`(string:)` variadic + alias (#9) — minor refactor + register.
- `(for:)` zero-item + `(loop:)` alias (#10) — `MinArgs` change + register.
- `(num:)` semantics + `(number:)` alias (#12) — refactor + register.
- `(align:)` regex-based parsing (#13) — replace the four-string lookup.
- `(random:)` fractional truncation (#14) — relax the `TryAsBound` check.

Total: ~9 small slices, sized appropriately for a single PR each, no shared
architecture.

**Medium architectural touches** (interact with deferred slices):

- `(if:)` / `(unless:)` returning Changer (#3) — touches `BodyRenderer`'s
  conditional path. Worth doing alongside the next conditional-related work.
- `(text-size:)` accepting Measurement (#7) — depends on whether we want a
  proper `Measurement` value type (parallel to the broader missing-value-types
  TODO) or just expand the macro to parse measurement strings inline.
- `(replace:)`/`(append:)`/`(prepend:)` variadic (#11) — small but touches
  `RevisionChangers.Build` interface for multiple targets.

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
- `C:\temp\harlowe-ref\harlowe-branch-default\ts\macrolib\` — reference
  macros, grouped by category (`commands.ts`, `datastructures.ts`,
  `stylechangers.ts`, `enchantments.ts`, `values.ts`, etc.). Unzip
  `references/harlowe-branch-default.zip` first; or use the in-repo zip
  directly via `unzip -p`.
