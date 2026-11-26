# Macro Semantics Divergences vs Reference Harlowe

The gaps between our macro implementations and reference Harlowe's, limited to
**user-visible behaviour**; cosmetic and internal differences are left out.
Fixed entries stay, marked ✅, so every number stays stable. Open bugs and the
roadmap are in `TODO.md`, and deliberate differences between Harlowe majors are
in `COMPATIBILITY.md`.

## Counts

- **High severity (0 active, 6 fixed)**: silent wrong result or breaks documented Harlowe idioms.
- **Medium severity (0 active, 10 fixed, 1 reclassified)**: error-message divergence, missing feature an author would expect, or rare-case wrong result.
- **Low severity (23 active, 4 fixed)**: documented as deliberate or marginal.

**23 active, all low-severity** — #26 (deliberate: a printed array renders each
item separately, so markup can't span items), #17 (load-guard vs intra-turn `(goto:)`), #18
(stacked-interaction nesting order), #21 (deliberate: out-of-range array sets
error rather than growing sparse holes), #23 (deliberate: a `via`-lambda's RNG
draw is rewound per match so the passes stay idempotent), #24 (deliberate: the
`even`/`odd` datatypes require a Number rather than coercing one), #25
(deliberate: the `lowercase`/`uppercase` datatypes don't expand under case
mapping, so `ß` is neither), #28 (deliberate: custom macro recursion stops at
this library's stack-safe limits), #29 (deliberate: `(partial:)` doesn't copy
the target's signature), #30 (deliberate: `(for:)+(output:)` works), #31
(deliberate: markup in a code hook can't pair with markup outside the macro),
#32 (a revision in a macro body whose target comes later does nothing), #33
(a revision, `(for:)` or link changer attached to `(show:)` has no effect), #34
(natural order is approximate for accented text and hex strings),
#35 (deliberate: limits on `(for:)` passes, `(range:)` length, print depth and live hooks),
#36 (a run of spaces in a hand-edited tags attribute adds no empty tags), #38 (a
hook hiding itself, or copied while hidden, can come back different), #39
(`time` moves by frames),
#41 (a revision's later copies aren't there yet while an earlier copy runs),
#42 (overlapping revision targets run the content twice in either order), #43
(`(for:)` with a click or hover changer is an error), #44 (a list inside `{…}`
or formatting inside a list item keeps the rest of the item), #45 (an unpaired
`<name|` shows as text, where the browser drops it as a tag).

Each section lists its entries in number order; #27 was never used. "Reclassified" means the
finding was real but misattributed — #7 recorded a Harlowe 4.0 behaviour as a
gap against our Harlowe 3 target; it lives on as a compatibility switch in
`COMPATIBILITY.md`. When auditing against the 4.0-unstable snapshot, always ask
which major a behaviour belongs to before filing it here.

---

## High-severity divergences

### 1. `(goto:)` doesn't validate target exists — ✅ FIXED (2026-06-01)

**Resolved.** `GotoMacro` now checks `MacroContext.PassageExists` (wired by
`StorySession` to the story's passage lookup) before recording the goto; a
missing target surfaces `I can't (goto:) to the passage 'X' because it doesn't
exist.` instead of silently navigating to a blank result. The check is skipped
when no story is wired (standalone renderer tests leave `PassageExists` null),
preserving the bare record-the-goto behaviour there. See `GotoMacro.cs`,
`MacroContext.PassageExists`, and the `PendingGoto_MacroToMissingPassage_*` tests.

*Amended 2026-07-12:* this note used to add that the host `StorySession.Goto(name)`
API "still returns an empty result for an unknown name (that's the host's explicit
request, not an authoring mistake)." That reasoning was wrong, and #19 is what it
cost: an empty result still advanced the timeline onto the nonexistent name, which
went into the save and made it unloadable. `Goto` now refuses. Original finding below.

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

### 3. `(if:)` / `(unless:)` returns Bool instead of Changer — ✅ FIXED (2026-07-03)

**Resolved.** All four conditionals now return Changers, matching reference
(`ts/macrolib/stylechangers.ts`: `new Changer(`if`, [expr])` with apply
`(d, expr) => {d.enabled &&= expr}`). A `ConditionalPatch` ANDs its decision
into the new `HookDescriptor.Enabled`; a disabled descriptor suppresses the
whole application (styles, iteration, revision, interaction — so
`(if: false) + (click: ?a)` arms nothing). `(else:)`/`(else-if:)` read the
pairing at *call* time and bake the decision (reference:
`new Changer(`else`, [lastHookShown === false])`), pre-stamped with the
equivalent `(if:)` source so a stored one survives save/load. The `(else:)`
pairing moved from macro-invoke time to hook-application time
(`BodyRenderer.UpdateConditionalPairing`, reference `section.ts`'s
`lastHookShown` rules: shown → true; hidden → false only when the applying
expression's *front macro name* is if/unless/else — an `(else-if:)` hide, or
one from a stored changer in a variable, preserves the prior value; attached
booleans hide/show the hook and always write the pairing; an unattached
changer in prose errors) — which also fixed a latent bug where a conditional
nested in `(set:)` args corrupted the pairing. The old trigger `(set: $c to (if: $cond) + (text-style:
"bold"))$c[content]` now renders bold-iff-condition. See the "Conditional
changers" tests in `BodyRendererTests`, the composition tests in
`InteractionMacroTests`, and `ConditionalChanger_*`/`ElseChanger_*` in
`SaveSerializerTests`.

