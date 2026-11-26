# Revisions rendered in place

Plan, 2026-11-24, built the same day. Fixes `MACRO-DIVERGENCES.md` #40 and #37, which stopped harlowe-demo at its first battle menu. Also lets `(for:)` combine with a revision.

The build departs from the plan in three ways:
- Each rendering is charged a `(for:)` pass's fixed cost, not a nested render's, so a string target with 200,000 matches still fits the budget.
- Each rendering checks the stack, as a custom macro does, because 120 nested revisions overflowed a 160 KB stack, before this change and after.
- Step 4's layers are exactly the ones a revision opens: its styles, its whitespace layer and its own hook. In real 3.3.9 a changer inside inline formatting, a `{…}` span or an anonymous hook in emptied content still works.

Measured afterwards, 76 of 82 cases match real 3.3.9. The rest are the three differences named below and #38.

## What's wrong

`Changer.RunRevision` renders a revision's content once, aside, and splices a copy into each target afterwards. Reference renders the content into each target and only then runs the macros in it.

I measured 73 cases on real 3.3.9, in `f21`–`f27` in the scratchpad's `four` folder. 19 of them match here today. The differences fall into six groups.

| Group | Example | Real 3.3.9 | Here |
|---|---|---|---|
| An inner revision lands on top of its outer one | `\|a>[x](replace: ?a)[(replace: ?a)[y]z]` | `y` | `z` |
| Each target is its own rendering, side effects included | `(set: $n to 0)\|a>[x]\|a>[y](replace: ?a)[(set: $n to it + 1)$n]` | `12` | `11` |
| While the passage renders, a revision only reaches what's before it | `\|a>[x]\|b>[q](replace: ?a)[(replace: ?b)[y]z]` | `zq` | `zy` |
| Macros left in content that a revision emptied lose their hooks | `\|a>[x](replace: ?a)[(replace: ?a)[y](click: ?a)[u]]` | `y`, no link | nothing, and a click that shows `u` |
| A hook revised while it's still being written (#37) | `\|a>[x(append: ?a)[y]z]`, `(replace: ?passage)[x]y` | `xzy`, `x` | `xyz`, `xy` |
| `(for:)` combined with a revision | `\|a>[x](for: each _i, 1, 2)+(append: ?a)[_i]` | `x12` | an error |

The same applies to a click's revealed content, to a `(show:)` and to a live hook. For example, `(link: 'go')[|c>[w](replace: ?c)[v]]` shows `v` there and `w` here.

## How reference does it

- **Render, then run.** `ChangeDescriptor.render` (`ts/internaltypes/changedescriptor.ts`) parses the content into unrun `tw-expression` and `tw-hook` elements. It inserts them into the target ("Here are the reasons why the DOM must be connected to the target before the expressions are evaluated"). `Section.execute` (`ts/section.ts`) then runs them in page order.
- **All targets first.** A revision resolves every target of every argument before rendering any. Each target then gets its own rendering ("when a HookSet has multiple targets, each target has its own distinct rendering of the same TwineMarkup"). All of them are inserted before any of them runs. During a click, `|a>[x]|a>[y]` with `(replace: ?a)[(append: ?a)[q]]` shows `qqqq`.
- **Earlier targets only, for revisions, while the passage renders.**
  - `showPassage` (`ts/engine.ts`) calls `detachStoryElement()` before rendering.
  - A target is dropped when it's both detached and later than the revision's own hook. This applies only to the targets of `(replace:)`, `(append:)` and `(prepend:)` ("These macros are scoped to only target hooks and text that has already rendered - earlier in the passage or section to this changed hook").
  - `(show:)`, `(hide:)`, enchantments and clicks have no such limit. There's none once the passage is attached, either.
  - Ancestors count as before. A string target is wrapped everywhere, and the later wraps are left in place unused.
  - The filter runs after the HookSet has picked its ordinals in `#hooks`. So `|a>[x]|a>[y](replace: ?a)[(replace: ?a's last)[q]]` picks the second `?a` from inside the first copy, drops it, and shows `q`.
- **Custom macro bodies follow from that filter.** A body renders in its own detached tree, so the "later than" test compares nodes from two separate trees. Chromium answers that by memory address. While the passage renders, a hook made with the passage therefore counts as earlier than the body's hook, and is reached. A string wrap made by the search itself counts as later, and never is.
  - In `(set: $m to (macro: [(replace: ?b)[y](output-data: [])]))|a>[x]|b>[q](replace: ?a)[($m:)]`, `?b` becomes `y`.
  - The `(replace: "q")` form leaves `q` before the call, after it, and at the top level.
  - During a click it replaces `q`.
