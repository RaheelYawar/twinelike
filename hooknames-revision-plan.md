# HookNames + Revision/Enchantment Macros — Implementation Plan

Forward-looking plan for the next major slice after v2.3. Once each sub-slice
ships, its summary folds into `CLAUDE.md` and the corresponding section here is
deleted (same lifecycle as `v2.3-lambdas-plan.md`).

## Goal

After this slice, a Harlowe story can target *named hooks* by reference
(`?cake`), rewrite their content (`(replace:)`/`(append:)`/`(prepend:)`),
restyle every match of a name (`(change:)`/`(enchant:)`), and attach
interaction handlers (`(click:)` family). These are the macros that make a
Harlowe story interactive beyond passage links — the next game-engine-visible
capability after `(for:)` iteration.

## The architectural decision: an internal render tree

### Why the current model can't carry this

`BodyRenderer` is a single linear pass that pushes `Text`/`Link`/`PushStyle`/…
events straight through `IRenderOutput`. There is no addressable, mutable
representation of rendered content. Revision and enchantment macros
fundamentally need one — confirmed against the reference Harlowe JS source
(`modality/harlowe` mirror):

- **HookSet** (`?name`) is `{section, selector}` — a *query*, lazily re-resolved
  against the live DOM every time it is used, not a captured reference.
- **`selectHook`** queries "this section's DOM and all above it" — revision
  macros target content *already rendered*, not content that comes later. (My
  earlier "forward-only, linear + pending" guess was backwards.)
- **Revision macros** (`replace`/`append`/`prepend`) are changers carrying a
  `target` (HookSet/String) + an `append` mode; they mutate the target element
  in the tree via a `ChangeDescriptor`.
- **Enchantment macros** (`enchant`/`change`/`click`/`mouseover`) wrap matched
  elements, register in a `section.enchantments` list, and are **re-applied by
  `updateEnchantments()` after every render pass** — that is how they catch
  later-rendered hooks and how `(enchant:)` persists.

A pure linear push can't do "target what's already rendered" or "re-scan after
each pass" without buffering everything first — which *is* a tree, just an
awkward one.

### What we introduce

An internal **render tree** layer that sits *between* `BodyRenderer` and
`IRenderOutput`:

```
PassageBody AST ──BodyRenderer──▶ RenderTree ──(revision/enchant passes)──▶ flush──▶ IRenderOutput
```

- `BodyRenderer` builds a `RenderNode` tree instead of pushing events directly.
- Revision/enchantment macros mutate the tree.
- A final flush walks the finished tree and emits the **same**
  `Text`/`Link`/`PushStyle`/`PopStyle`/`Error` events to `IRenderOutput`.

**`IRenderOutput`'s contract does not change in 3A–3C.** Every existing engine
consumer and all 854 tests keep working because the flushed event stream is
byte-identical for any content that doesn't use the new macros. The only
interface growth is in 3D (an interactive-region channel for `(click:)`), and
even that is additive.

This mirrors the reference implementation (`ChangeDescriptor` → our
`HookDescriptor`; `<tw-hook>` elements → our `RenderHookNode`) — the same
"align with reference architecture" principle behind the v2.3D descriptor-patch
refactor.

### RenderNode shape (3A)

New folder `HarloweParser/Runtime/Rendering/`:

- `RenderNode` — base; a node in the tree. Children list where applicable.
- `RenderTextNode { string Content }`
- `RenderHtmlNode { string RawHtml }`
- `RenderLinkNode { string Text, Target }`
- `RenderErrorNode { string Message }`
- `RenderStyleNode { StyleSpec Style; List<RenderNode> Children }` — replaces a
  Push/render/Pop bracket; flush emits `PushStyle`/children/`PopStyle`.
- `RenderHookNode { string Name; HookAnchor Anchor; List<RenderNode> Children }`
  — the addressable unit. Anonymous hooks still produce one (Name == null) so
  position/string targeting can find them; named hooks carry their name.
- `RenderRoot : RenderNode` — top of a passage render.

`RenderTreeFlusher` — walks the tree, emits to `IRenderOutput`. `BufferedRenderOutput`
stays the test double; tests can also assert against the tree directly.

## Sub-slices

### 3A — render tree + HookName foundation

**De-risks the scary refactor in isolation: zero author-visible behaviour
change, all 854 tests stay green.** No new macros land; this slice is the
plumbing everything else builds on.

**Lands:**

- **Render tree** (`Runtime/Rendering/`) as above. `BodyRenderer` rewritten to
  build a `RenderRoot`; `StorySession.RenderInternal` flushes it through the
  existing `BufferedRenderOutput`. `Changer.Apply` builds `RenderStyleNode` /
  iteration subtrees instead of calling `output.PushStyle` directly — the
  descriptor-patch model is unchanged, only its *executor* retargets from
  `IRenderOutput` to tree nodes.