### 4. `(change:)` / `(enchant:)` reject string targets and `via`-lambdas — ✅ FIXED (2026-07-04)

**Resolved.** Both macros now match reference's signature
`[either(HookSet, String), either(Changer, Lambda.TypeSignature('via'))]`
(`ts/macrolib/enchantments.ts`). A string target's occurrences are wrapped as
addressable hooks per pass (reusing `TextOccurrenceFinder` from string-target
revision); persistent wraps carry `RenderHookNode.SourceEnchantment` so the
disenchant sweep unwinds them before re-matching — the pass stays idempotent
across dispatches. A `via` lambda is evaluated per match with `pos` bound
1-based and must produce an enchantable changer; a failure replaces that match
with the in-prose error and ignores the rest of the scope (reference's
`enchantScope` in `ts/internaltypes/enchantment.ts`). Completely empty hooks
are skipped and don't advance `pos` (reference's `:empty` check). Also landed
reference's `notRevisionChanger` gate: a changer carrying a revision or
interaction patch — `(replace:)`, `(click:)`, … — now errors instead of
silently dropping the patch (`Changer.CanEnchant`). Lambda evaluation restores
`PendingGoto`/`PendingLoad`, so a `(goto:)` reached inside a via-lambda can't
clobber a dispatch's queued navigation. See `EnchantmentMacroSupport`,
`EnchantmentPass.Apply`, and the string-target / via-lambda / error tests in
`EnchantMacroTests`. Original finding below.

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

### 5. `(click:)` family rejects string targets and second-arg changers — ✅ FIXED (2026-07-07)