- **Emptied content loses its hooks.**
  - The content goes straight into the target, not into a wrapper. Emptying the target detaches each of its children separately.
  - `execute` still runs the detached expressions ("in case they are removed from the DOM before running").
  - But `#runExpression` finds a changer's hook through `nextNonWhitespace`, which finds nothing once the siblings are separated. The changer becomes an error that's off the page. Commands still run, so `(replace: ?a)[(replace: ?a)[y](set: $n to 5)]$n` shows `y5`.
  - A hook nested deeper keeps its own children together, so changers inside it still work.
- **A hook's text is in place before its macros run.** Reference has no "still being written" state. A hook's later text is already in the target when a revision in it runs, which is what #37 measures. Step 5 below reproduces that ordering.
- **`(for:)` with a revision** renders once per pass. Each pass applies the revision with that pass's loop variable (the `loopVars` loop in `Section.renderInto`).
- **Collapsing whitespace** follows where a hook was written, not its target ("this uses the **original** target, not desc.target, to determine if it's inside a <tw-collapsed>"). That's what `RenderHookInto` already does.

## Design

1. **One rendering per target, into a placeholder.**
   - **Resolve.** Resolve every target of every argument first, including string wraps.
   - **Place.** Then, argument by argument and each argument's targets in page order, give each target a placeholder W. W is an anonymous `RenderHookNode` with `WhitespaceMode.Inherit`, added directly rather than through `BeginWhitespace`.
     - `replace` clears the target and leaves only W.
     - `append` puts W last.
     - `prepend` puts W first.
   - **Render.** Then render the hook into every W: those on the page in page order, then any already off the page in the order they went in.
     - Each W gets a builder whose stack starts at W. The composed styles go inside W, and `RenderHookInto` adds its whitespace layer inside W as it does now.
     - W is building while it renders.
     - Afterwards, if W is still directly in its target, unwrap it there.
   - **Budget.** Before each rendering, spend `NestedRenderOverhead` with `TrySpendOnSplice`. After it, spend `SpliceCost` of what W holds. Both come from the existing `NestedRenderCost`. If either fails, put the budget error in that target and skip the rest.
   - **Result.**
     - An inner revision lands on top. The rest of the outer content goes before what an inner append adds and after what an inner prepend adds: `(replace: ?a)[w(append: ?a)[y]z]` shows `wzy`, and the prepend form `ywz`. After an inner replace they go nowhere, because W is off the page.
     - Click and hover combos fill their targets the same way.
     - A plain reveal renders into its anchor with the interaction's reveal mode, so a rerun clears the anchor first. `(link-rerun: 'go')[(set: $n to it + 1)$n]` shows `go1`, then `go2`.
2. **`(for:)` with a revision.** Drop the error for this pair. Each pass of the loop runs step 1 inside that pass's scope, and each pass is charged as a loop pass is now.
   - `(for:)` with a click or hover changer stays an error, and gets its own divergence. In real 3.3.9 `|a>[x](for: each _i, 1, 2)+(click: ?a)[_i]` arms the click once per pass, and a single click reveals every pass (`x12`). That's a different mechanism from #18's stacked clicks, which reveal one region per click.
3. **Earlier targets only, for revisions, while the passage renders.**
   - **The flag.** `StorySession` sets a context flag for the passage's own render, which includes the startup and header passages.
   - **Which builders it applies to.** The flag only matters for a revision whose builder writes the page.
     - The passage's builder is marked as writing the page, and so is every builder that starts at a node already in the page: placeholders, `(show:)` and live renders.
     - A custom macro body's sealed builder isn't, and neither is any other builder that starts empty.
   - **The filter.** A revision from a builder that writes the page drops matches that come after the end of the builder's top container in page order.
     - Before means ancestors, everything earlier, and everything already written inside the open containers. After means whatever follows the top container's subtree.
     - The resolver's own walk records each match's position. Ordinal narrowing runs first, over all the matches, and the filter then drops the later ones. In `|a>[x]|a>[y]|a>[z](replace: ?a's 2nd)[(append: ?a's last)[q]]` the inner append picks the third `?a`, drops it, and shows `xz`.
     - A string target only wraps text before that point.
   - **Custom macro bodies.** While the flag is set, a string target in a custom macro body matches nothing, and a hook target there is unlimited. That's Chromium's answer above.
4. **Emptied content drops its changers.**
   - **Cut.** A placeholder, W or S below, is cut when it's no longer a direct child of its target. That's a cheap check, because the target is known.
   - **The test.** Start at the top of the builder that's writing the changer, and walk down through any style, whitespace and interactive layers. If that reaches a cut placeholder, the changer is dropped, and the top itself counts.
   - **Dropped.** A dropped changer does nothing. Its hook renders plainly into the cut content, out of sight, and commands still run.
   - **Not dropped.** A hook nested deeper inside cut content keeps its changers.