- **`?name` tokenization** — new `TokenType.HookRef`. The tokenizer scans `?`
  followed by letters in **expression context only** (`?name` is a value used
  in macro args: `(replace: ?cake)`, `(set: $x to ?cake)`). Distinct from the
  existing `HookNameLeft`/`HookNameRight` tokens, which are hook *declarations*.
- **`HookRefNode : IExpressionNode`** — `{ string Name; List<HookRefStep> Steps }`
  where `Steps` carries `'s 1st` / `'s last` / `'s (N)` ordinal narrowing and
  built-in sub-selectors. `IExpressionVisitor` gains `Visit(HookRefNode)`.
  Parser: `ParseAtom` handles `TokenType.HookRef`; the existing `'s` operator
  path produces the ordinal steps.
- **`HarloweValueKind.HookName`** + `HookNameValue` — the runtime value: a
  selector spec `{ string Name; IReadOnlyList<HookRefStep> Steps }`. Reference-
  ish equality (authors rarely compare hooknames; matches `LambdaValue`
  precedent). `ExpressionEvaluator.Visit(HookRefNode)` produces one.
- **`selectHook` resolution** — `HookResolver.Resolve(RenderNode tree, HookNameValue)`
  returns the matching `RenderHookNode`s, applying ordinal steps. Lives in
  `Runtime/Rendering/`. Nothing consumes it yet except a couple of direct
  tests — it is the primitive 3B/3C/3D call.
- **Built-in hooks** — `?page`, `?passage`, `?link` recognised as reserved
  names. 3A wires `?passage` (the passage root) and `?link` (all `RenderLinkNode`s);
  `?page` is a session-level concept, deferred to 3C where enchant scope needs it.

**Tests:** render-tree flush parity (a fixture corpus renders to the identical
`BufferedRenderOutput` entry stream pre/post refactor); `?name` tokenizer cases;
`HookRefNode` parsing incl. `?name's 1st`/`'s last`; `HookNameValue` evaluation;
`HookResolver` over hand-built trees (named match, anonymous skip, ordinal
narrowing, no-match → empty, `?link` built-in).

### 3B — revision macros `(replace:)` / `(append:)` / `(prepend:)`

**Lands:**

- **`HookDescriptor` grows** `RevisionTarget` (a `HookNameValue` or a `string`
  for text-occurrence targeting) + `RevisionMode` enum (`Replace`/`Append`/`Prepend`).
- **New `IChangerPatch`: `RevisionPatch { Target, Mode }`** — drops in per the
  v2.3D model, no change to `Changer.Apply`'s signature.
- **`Changer.Apply` executor branch** — when the descriptor carries a
  `RevisionTarget`: render the attached hook's content into a detached
  subtree, resolve the target node(s) via `HookResolver`, and splice — replace
  empties the target's children first; append/prepend add at the end/start.
  Targeting operates on the **tree built so far** (current + above), matching
  Harlowe's `selectHook` scope.
- **String targeting** — `(replace: "old text")` finds `RenderTextNode`
  occurrences of the substring and wraps/splits them as targets. Scoped to a
  shared `TextOccurrenceFinder` helper so 3C/3D reuse it.
- The three macros are thin: each produces a `Changer` with one `RevisionPatch`.
  `(replace:)`/`(append:)`/`(prepend:)` register in `StandardMacros`.

**Tests:** replace/append/prepend into a named hook; into an anonymous hook by
string; target appears above vs. not-yet-rendered (latter → no-op, documented);
multiple matches all updated; revision composed with a style changer; error
when target arg isn't a hookname/string; round-trip through `MarkupPrinter`.

### 3C — `(change:)` / `(enchant:)` + the enchantment re-scan pass

**Lands:**

- **`(change:)`** — `(change: ?target, changer)`: applies `changer` to every
  current match of `?target`. One-shot, at the point it runs.
- **`(enchant:)`** — same surface, but **persistent**: the enchantment is
  registered and re-applied after every render pass within the passage, so it
  catches hooks rendered later and survives revision-driven re-renders.