**Resolved.** All thirteen interaction changers now match reference's shared
signature `[either(HookSet, String), optional(either(Changer,
Lambda.TypeSignature('via')))]` (`newEnchantmentMacroFns` in
`ts/macrolib/enchantments.ts`). A string target's occurrences are wrapped as
armed regions per `InteractionPass` (reusing `TextOccurrenceFinder`; wraps
carry `RenderHookNode.SourceRegionId` so `StripWraps` unwinds them — the
interaction mirror of the enchant pass's disenchant tag), an empty string
errors with reference's *"A string given to this (click:) macro was empty."*,
and empty hooks are never armed (reference's `:empty` filter). The optional
second argument styles the *armed* region: a changer (gated by the same
`notRevisionChanger` check as `(enchant:)`) or a `via`-lambda evaluated per
non-empty match with 1-based `pos` through the shared
`EnchantmentPass.EvaluateViaLambda` (failure replaces the match with the
in-prose error, reference's `enchantScope` path). `(click-rerun:)` registers
with `once: false` — click only, as in reference — staying armed and
re-rendering its hook over the previous run's content each activation.

Fixing the family surfaced two adjacent divergences in the dispatch model,
both re-aligned to reference in the same slice:

- **Plain `(click:)`/`(mouseover:)`/`(mouseout:)` now reveal the attached hook
  at the macro's own position** (an anonymous anchor node planted at apply
  time — reference's hidden attached-hook element; the target just loses its
  armed styling), instead of behaving like `(click-replace:)`. Reference test:
  `[cool]<foo|(click:?foo)[beans]` → click → `coolbeans`. Only the
  `-replace`/`-append`/`-prepend` combos splice into the target
  (`enchantDesc.rerender`).
- **Composed styles apply to the revealed/spliced content, not the armed
  region** — `(text-style:"bold")+(click: ?a)[x]` reveals a bold `x`
  (reference applies the descriptor's styles at the event's `renderInto`);
  the armed region is styled by the new second argument instead.

The adjacent *command* variants — `(click-goto:)`/`(click-undo:)` and the
hover mirrors, no attached hook, registered by `Macros.addCommand` over the
same enchant machinery — shipped separately (2026-07-10):
`InteractionCommandMacro` registers a persistent `Interaction` directly
(goto: passage existence-checked at call time; undo: reference's *"I can't
(undo:) on the first turn."*), and `DispatchEvent` navigates via
`Goto`/`Undo`+re-render instead of filling anything (reference's
`enchantDesc.goto`/`undo` branches). Only the Harlowe 3.3 `doubleclick`
interaction type remains unimplemented. See `InteractionMacros.cs`,
`Interaction.cs` (`InteractionPass`), `StorySession.DispatchEvent`, and the
string-target / second-arg / rerun / reveal / command tests in
`InteractionMacroTests`. Original finding below.

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

### 19. Links to nonexistent passages are live, and following one poisons the save — ✅ FIXED (2026-07-12)

**Resolved.** A link is existence-checked when it *renders*, in
`BodyRenderer.Visit(LinkNode)`, through the same `MacroContext.PassageExists` gate
`(goto:)` uses (skipped when no story is wired, so standalone renderer tests are
unaffected). `(link-goto:)` returns a command that renders through the same method,
so a link stored in a variable is checked when it's printed. A dangling target renders its label
as plain prose plus an in-prose error and emits **no `Link` event at all**, so the
bug is closed structurally: there is nothing for a host to make clickable.
`StorySession.Goto` refuses an unknown passage outright — returning the current
view with an error appended, before touching a single field — so a host passing an
arbitrary string, or clicking a link whose target was removed through the editing
API between render and click, still can't get a nonexistent name into the timeline
and from there into an unloadable save. `HarloweStory.GetBrokenLinks()` was added
alongside as the authoring/CI check (it reads the `Branches` inventory
`BranchCollector` already derives, so it costs an index lookup).

**Deliberate divergence recorded:** reference renders an unclickable red
`<tw-broken-link>` and no error; we render the label plus an error. We have no HTML
to hand an engine-agnostic host, and a dedicated broken-link render channel wasn't
worth the surface. The un-navigable half is reproduced exactly; the *visible* half
rides the error channel, which hosts already silence in production builds if they
want reference's quieter presentation. Note this also makes us **stricter** than
reference in one place: reference shows a broken link silently, we report it.

See `BrokenLinkTests.cs` — in particular
`DeadLink_Clicked_Saved_ThenLoaded_Survives`, which walks the whole original chain
(dead link → click → save → load) and fails on the pre-fix code. Original finding
below.

- **Ours**: nothing checks that a link's target exists.
  `BodyRenderer.Visit(LinkNode)` is `_output.Link(node.Text, node.Target)` flat,
  and `LinkGotoMacro` never consults `MacroContext.PassageExists` — so
  `[[Go->Missing]]` and `(link-goto: "Click", "Nowhere")` both emit an ordinary,
  clickable `Link` event that the host wires to `StorySession.Goto`. `Goto` then
  doesn't validate either: it calls `EnterPassage`, finds no passage, and returns
  `EmptyResult` — **zero entries, no `Error`, a silently blank screen** — while
  leaving `CurrentPassage` and the present `Moment.PassageName` set to the name
  that doesn't exist. `SaveGame` happily serialises that Moment; `DeserialiseTimeline`
  then validates every passage on load, so the blob is rejected **forever** with
  *"saved passage 'Missing' no longer exists"*. A dead link therefore turns into a
  permanently unloadable save. (`(goto:)` and `(click-goto:)` *do* validate — this
  is the un-validated half of the family.)
- **Reference**: `Passages.hasValid(passage)` gates every link.
  `ts/macrolib/links.ts` (search `Since the passage isn't available`) emits
  `<tw-broken-link passage-name="…">text</tw-broken-link>` instead of `<tw-link>`
  — documented as *"a broken link (a red link that can't be clicked) will be
  created"*. The `[[…]]` syntax gets the same treatment: the same file notes
  *"the Harlowe engine actually converts all standard links into (link-goto:)
  macro calls internally — the link syntax is, essentially, a syntactic shorthand
  for (link-goto:)"*. The click never happens, so the rest of the chain can't.
- **Trigger**: `[[Go->Missing]]`, click it, `(save-game: "1")`, reload. Reached in a
  real story either by a typo'd target or by `RemovePassage` orphaning a link under
  a live session — which is how the fix was driven end to end. (An earlier draft of
  this entry claimed `TestFiles/testFile.html` ships dangling links; it doesn't —
  `GetBrokenLinks()` reports it clean, and the note in `CLAUDE.md` that said
  otherwise has been corrected.)
- **User-visible**: dead links look and behave like live ones; following one blanks
  the passage with no diagnostic; any save taken afterwards can never be loaded.

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

### 7. `(text-size:) / (size:)` requires Number-then-em — ✅ NOT A DIVERGENCE (reclassified 2026-07-19)

**Misfiled: this is a 3→4 language change, not a gap.** The original entry read
its "Reference" behaviour off `ts/macrolib/stylechangers.ts` in the pinned
**4.0-unstable** snapshot without asking which major that behaviour belonged to.
The measurement datatype is *new in 4.0* — its changelog introduces it under
Additions → Coding and says the listed changers now accept it "in place of their
former number-based values". In Harlowe 3, which is the version this library
targets, `(text-size:)` takes a plain Number scale multiplier: the bundled
`harlowe3Docs.html` gives the signature as `(text-size: Number)` and its own
examples are `(change: ?passage, (text-size: 0.6))`. Ours takes a single Number
and renders it as an `em` scale — which is that behaviour. So we match 3.3.9 on
all three points the entry called out (Number-only, `em` scaling, no second
argument).

It is therefore a **compatibility switch, not work**: row 10 of
`COMPATIBILITY.md`, to be implemented if and when V4 stories are supported —
along with the rest of the measurement surface (`(border-size:)`,
`(corner-radius:)`, `(text-indent:)`, `(box:)`, `(scroll:)`), most of which is
unimplemented here anyway. The units are `px`/`em`/`rem`/`Lh` plus the
dimension-declaring `w`/`h`, and measurements do arithmetic (`2em - 10px`,
`50px * 2`), so it wants a real value type with operator support rather than
string parsing.

*Confidence caveat:* verified against the Harlowe 3 documentation (signature
index + worked examples), not 3.x macro source — the pinned snapshot only
carries 4.0 source. Confirm against a v3.3.9 snapshot when one is pinned (see
`TODO.md`'s compatibility-profiles entry).

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

### 11. `(replace:) / (append:) / (prepend:)` are unary — ✅ FIXED (2026-07-15)

**Resolved.** The three macros are variadic (`MaxArgs = -1`), matching
reference's `rest(either(HookSet, String))`: each argument becomes one
`RevisionSpec` (a `{target, mode}` pair, reference's newTarget), and an empty
string errors with reference's exact wording (`A string given to this
(replace:) macro was empty.`) instead of silently matching nothing. The
descriptor model was aligned in the same change: `HookDescriptor.Revision`
became a `Revisions` *list* that `RevisionPatch` appends to with reference's
duplicate-(target, mode) filter — `desc.newTargets.push(...)` in
`ts/macrolib/enchantments.ts` — which also fixed composed revision changers:
`(replace: ?a)+(replace: ?b)` used to last-win, now both splice, and
`(append: ?a)+(prepend: ?b)` carries each target's own mode on one descriptor.
`RunRevision` renders the source once and splices a clone per match, per spec,
in argument/composition order. See the "Variadic targets" and "Composed
revision changers" tests in `RevisionMacroTests`. Original finding below.

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

### 14. `(sorted:)` rejects mixed number+string input — ✅ FIXED (2026-07-11)

**Resolved.** `SortedMacro` sorts mixed numbers+strings (numbers numerically
ahead of strings ordinally — reference's own `(a:'A','C','E','G',2,1)` →
`(a:1,2,"A","C","E","G")` example now reproduces), accepts the optional
leading `via` key-lambda through the existing `LambdaInvoker.EvalTransform`
(keys must evaluate to numbers or strings; the values themselves may be any
kind; equal keys keep their given order via an index tie-break — reference
documents the sort as stable), validates the lambda is `via`-only (reference's
"must be a 'via' lambda" error), and returns an empty array for zero values
(reference's 3.3.0 behaviour; `MinArgs` 0). Remaining deliberate divergences,
documented in the macro docstring: string keys order ordinally rather than by
reference's locale-collated alphanumeric NaturalSort (`ts/utils/naturalsort.ts`),
and — since reference stringifies numbers into that same sort — a numeric
string like `"1"` interleaves with numbers there but sorts with the strings
here. See the `Sorted_*` tests in `LambdaMacroTests`. Original finding below.

- **Ours**: Requires every value to share the first value's kind; a mixed-kind
  input errors with "(sorted:) can't compare values of different types".
  Numbers sort numerically, strings via `string.CompareOrdinal`. (The
  docstring has since been rewritten to document this divergence honestly —
  2026-07-07 audit; the behaviour itself is unchanged.) See
  `HarloweParser\Runtime\Macros\SortedMacro.cs` (the `item.Kind != kind`
  check). (The ordinal-vs-alphanumeric string ordering is a
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

### 15. `(folded:)` ignores a `where` filter clause — ✅ FIXED (2026-07-15)

**Resolved.** Both halves landed. Parser: `ParseLambdaTail` accepts a trailing
`where` after the `via` body (the clause parses at order 14, so the keyword
survives to the tail) — covering the fold-filter shape `_item making _total
via _total + _item where _item > 0` and, for free, `via … where …` on plain
transform lambdas. Runtime: `EvalFold` evaluates the `where` clause under the
same bindings as the via body (item + `making` accumulator + `it` + `pos` —
so a filter can consult the running total), and a false result returns the
accumulator unchanged, reference's null-filter branch in `(folded:)`'s
`reduce` (`ts/macrolib/datastructures.ts`). The seed stays exempt per 3.3.6 —
it never becomes a loop value, so it never reaches the filter. A non-Bool
`where` result errors, as for predicate lambdas. `MarkupPrinter` prints a fold
lambda's `where` after the via body (the only order that reparses, since
`making` must be followed directly by `via`); non-fold lambdas keep the
`where … via …` order. See the `Folded_WhereClause_*` tests in
`LambdaMacroTests` and the fold round-trip pins in
`MarkupPrinterRoundTripTests`. Original finding below.

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

### 20. String targets don't bestride text nodes — ✅ FIXED (2026-07-15)

**Resolution.** `TextOccurrenceFinder` was rebuilt as a direct port of
reference's `findTextInNodes` + `wrapTextNodes` pair: matching runs over the
tree's *flattened* text-node stream in document order (so a needle can span
inline formatting, styled spans, links, and hooks), boundary nodes are split
around each hit, and — the key discovery that kept the fix contained — every
fragment of one match moves into a **single** `RenderHookNode` placed at the
first fragment's position, exactly like reference's jQuery
`wrapAll(<tw-pseudo-hook>)`, whose rehoming persists after the unwrap. One
match = one wrap, so the consumer contract (revision splices, enchant tagging,
interaction arming, strip/disenchant sweeps, dispatch-time region lookups)
needed **zero changes**; the entire fix sits inside the finder. Consequences
mirrored from reference and pinned in tests: the trigger case now replaces
(`Say ''hello'' friend` + `(replace: "hello friend")[X]` → `Say X`, with X
rendering *inside* the bold span — the match relocated to the first
fragment's home), a wholly-spanned styled word leaves its emptied style span
behind, scanning resumes after each match, and the enchant/interaction passes
stay idempotent because the rehomed text is contiguous on re-find. See the
"bestriding" tests in `RevisionMacroTests` / `EnchantMacroTests` /
`InteractionMacroTests`. Original report below.

---

- **Ours**: `TextOccurrenceFinder` matches a needle **within a single
  `RenderTextNode`** and says so in its own docstring. Adjacent prose coalesces
  into one node (so a `(print:)` in the middle is fine), but anything that opens a
  child container — inline formatting, a styled span, a link, a hook — splits the
  prose into siblings, and a needle spanning the boundary is silently not found.
  Affects every string-target consumer: `(replace:)`/`(append:)`/`(prepend:)`,
  `(enchant:)`/`(change:)`, and the whole `(click:)`/`(mouseover:)`/`(mouseout:)`
  family.
- **Reference**: `findTextInNodes` in `ts/utils/renderutils.ts` accumulates
  `examinedNodes`/`examinedText` across consecutive text nodes precisely so a match
  can straddle them — its header comment is *"to allow transformations of exact
  textual matches within passage text, **regardless** of the actual DOM hierarchy
  which those matches bestride"* — and splits the run back apart around the hit.
  (Matching is exact/case-sensitive in both, so that half already agrees.)
- **Trigger**: `Say ''hello'' friend(replace: "hello friend")[X]` — reference
  replaces, we render `Say hello friend` unchanged and emit no error. The control
  `Say hello friend(replace: "hello friend")[X]` works, so the failure is invisible
  until an author adds emphasis inside the phrase.
- **User-visible**: a string-target macro silently no-ops whenever the phrase
  contains markup. Newly reachable: the inline-formatting slice (`''bold''` etc.)
  landed the most common way to split a phrase.
- **Fix**: rework `TextOccurrenceFinder` to walk a flattened text-node run (the
  `findTextInNodes` model) and split across nodes, instead of scanning each
  `RenderTextNode` in isolation.

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
  `docs/plans/SAVE-LOAD-PLAN.md`: auto-followed `(goto:)` maps to reference's `redirect()`,
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

### 18. Stacked interactions on one hook nest in reverse order (found 2026-07-07 audit)

- **Ours**: When several `(click:)`-family macros target the same hook, each
  pass wraps the container's current content in turn, so the *last* macro's
  region ends up outermost — `|f>[A](click:?f)[1](click:?f)[2]` flushes as
  `Begin(r-1) Begin(r-0) … End End`. Both regions fire correctly and, fired in
  passage order, accumulate exactly as reference does (`A1`, then `A12`); only
  the nesting — and therefore which region a host that fires
  "outermost-first" picks — diverges. See the wrap loop in
  `InteractionPass.Update`.
- **Reference**: `<tw-enchantment>`s wrap "outward-to-inward first-to-last",
  and the shared event handler sorts by `compareDocumentPosition` to execute
  the outermost first — so stacked interactions activate in passage order,
  top to bottom (`generalEnchantmentEvent` in `ts/macrolib/enchantments.ts`;
  pinned by the "multiple enchantments are triggered in order" spec).
- **Trigger**: `[A]<foo|(click:?foo)[1](click:?foo)[2](click:?foo)[3]` — click
  the text three times.
- **User-visible**: only through the host's choice of which nested region a
  click on the shared text reports; a host that dispatches the outermost
  region first will fire ours in reverse passage order. Reversing our wrap
  loop (or documenting "innermost = first macro" as the host contract) would
  align it.

---

### 21. Out-of-range array property-set errors instead of growing a sparse array (deliberate; filed with the property-assignment slice, 2026-07-18)

- **Ours**: an array property-set allows indices 1..Count (replace) plus
  Count+1 (append), and errors beyond — `(set: $arr's 5th to 1)` on a 3-item
  array is `array index 5 is out of range (1..4)`. See the final-step bounds
  rule in `ExpressionEvaluator.ResolveWritePath`.
- **Reference**: `objectOrMapSet` in `ts/internaltypes/varref.ts` is a plain JS
  `obj[prop] = value`, so setting past the end grows a **sparse array with
  holes** — positions that error on read (*"an empty variable"*) and break
  reference's own serialisation (`(save-game:)` on a holed array fails).
- **Trigger**: `(set: $arr to (a: 1, 2, 3))(set: $arr's 5th to 9)` — reference
  silently creates a hole at 4th; we error.
- **User-visible**: only for out-of-range writes, which produce degenerate
  state in reference. Deliberate: holes aren't representable in
  `List<HarloweValue>` and reproduce a reference behaviour that's broken on
  its own terms. The append case (Count+1, reference's no-hole growth) is
  reproduced exactly.

### 22. `(set: … into …)` / `(put: … to …)` accept the wrong operator — ✅ FIXED (2026-07-18)

**Resolved by the (move:) slice**, via a third mechanism neither of the
recorded fix ideas needed: `ExpressionEvaluator.ValidateAssignmentOperators`,
an evaluation-time pre-pass run at both macro-invocation sites
(`BodyRenderer.Visit(MacroNode)` and the evaluator's `Visit(MacroCallNode)`).
It mirrors reference's typeChecker model — every argument's operator is
checked BEFORE any argument evaluates, so `(set: $a to 1, 5 into $b)` assigns
nothing at all. Wordings are reference's (`Please say 'to' when using the
(set:) macro.`, `Please say 'into' when using the (move:) macro.`). (A trim
recorded here originally — ours named (put:) alone while (unpack:) was
unimplemented — was reverted when the (unpack:) slice shipped later the same
day: the message is reference-verbatim again, and the pre-pass also carries
reference's XOR dest-shape gate.) The same pre-pass
also rejects a non-assignment argument to the three macros (`(set: 5)`,
previously a silent no-op — reference rejects it in the AssignmentRequest
type signature). See the `ValidateOperators_*` tests in
`ExpressionEvaluatorTests` and the operator-discipline tests in
`BodyRendererTests`. Original finding below.

- **Ours**: the parser gates `to`/`into` to `(set:)`/`(put:)` arg-tops as a
  *pair* — `HarloweExpressionParser.IsAssignmentMacro` doesn't distinguish
  which — so `(set: 5 into $x)` and `(put: $x to 5)` parse and assign
  successfully.
- **Reference**: `(set:)` checks each AssignmentRequest's operator and errors
  `Please say 'to' when using the (set:) macro.`; `(put:)`/`(unpack:)` error
  `Please say 'into' when using the (put:) or (unpack:) macro.` See the
  `ar.operator` checks in `ts/macrolib/commands.ts`.
- **Trigger**: `(set: 5 into $x)` — reference errors, ours assigns 5 to $x.
- **User-visible**: marginal — the assignment itself lands correctly; only the
  style-guiding error is missing. Fixing it means threading the calling
  macro's name into `ParseArgumentList` (or tagging the node with its
  operator source) so the mismatch can be reported — orthogonal to the
  property-assignment slice that recorded this, so left open.

### 23. A `via`-lambda in an enchant/interaction pass draws the same random value for every match (deliberate; found in the `random` data-name review, 2026-07-20)

- **Ours**: `EnchantmentPass.EvaluateViaLambda` wraps each per-match evaluation
  in `MacroContext.PushSideEffectGuard`, whose dispose restores the RNG's
  `(Seed, SeedIter)`. Every match therefore evaluates from the identical stream
  position and gets the identical draw.
- **Reference**: `enchantScope` calls the lambda per match against the global
  `State.random()`, so each match advances the stream and varies.
- **Trigger**: `(enchant: ?x, via (text-colour: (either: "red", "blue")))` —
  reference colours matches independently; we colour them all alike. Same for
  any `random`-bearing lambda, including `$palette's random`, which the
  `random` data name newly routes through `ctx.Rng`.
- **User-visible**: cosmetic and rare (a lambda whose *only* job is producing a
  changer, reaching a random source). Deliberate: our enchant and interaction
  passes are idempotent and re-run after every render **and** every dispatch,
  so an escaping draw would make the stream position depend on how many passes
  happened to run — which breaks the save model's promise that undo/redo/load
  reproduce a turn's randomness exactly. Reference has the same exposure and
  simply doesn't guarantee that. Fixing it properly means deriving each match's
  draw deterministically from seed + `pos` rather than letting the guard rewind;
  that is a design slice, not a patch, and isn't worth it until an author asks.

### 24. The `even` / `odd` datatypes require a Number where reference coerces one (deliberate; found in the Datatype slice, 2026-07-25)

- **Ours**: `DatatypeValue.IsTypeOf`'s `even`/`odd` arms return false for
  anything that isn't a Number, so `"2" is a even` is false.
- **Reference**: `even`/`odd` are the only entries in `datatype.ts`'s
  `typeIndex` with no type check — `obj => !isNaN(<number>obj) && (floor(abs(<number>obj)) % 2 === 0)`.
  The TypeScript cast is a lie at runtime, so JS coercion makes `"2" is a even`
  and `"3" is a odd` both true there. Every other string-adjacent entry
  (`digit`, `alnum`, `whitespace`, the case types) does check
  `typeof obj === 'string'` first, which is what marks this as an oversight
  rather than a design.
- **Trigger**: `(if: "2" is a even)`. Also `(if: true is a even)` — JS
  `abs(true)` is 1, so reference reports `true is a odd`.
- **User-visible**: rare and one-directional (we reject where reference
  accepts), and an author reaching it has almost certainly forgotten a
  `(num:)`. Deliberate: reference's own Datatype article says these "Only
  matches even numbers" / "odd numbers", so the documented contract was kept
  over the coercion artefact, an exception to following the released build. Pinned by
  `EvenAndOdd_RejectNonNumbers`. Revisit only if a real story depends on it.

### 25. The `lowercase` / `uppercase` datatypes don't expand under case mapping (deliberate; found in the Datatype slice review, 2026-07-26)

- **Ours**: `IsSingleCasedCodePoint` compares against .NET invariant casing,
  which is *simple* (never expands), so `"ß" is a lowercase` is false. Same for
  U+FB01 `ﬁ` and U+0149 `ŉ`.
- **Reference**: `lowercase: obj => [...obj].length === 1 && [...obj].every(char => char !== char.toUpperCase())`.
  JS applies full case mapping, so `"ß".toUpperCase()` is `"SS"` — different
  from `"ß"`, making it a `lowercase` there.
- **Trigger**: `(if: _c is a lowercase)` walked over German prose silently
  skips every `ß`.
- **User-visible**: rare, and confined to the handful of code points whose case
  mapping changes length. Deliberate: netstandard2.0 has no full-case-mapping
  API, and matching reference here would break the agreement with this
  library's own `(uppercase:)`/`(lowercase:)` (equally non-expanding) that
  reference's own comment names as the property it wants — "coincidentally
  consistent with (uppercase:), (lowercase:)". Pinned by
  `Lowercase_DoesNotExpandUnderCaseMapping`. Revisit if the string macros ever
  gain full case mapping, and move both together.

### 26. A printed array renders each item separately (deliberate; found in the printed-markup slice, 2026-11-21)

- **Ours**: `BodyRenderer.EmitPrinted` renders each item as its own markup source, joined by commas. A datamap prints as literal `key:value` text.
- **Reference**: `printBuiltinValue` joins the items into one string before rendering, so markup can span items, and prints a datamap as an HTML table with verbatim cells.
- **Trigger**: `(print: (a: "''x", "y''"))`, where the bold opens in one item and closes in another.
- **User-visible**: those delimiters stay literal. Deliberate: per-item rendering keeps non-string items such as datatypes and changers from being parsed as markup, and the core never puts HTML tables into the render channel.

### 28. Custom macro recursion stops sooner than in a browser (deliberate; found in the custom macro slice, 2026-11-22)

Real 3.3.9 recurses until the JavaScript stack runs out, about 450 calls for a simple macro. Here a call counts toward the 128-level render nesting limit, with a second limit on open expressions, and past either one it's the same in-prose "infinite loop" error. A stack overflow can't be caught and would kill the host, and these limits fit a 512 KB stack even where the runtime has no stack check.

### 29. `(partial:)` doesn't copy the target's type signature (deliberate; found in the custom macro slice, 2026-11-22)

Reference trims the target's signature and checks calls against it, keeping any rest position even when pre-filled, so `(partial: 'text-style', 'bold')` called with nothing errors there. Here the target macro does its own checking on the combined values, so that call works. Twinelike's built-ins have no signature objects to copy.

### 30. `(for:)` composed with `(output:)` works (deliberate; found in the custom macro slice, 2026-11-22)

`(for: each _a, 1, 2)+(output:)[_a]` renders `12` here, which is what Harlowe's own spec asserts. Real 3.3.9 stops with an internal JavaScript error when the command runs, so no working story can depend on that.

### 31. Markup in a code hook can't pair with markup outside the macro (deliberate; found in the custom macro review, 2026-11-22)

Reference lexes a passage with one stack of open markers, so a closer inside a code hook can close something opened before the macro. That discards the macro's own markers and turns the call into prose: `''(print: [ a'' ])''` renders a bold `(print: [ a` followed by ` ])''`. A code hook with no `]` likewise leaves `(print: [ )z` as prose. Here a code hook has its own markers, so the first example prints ` a'' ` in bold and the second is an in-prose error that takes the rest of its line with it, as other unparseable macros do. Only malformed macro calls are affected, and matching them would mean un-lexing a macro that has already been tokenized.

### 32. A revision in a macro body with a later target does nothing (found in the custom macro review, 2026-11-22)

`(set: $m to (macro: [(replace: ?a)[new](output-data: 1)]))($m:)|a>[x]` renders `1newx` in real 3.3.9, printing the hook beside the call even though `?a` isn't on the page yet. Here it renders `1x`, as the same `(replace:)` outside a macro does in both. A target already on the page behaves the same in both.

### 33. A revision, `(for:)` or link changer attached to `(show:)` has no effect (found in the `(show:)` slice, 2026-11-23)

A changer written before `(show:)` styles the hook it reveals, and a false condition stops it, as in real 3.3.9. Reference also passes the rest of the changer into the reveal, so `|a)[_i](for: each _i, 1, 2)(show: ?a)` shows `2`, the last pass, and `(click-replace: ?c)(show: ?a)` arms a click. Here those show the hook once, with no loop variable and no click. `(link:)` before `(show:)` throws an internal error in real 3.3.9, so nothing can depend on it.

### 34. Natural order is approximate for accented text and hex strings (found in the dataset slice, 2026-11-23)

Spreading a dataset, `(passages:)`, the order `startup` passages run in, and `(dm-names:)`, `(dm-values:)` and `(dm-entries:)` all sort with `NaturalOrder`, which covers ASCII exactly. Real 3.3.9 uses the browser's English collator, so `(a: ...(ds: "é", "e", "z"))` is `e,é,z` there and `e,z,é` here, because other letters sort after `z`. It also reads `"0x10"` as the number 16, so `(dm-names:)` puts `"0x9"` before `"0x10"`, and reads date-shaped strings as dates. Neither is implemented here.

### 35. Limits on `(for:)` passes, `(range:)` length, print depth and live hooks (deliberate; found in the `(range:)` slice, 2026-11-23)

One render can make a quarter of a million `(for:)` passes, one `(range:)` can hold 200,000 numbers, a value can print 256 levels deep, and a passage can have 500 `(live:)` hooks running. Past any of these it's an in-prose error. Real 3.3.9 sets no limit of its own: `(range:)` worked at 100,000 and failed with a JavaScript stack error at 150,000, printing managed 300 levels and failed at 5,000, and a 3,000 by 3,000 loop was still running when the test stopped it after 3 seconds. Here a stuck render or stack overflow would take the host game down with it.

### 36. A run of spaces in a hand-edited tags attribute adds no empty tags (found in the `(passage:)` review, 2026-11-23)

Reference splits the `tags` attribute on each whitespace character, so `tags="a  b"` gives `(passage:)` three tags, the middle one empty. Here it gives two, because `HarlowePassage.Tags` promises hosts a list with no empty entries. Matching reference under that promise would mean keeping the raw attribute beside the list, a copy that goes stale when a host edits `Tags`, for something Twine doesn't write: none of the pinned exports has a run of spaces in its tags. An untagged passage does get reference's one empty tag.

### 37. A visible hook revising itself while it renders keeps its later content — ✅ FIXED (2026-11-24)

**Resolved.** A revision of a hook still being written first moves what's written so far aside, into a placeholder in the hook, so the revision lands after, before or over all of it, as in reference, where the hook's whole text is in place before its macros run. `|o>[x(replace: ?o)[y]z]` renders `y` and the `(append:)` form `xzy`, as in real 3.3.9, and so do `(replace: ?passage)[x]y` (`x`) and `w(append: ?passage)[x]y` (`wyx`).

### 38. A hook hiding itself, or copied while hidden, can come back different (found in the `(hide:)` slice, 2026-11-23)

Reference renders a hook's content before running its macros, so `(hide:)` inside the hook it names takes the whole content out at once. Here the hook is hidden when it finishes rendering, so the result shows the same, but a macro after the `(hide:)` still sees the content on the page: `|a>[x(hide: ?a)(replace: "x")[y]](show: ?a)` shows `xy` in real 3.3.9 and `y` here. Copies differ too. When a revision's content holds a hook that hides itself, each target's copy renders in turn here, so the first copy's `(hide:)` can't see the second (#41): `|o>[]|q>[](append: ?o, ?q)[|a>[x](hide: ?a)]` followed by `(show: ?a's 1st)` shows `x` and `(show: ?a)` shows `xx`, where 3.3.9 shows `xx` and `x`.

### 39. `time` moves by frames (found in the `(live:)` slice, 2026-11-24)

`time` reads the clock the host moves with `StorySession.Tick`, so between two frames it doesn't change, where reference reads the browser's clock, which does. A click 6 ms after a frame reads the frame's time here.

This entry also recorded a trailing space that a live hook inside `{…}` lost. That's fixed (2026-11-25). Every render after the passage, a live hook's included, now collapses on its own as it finishes, as reference's `Section.execute` does, so `{(live: 100ms)[ (set: $n to it + 1) $n ]}` shows `1 ` as in real 3.3.9. 91 of 92 measured cases with a click, tick or `(show:)` inside `{…}` match; the other is a harness artifact.

### 40. A revision's own content can't revise its target first — ✅ FIXED (2026-11-24)

**Resolved.** A revision renders its content into a placeholder in each of its targets and then runs it, as reference's `ChangeDescriptor.render` inserts it before `Section.execute` runs it, so a revision inside that content lands on top, each target gets its own rendering, with its own side effects, clicks and links, and a hook in the content can be targeted at once. While the passage renders, a revision only reaches what's before it, after any ordinal has picked; a changer left in content a revision emptied does nothing; and `(for:)` combines with a revision. Clicks, live hooks and `(show:)` render the same way. 76 of 82 measured cases match real 3.3.9, the rest being #38, #41, #42 and #43; harlowe-demo's battles now get past their first menu.

### 41. A revision's later copies aren't there yet while an earlier copy runs (found in the revision slice, 2026-11-24)

Reference inserts a revision's content into every target before running any of it, so an earlier copy's macros can see and reach a later copy's text and unrun hooks. Here each copy renders in turn, so a later one is still an empty placeholder. While the passage renders, a revision can't reach a later copy anyway, so this only shows after it, or with macros that aren't revisions: `|a>[x]|a>[y](link: 'go')[(replace: ?a)[|c>[w](append: ?c)[q]]]` shows `wqqqwq` in real 3.3.9 after the click, and `wqqwq` here. Matching it would mean rendering every copy's text before running any copy's macros, which the renderer can't separate.

### 42. Overlapping revision targets run the content twice in either order (found in the revision slice, 2026-11-24)

With the `x` inside `?a`, `(set: $n to 0)|a>[x](replace: ?a, "x")[(set: $n to it + 1)]$n` shows `2` in real 3.3.9 and here, but `(replace: "x", ?a)` shows `1` there and `2` here. Reference's result depends on whether the copy for the inner target is inserted before or after the outer target is emptied; here every copy runs, on the page or off it.

### 43. `(for:)` with a click or hover changer is an error (found in the revision slice, 2026-11-24)

`|a>[x](for: each _i, 1, 2)+(click: ?a)[_i]` is an in-prose error here. Real 3.3.9 arms the click once per pass, and a single click reveals every pass: `x12`. Here a click reveals one region per click, so this needs one click to fire several interactions, unlike #18's stacking.

### 44. A list inside `{…}` or formatting inside a list item keeps the rest of the item (found in the list slice, 2026-11-25)

Reference's renderer closes one more list level than it opened. Where an enclosing list is open in the same render, as with a list in a `{…}` span or inline formatting inside a list item, that surplus `</ul>` closes the outer list early, because none of `<tw-collapsed>`, `<b>` or `<li>` bounds an end tag's scope. So `* {` / `* b` / `} c` shows ` c` after the whole list in real 3.3.9, and `* a {x` / `* b` / `} c` / `* d` also leaves `d` as a stray item. Here the tags balance, so ` c` stays in its item. Where nothing enclosing is open, the surplus close is ignored and the results match.

### 45. A `<name|` with nothing to attach to shows as text (found in the project review, 2026-11-26)

An unpaired `<name|` prints as written here, as an unpaired `|name>` does in both. Real 3.3.9 builds its page as HTML, so the browser reads `<n|` as the start of a tag and drops it, along with the text after it up to the next `>`: `]<n|a` shows `]` there and `]<n|a` here. About 15,700 of the bench's generated snippets do this, and the snippet suite leaves them out. A host shows text as text, so there's nothing for it to drop.

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
  shape and `pos` binding.
- `(history:)` — derived from the save timeline, as reference derives it.

---

## Critical files (quick reference)

- `HarloweParser/Runtime/Macros/` — all our macro
  implementations, one class per file.
- `HarloweParser/Runtime/Macros/StandardMacros.cs` —
  macro registration; the `RegisterAll` site for new aliases.
- Reference macros live under `ts/macrolib/`, grouped by category
  (`commands.ts`, `datastructures.ts`, `stylechangers.ts`,
  `enchantments.ts`, `values.ts`, etc.). Fetch them from the
  `Codaea/harlowe-branch-default-2` GitHub mirror via `gh api` (Heptapod
  itself is Anubis bot-walled) — see CLAUDE.md for the command and the
  unofficial-mirror caveat. For local grep access instead, unzip a `branch
  default` snapshot to `references/harlowe-branch-default.zip` (**not**
  committed — third-party Zlib source, gitignored).