5. **A target that's still being written.**
   - **The writer.** `StartBuilding` records which builder is writing the node.
   - **Move aside.** When a revision targets a node that's still being built, it first moves all of the node's children into a new placeholder S, including any hooks still open in it. S becomes the node's only child.
   - **The stack.** In that builder's stack, the node's entry becomes S, wherever it sits, and the entries above it don't change. `[root, a, b]` becomes `[root, S, b]`. When the node is the passage root at the bottom of the passage's builder, `[root]` becomes `[S]`, and `Root` still returns the page root. Then the revision applies as usual.
   - **Closing.** When the builder closes S, it finishes S, unwraps S if it's still in the node, and then finishes the node, so a pending `(hide:)` still runs.
   - **Covers.** `?passage` during the passage's render, a live hook's render and a `(show:)` render.
   - **Measured.** `|a>[x|b>[p(replace: ?a)[y]q]z]` shows `y`, and its append form `xpqzy`. `(replace: ?passage)[x]y` shows `x`, and `w(append: ?passage)[x]y` shows `wyx`. `|a>[x(append: ?a)[(append: ?a)[y]w]z]` shows `xzwy`, and `|a>[x(replace: ?a)[(append: ?a)[y]w]z]` shows `wy`. A splice deferred until the node finishes would show `xzyw` and `w` for the last two, so it isn't used.
6. **Remove the copying.**
   - **Deleted.** `RenderNode.Clone` and every override, `RenderNodes.CloneAll`/`CloneInto`/`Splice`, and `LiveId`. `Tick` renders each live hook's held node, on or off the page. Off the page, real 3.3.9 keeps running it too: `y3`.
   - **Kept.** `RevealRegionId`, `LabelRegionId`, `SourceRegionId` and `SourceEnchantment` stay, because the dispatch and the passes still find nodes by them. They're just never copied.

## What changes for stories

- Side effects in a revision's content run once per target: `(set:)`, `(either:)`, `(random:)`, and links and clicks. One click reveals one copy.
- An inner revision wins over the outer one, in all three modes.
- `(replace: ?passage)[x]y` shows `x`, and `w(append: ?passage)[x]y` shows `wyx`.
- A revision inside shown or revised content written mid-passage can't reach hooks after it.
- While the passage renders, a string target in a custom macro body matches nothing.
- `(for:)` combines with a revision.

## Differences that stay

- **Later copies don't exist yet.** After the passage has rendered, during a click or a live hook, an earlier copy's macros can't see or reach a later copy. In reference, the later copy's text and unrun hooks are already there. `|a>[x]|a>[y](link: 'go')[(replace: ?a)[|c>[w](append: ?c)[q]]]` shows `wqqqwq` there, and will show `wqqwq` here. While the passage renders, the filter hides later copies in both, and they agree: `wqqwq`.
- **Overlapping targets.** When one revision's targets overlap, reference's result depends on their order. `(replace: ?a, "x")` with the `x` inside `?a` runs the content twice, and `(replace: "x", ?a)` runs it once. Here both run it twice.
- **Detached trees.** Inside a hook nested in cut content, reference compares positions across detached trees by memory address, and that isn't reproduced. Here such a revision has no limit.
- **Leftover wraps.** Reference leaves unused wraps around later string matches. Pages are the same.
- **Existing divergences.** #32 is unchanged. #38 gets re-measured: the half about copied hooks goes away with copying, and the self-hiding half may change.

## Steps

1. Build step 1, including the dispatch paths, and run the tests. Every test that changes gets checked against real 3.3.9 before its expectation moves.
2. Build steps 2 to 5, then step 6.
3. Add `f21`–`f27` as tests. All 73 should match except the three named above: `(for:)` with a click, a later copy during a click, and overlapping targets.
4. Verify:
   - every earlier differential set, the markup corpus and the specs;
   - Service Suspended and Files-005;
   - fuzzing;
   - 160 KB stacks, adding two shapes: many targets with nested revisions, and deep revision nesting;
   - the harlowe-demo walks, which should get past the first battle menu.
5. Update the docs:
   - `MACRO-DIVERGENCES.md`: #40 and #37 resolved, #38 re-measured, the differences above, and `(for:)` with a click as its own entry;
   - the revision, render tree and dispatch sections of `CLAUDE.md` and `ARCHITECTURE.md`;
   - `CHANGELOG.md`;
   - `TODO.md`;
   - the harlowe-demo row in `TEST-BENCH-PLAN.md`.

## Risks

- **Work grows with targets.** N targets whose content revises the same N targets means N^depth renderings. Step 1's charges stop that with the budget error, where reference would try to build the whole page.
- **Stack.** The renderings nest exactly as the aside rendering does now, so there's no new recursion. The two new stack shapes check that.
- **Builder surgery.** Step 5 is the one place a builder's stack changes under it. Every case above runs through it, and the fuzzer exercises it.
- **Pinned tests.** Tests of `Clone`, of anchors that reach the page as copies, and of multi-target revisions encode today's behaviour, and will change.