- **Enchantment registry** — `MacroContext.Enchantments` (a `List<Enchantment>`),
  plus an `UpdateEnchantments(RenderRoot)` pass invoked by the flusher driver
  after the main render and after any revision mutation — the analogue of
  Harlowe's `updateEnchantments()`. Each `Enchantment { HookNameValue Target;
  Changer Changer }` re-resolves its target fresh each pass (re-query, not a
  cached node list).
- **`?page` built-in** — resolves to the `RenderRoot`; `(enchant: ?page, …)` is
  the documented whole-passage styling idiom.
- **Out of scope here:** story-wide `(enchant:)` via `header`/`footer`-tagged
  passages (needs `StorySession` to know tagged passages and thread
  enchantments across passage boundaries) — note as a follow-up.

**Tests:** `(change:)` restyles all matches once; `(enchant:)` catches a hook
declared after the macro; `(enchant:)` re-applies after a `(replace:)` mutates
the tree; `(enchant: ?page, …)` wraps the whole passage; enchant + revision
interaction order; changer-arg type errors surface in-prose.

### 3D — `(click:)` family + event-dispatch contract

**The one slice that grows `IRenderOutput`.** Open design decision to settle at
the top of this slice (will surface options before committing):

- **Interactive-region channel.** Two candidates: (a) additive `IRenderOutput`
  methods `BeginInteractive(InteractiveRegion)` / `EndInteractive()` bracketing
  the region's flushed events; or (b) `RenderResult` carries a flat
  `List<InteractiveRegion>` with character ranges and the host wires its own
  hit-testing. (a) suits engines that build a UI tree (Unity/Godot); (b) suits
  buffer/CLI consumers. Likely ship (a) with (b) derivable.
- **Event dispatch** — `StorySession.DispatchEvent(string regionId)`: looks up
  the registered handler, renders its deferred prose into the target via the
  same revision machinery from 3B, runs the enchantment re-scan, returns a
  fresh `RenderResult`. No whole-passage re-render — targeted, like Harlowe.

**Lands:** `(click:)`, `(click-replace:)`, `(click-append:)`, `(click-prepend:)`,
`(mouseover:)`, `(mouseout:)` (and the `-replace`/`-append`/`-prepend` combos).
Combos are shorthand: an interaction kind + a revision mode on the same
descriptor — they reuse 3B's `RevisionPatch` and a new `InteractionPatch`.

**Tests:** click region emitted around the right hook; dispatch renders deferred
prose into the target; single-use vs. repeatable; combo macros; mouseover/out
region kinds; dispatch of an unknown region id → no-op; `RenderResult` interactive
metadata shape.

## Cross-cutting concerns

### MarkupPrinter

`MarkupPrinter` gains `Visit(HookRefNode)` — emits `?name`, `?name's 1st`, etc.
Revision/enchant/click macros are ordinary `MacroCallNode`s in the AST, so they
already round-trip; only the `?name` *argument* shape is new. Round-trip tests
added per sub-slice (3A: `?name` forms; 3B: `(replace: ?x)[…]`; 3D: `(click: ?x)[…]`).

### Error policy

Unchanged. A bad hook reference, a missing target, or a wrong-typed changer arg
produces a `HarloweValue.Error` that flushes through `IRenderOutput.Error` —
never a thrown exception. The render tree carries `RenderErrorNode`s inline so
errors stay positioned where they occurred.

### Existing tests

The 3A flush-parity test is the safety net: the render-tree refactor must not
change a single `BufferedRenderOutput` entry for existing content. All 854
v2.3-era tests must stay green through 3A with no edits. If a test needs
editing in 3A, the refactor changed observable behaviour and that is a bug.

### Performance

The tree is built once per passage render and discarded (except registered
enchantments). For typical passage sizes this is negligible; engine consumers
already re-render on navigation. No pooling needed initially — note as a
revisit point only if profiling on a large story shows it.

## What this slice does NOT ship, and why

- **Transitions** (`(t8n:)`, `(transition:)`, `t8n-depart`/`t8n-arrive`) — they
  ride on the descriptor too, but they are a styling/timing concern orthogonal
  to hook targeting. Their own slice; slot in as another `IChangerPatch`.
- **Story-wide `header`/`footer` enchantment** — needs session-level tagged-
  passage awareness and cross-passage enchantment threading. Follow-up after 3C.
- **`(link:)` family** (`(link-replace:)`, `(link-reveal:)`, `(link-goto:)`) —
  interaction macros, but they create *new* link elements rather than targeting
  existing hooks. Adjacent, separate slice; reuses 3D's interaction channel.
- **Full pseudo-hook / `?page`-of-other-passages semantics** — 3C does the
  same-passage `?page`; the reference impl's cross-section pseudo-hooks are
  deferred.
- **`(replace:)` of not-yet-rendered targets** — Harlowe itself no-ops here
  (the DOM doesn't exist yet); we match that. Authors use `(enchant:)` for
  forward targeting, which 3C supports via the re-scan pass.
- **Custom DOM elements / raw `<tw-*>` exposure** — engine consumers get
  semantic events, not Harlowe's HTML element model.

## Sequencing

3A → 3B → 3C → 3D, in order. 3A is a pure refactor + primitive and must land
green before anything builds on it. 3B and 3C are each independently shippable
author-visible increments. 3D is gated on the `IRenderOutput` interactive-channel
decision and is the largest single sub-slice.
