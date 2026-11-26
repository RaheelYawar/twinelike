# Lists, and collapsing later renders one at a time

Plan, 2026-11-25, built the same day. Measured afterwards, 133 of 138 list cases and 91 of 92 collapse cases match real 3.3.9, and so do all 810 harlowe-demo steps. The list cases that differ are the two #44 cases, two from the existing unterminated-macro gap, and one where the probe doesn't mark a `(link:)` label. The collapse case that differs is the harness artifact named below. The build departs from the plan in two ways. A hook hidden while it was written still has its content collapsed, as reference collapses detached content: with nothing before or after it, and each span in it searching only its own top-level detached node. And renderers track the sibling lists they're inside only after the passage has rendered, since nothing during the passage render reads them.

These are the two things left between harlowe-demo and real 3.3.9. Of the demo's 810 replayed steps, 302 match exactly. 266 differ only because `* item` lines print as text, and 242 only by a trailing space (`MACRO-DIVERGENCES.md` #39).

## What's wrong

**Lists.** The tokenizer already recognises `* ` at a block start, because it outranks emphasis, but it emits it as text, so the bullet prints. `0. ` isn't recognised at all. Of 110 list cases measured on real 3.3.9, 18 match here.

**Whitespace after the passage renders.** After every click and every live-hook tick, the session re-collapses every `{…}` span on the page. Reference collapses only what each later render added, once, and never revisits the rest. Of 74 cases with a click or a live hook inside `{…}`, 35 differ here. One of those is a harness artifact (the real side reads `textContent`, which drops `<br>`).

| Case | Real 3.3.9 after the click or tick | Here |
|---|---|---|
| `{(link: "a")[ x ] y}` | `x  y` | `x y` |
| `{(live: 100ms)[(stop:) x ]}` | `x ` | `x` |
| `{x (live: 100ms)[(stop:) ]}` | `x ` | `x` |
| `{(live: 100ms)[(stop:) x ]}y` | `x y` | `xy` |
| `{(link: "a")[(replace: ?h)[ y ]] \|h>[z]}` | ` y` | `y` |
| `{(link-repeat: "a")[ x ] y}`, clicked twice | `a x x  y` | `a x x y` |

The demo works like the third row. In its battle passage (`differential/demo-battle.twee`), the live hook's content is `\n    (stop:)\n($br_state_machine:)\n\n `. Real's page keeps a lone `" "` text node at the end of it (`differential/demo-live-hook-nodes.txt`). The last text before the hook is the battle line the same render just wrote into `?battletext`, which ends in `appeared!`. So the hook's leading whitespace survives the left trim, is trimmed off the end, and comes back as one space, because the hook has text of its own after its first node.

## How reference does it

**Lists** (`ts/markup/patterns.ts`, `ts/markup/markup.ts`, `ts/renderer.ts`):
- The patterns are `bulleted = ws(\*+)mws` and `numbered = ws((?:0\.)+)mws`. Depth is the number of `*`s, or of `0.` pairs.
- Like every block rule, they take `blockRuleConstraint`, which allows them only at the start of the source or after a `br`, `hr`, `bulleted`, `numbered`, `heading`, `align`, `column` or `escapedLine` token, and `cannotFollowText`. So `* * a` is two items, `[* a]` and `''* a''` are not lists, and `(print: "* a")` is, because printed markup lexes fresh.
- The renderer's `case 'bulleted'` marches through the sibling tokens:
  - A `br` closes the item with `</li>`, and the list unless the next token is the same type. So the line break that ends an item never renders.
  - A token of the same type adjusts depth by opening or closing tags, then opens an `<li>`, with or without a `br` before it.
  - Anything else renders inside the item, including a marker of the other type. That renders as a list of its own with one empty item: `* 0. a` is `⟦ul:⟦li:⟦ol:⟦li:⟧⟧a⟧⟧`.
  - At the end it writes `</ul>` depth+1 times, one more than it opened.
- The march runs over one token level after the lexer has folded paired tokens. So a `''…''` or `{…}` pair that crosses an item's line break belongs to that item, and a hook's content is a separate render.
- The march renders each other token on its own, so an unclosed `[=` or `{=` inside an item gets nothing to hold, since its case takes "the rest of the tokens" from a one-token list. Measured: `* (if: false)[=b` / `* c` shows `b` and `c`, and `* a {=  b   c` keeps its spaces. Outside a list it still takes the rest of the passage.
- Collapsing works only on text nodes, so `<ul>` and `<li>` are transparent to it. `{\n* a  b \n* c\n}` is `⟦ul:⟦li:a b ⟧⟦li:c⟧⟧`.

**Collapsing** (`ts/section.ts` `execute`, `collapse` in `ts/utils/renderutils.ts`; the 3.3.9 build's `section` and `utils/renderutils` modules agree, in `differential/`):
- At the end of every render, `execute` calls `collapse(dom)` when the render's original target hook is inside a `tw-collapsed` or `[collapsing=true]`. It then collapses each `tw-collapsed` or `[collapsing=true]` element in the new content, found with `findAndFilter`. That uses jQuery's `.add`, which in 3.3.9's jQuery 3.7.1 is `uniqueSort(merge(…))`, so they go in document order, outer first. The passage's own spans are collapsed once, when the passage finishes. Later renders never revisit them.
- A revision whose source hook is outside collapsing gets `[collapsing=false]` on its target (`macrolib/enchantments.ts`). Here that's the `Preserve` layer `RenderHookInto` already adds.
- `collapse(elem)`:
  - `beforeNode` is the last text node before `elem[0]` in document order, and `afterNode` the first text node after `elem[0]`, outside it. Each counts only if some ancestor is collapsing, passing through `[collapsing=false]`. Both are found before `<br>`s are converted, so a line break is no text node then.
  - `elem[0]` is the render's first top-level node of any kind, so `afterNode` is usually the render's own next text. With `[(stop:) x ]`, `elem[0]` is `(stop:)`'s empty `<tw-expression>`, and ` x ` is the after node. With `[ x ]`, `elem[0]` is ` x ` itself, and the after node must come from later on the page.
  - Non-raw `<br>`s become spaces, and runs of whitespace become one space.
  - Walking from `beforeNode`, a node starting with a space loses it if the previous node ends in whitespace or is empty. Text inside a `tw-expression`, `tw-verbatim` or `[collapsing=false]`, up to the nearest collapsing ancestor, is solid: it's left alone and counts as non-whitespace.
  - Trailing whitespace is trimmed back to the last visible node, counting what was removed. If anything was removed and `afterNode` exists, one space goes back on the last text node.
  - Finally `elem[0].normalize()` drops empty text nodes, but only inside `elem[0]`. A span's collapse cleans the whole span, while a render's collapse usually leaves its emptied nodes on the page, where a later render can meet them.

## Design

### Lists

1. **Tokenizer.** Add a new `TokenType.ListMarker`, whose `Value` is the marker as written, leading and trailing whitespace included. It's recognised where bullets are today, plus `(0.)+` runs. `AtBlockStart` also holds after a `ListMarker`, which replaces the `_blockStartPos` workaround.
2. **Parser.** A marker parses to an internal marker node in its sibling list. Each level folds in this order: paired inline formats and `{…}` as now, then lists, then unclosed `[=`/`{=` markers. Inside an item, an unclosed marker becomes an empty hook or span and the rest of the item stays the item's. Outside lists, it takes the rest of the level as now.
   - A marker starts a list. An item runs to the next newline at its level, which the list consumes, to the next marker of the same kind, which starts an item with no newline, or to the end of the level.
   - After an item's newline, a same-kind marker continues the list. Anything else ends it.
   - A marker of the other kind inside an item becomes its own list with one empty item.
   - Grouping also runs inside each `FormatNode` and `CollapsedNode` the fold creates, recursing the way `ApplyUnclosedMarkers` does, within the parser's nesting limit.
   - **Hook attachment** currently looks past newlines. It's decided at the macro or variable, from the parser's state at that level. On a line that began with a marker at this level, the lookahead stops at a newline, unless the macro itself sits inside a paired format or `{…}` opened after the item's marker and still open there. A span opened on an earlier line holds the whole list, so a line break inside it still ends the item. Real 3.3.9 shows `* (if: true)` then `[x]` on the next line as an error with `x` after the list, but attaches inside `* ''(if: true)` / `[x]''`. In `* (if: true) ''x` / `[y]''` the macro is outside the span, so it doesn't attach, and the `''` stops the lookahead anyway.
   - **Parse-error recovery** consumes the newline after a broken construct into the error node's source. A `ParseErrorNode` whose source ends in a newline therefore ends its item, as the newline would have. The printer writes that source, newline included, so the next marker still starts a line.
3. **AST.** Add public `ListNode` (`Ordered`, `Items`) and `ListItemNode` (`Depth`, `Marker`, `Children`, `EndsLine`), and `IBodyVisitor.Visit(ListNode)`.
   - Adding a member to the public `IBodyVisitor` breaks any visitor written outside the library. netstandard2.0 has no default interface methods, so the CHANGELOG says so.
   - `BodyWalker` recurses into items, which covers branches, `GetBrokenLinks`' references and parse errors. `BodyRenderer.ContentCost` counts list children, so a hidden hook holding a list is charged for it.
   - `MarkupPrinter` writes each marker as written, its children and the consumed newline. A dirty passage round-trips structurally, as dirty passages already do, since children print canonically.
4. **Renderer.** `Visit(ListNode)` emits `<ul>` or `<ol>`, the depth changes as reference makes them, then `<li>`, the children and `</li>`, and finally one close per level still open. Each tag is its own `Html` event, always balanced. Items get no temp or conditional scope of their own, as in reference, so `* (if: false)[a]` / `* (else:)[b]` pair.
5. **Hosts** receive a list as `Html` events, the way they already receive `<hr>`. `IRenderOutput` doesn't change.

**The extra close.** Reference's surplus `</ul>` closes an enclosing open list in the same render string. That happens with a list inside `{…}` or inline formatting inside a list item, or inside an author's raw `<ul>`, because none of `<tw-collapsed>`, `<b>` or `<li>` bounds an end tag's scope. Two measured cases show it: `* {` / `* b` / `} c` gives `⟦ul:⟦li:⟦ul:⟦li:b⟧⟧⟧⟧ c`, and `* a {x` / `* b` / `} c` / `* d` gives `⟦ul:⟦li:a x ⟦ul:⟦li:b⟧⟧⟧⟧ c⟦li:d⟧`. In both, ` c` leaves the list, and in the second `d` becomes a stray item. Here the tags balance, so ` c` stays in the item. Where nothing enclosing is open, as with `* ''a` / `* b''`, the surplus close is ignored and the cases already match. I'll record this as divergence #44 rather than imitate a parser accident.

### Collapsing later renders

6. **When.** Add a `MacroContext` flag. The session sets it on the live context once a passage's render, including its whole-page collapse, has finished. Every passage render, including undo, redo, load and a `(goto:)` from a click, starts a fresh context without it. While it's set, renders collapse on their own. A renderer used without a session never sets it.
7. **Where.** Reveals, revisions, live hooks and `(show:)` with a changer render through `BodyRenderer.RenderHookInto`. Plain `(show:)` renders its body directly today, so it moves onto the same path. It renders either into a placeholder, with a whitespace layer around the content when the passage uses `{…}`, or straight into its own target with no layer at all. The second is how a live tick and a `(show:)` reveal render. Neither path changes, and the fast path that opens no layer stays. While the flag is set, the collapse works on what the render added to its container, looking through the layer if there is one, and takes the layer mode from `insideCollapse`. After the hook renders:
   - if it was written inside a span, its rendering is collapsed as `collapse(dom)`;
   - then each `{…}` span anywhere inside the rendering, nested hooks included, is collapsed in document order, outer first, as `collapse` of that span.
   - `(rerun:)` isn't implemented, so there's nothing more to route. A render inside a custom macro body's own tree isn't on the page, so it's skipped. What the macro outputs is collapsed by the render that prints it.
8. **The first node.** Reference's `elem[0]` is the rendering's first top-level node. Here a macro that prints nothing leaves no node, and the builder merges neighbouring text. So while the flag is set, `RenderHookInto` plants an invisible sentinel where `elem[0]` ends, and removes it after the collapse. Leading comments render nothing and are skipped. The sentinel goes:
   - **before** a leading line break, which in reference is a `<br>` and not text;
   - **after** a leading text run, meaning the text nodes up to the first line break, or after a leading hook, link, verbatim span, inline format, `{…}` span or other construct;
   - for a **leading macro or variable**, before its attached hook if that hook renders in place (the changer's `RenderHookInto` call), otherwise after everything it printed.
   The sentinel also stops the leading text merging with what follows it.
9. **`WhitespaceCollapser.CollapseRendered`**, reference's `collapse` over the render tree:
   - **Text nodes as reference sees them.** A line break inside a text node is a `<br>` there, so for the before and after search a node's text is split at line breaks, and empty pieces are no nodes. Line-break-only text is never a before or after node, and the before node's text is its last piece.
   - **Before and after.** The before node is the nearest such text before the rendering. The after node is the nearest after the sentinel, in the rendering or past it. Each counts only if it has a real `{…}` span among its ancestors, looking through `Preserve` hooks. Reference takes the nearest one and then checks it, so a nearer text outside any span means there is none.
   - **Solid text.** A node is solid when a `Preserve` hook, a link or an error lies between it and its nearest real span, or the rendering's own layer. A rendering written outside a span has a `Preserve` mode, so step 7 doesn't collapse it and its own text is solid to any span collapse that reaches it.
   - **Emptied nodes stay.** A render's emptied text nodes stay in the tree, as reference's do, and `RenderTreeFlusher` skips empty text so hosts still get none. A span's collapse removes its own emptied nodes, as `normalize()` on the span does.
   - **Budget.** Each collapse is a page walk, charged with `TrySpendOnTargetWalk`. Past the budget, or past `MaxTargetedTreeDepth`, whatever hasn't collapsed yet stays uncollapsed and the page shows the walk budget's error, as the other walks do.
10. **Hooks inside a later render.** In reference every hook's render ends in its own `execute`, so a hook nested in a later render collapses as soon as it finishes, before the rest of the render runs. `{(live: 100ms)[(stop:)(if: true)[ x ]]}` shows `x` in real 3.3.9: the `(if:)` hook's collapse finds nothing after it and trims the space for good. So while the flag is set, `Visit(HookNode)` collapses each hook it finishes the same way, when the hook is inside a real span. The rendering's own hook is collapsed by step 7 only. Steps 7 and 10 use the same after search, lookahead then page, and a second collapse of the same text changes nothing. A nested hook's `elem[0]` is its own first node, so it gets a sentinel by step 8's rule too.
    - **Not yet rendered.** At that moment reference already has the rest of the render's static text on the page. Expressions and hooks still to run are empty elements. The builder hasn't written any of it yet, so the after search first looks at the siblings still to come. At the hook's level and each enclosing level of the render, it checks for static text already in place: a text node other than a line break, a verbatim span or an error stub, or one of these directly inside an inline format, `{…}` span or list. Text inside a hook, macro or link still to run doesn't count, because those are still empty elements in reference. Only if there's none does it search the page past the render.
11. **Spans, not layers.** A `{…}` span and the layer `RenderHookInto` adds are both `WhitespaceMode.Collapse` today. Add a flag that only `{…}` sets. The passage pass treats both as before. The later-render step counts only real spans, both as "inside collapsing" and as nested spans to collapse, because reference has no element for a layer.
12. **Delete** the whole-page re-collapse after a dispatch and after a tick. The passage render keeps its whole-page pass, which already matches real on 99.98% of snippets.

Walking the design through every collapse case that differs today, Muse's eleven included, predicts each matches real 3.3.9. So does the demo's live hook. The six `(link:)` and `(show:)` cases with text after them come out as `x  y` with two spaces, as in real. `{(link: "a")[ x ]}` keeps its `x`, because ` x ` is its own first node and nothing follows. `{x (live: 100ms)[\n(stop:)\n]}` stays `x`, because the line breaks are no text nodes.

Eight more shapes from the review were measured on real 3.3.9 (`differential/review-*`), and the design predicts all eight. `{(link: "a")[ { x } { y } ] z}` gives `x y  z`, `{(link-repeat: "a")[ ] (link-repeat: "b")[ x ]}` gives `a  b` then `a  b x`, and `{(live: 100ms)[(stop:)(link: "q")[r] ]}` gives `q `. One list shape is the existing unterminated-macro gap: `* (print: ` / `* b` is one item of literal text in real 3.3.9, where here the broken macro is an error and `b` its own item.

**Known approximations**, to measure and record if they show:
- Merged text has one node where reference has several, so a reference node emptied in the middle of a run that's partly visible here has no empty counterpart. That matters only if it's a later render's before node.

## What changes for stories

- A line starting `* ` or `0. ` is now a list, where it used to print literally.
- After a click or live render inside `{…}`, spacing follows reference's, including double and trailing spaces like those in the table above. A browser shows them as one space anyway.

## Tests and verification

- **Tests.**
  - Every measured case becomes a test with real 3.3.9's result: the list cases, including `(if:)` and `(else:)` pairing across items, and the collapse cases, run through clicks and ticks.
  - Add tokenizer, printer round-trip and visitor tests, such as a link in a list item counting as a branch and as a broken link.
  - Add the shapes this review raised, checked on real 3.3.9 first:
    - a broken macro inside an item;
    - `* (if: true) ''x` / `[y]''`;
    - a hidden hook holding a list;
    - a `(click: "…")` string target in text a click just collapsed, clicked again;
    - nested spans in a revealed hook;
    - `{(link-repeat: "a")[ ] (link-repeat: "b")[ x ]}`.
  - Before changing any existing test that asserts a literal `* ` or a whole-page re-collapse after a click, I'll check its case against real 3.3.9.
- **Verification.** It runs against a snapshot of this commit's outputs, already taken. It covers:
  - every earlier real-3.3.9 comparison;
  - the snippet corpus, whose 311 list cases all differ today;
  - the harlowe-demo walks (expected 810 of 810, or each difference explained), Service Suspended and Files-005;
  - the reference specs under both majors and the public story corpus;
  - fuzzing and the small-stack recursion checks.
- **Docs.** I'll update CHANGELOG, README, CLAUDE.md, ARCHITECTURE, TODO (the lists entry goes), MACRO-DIVERGENCES (#39 keeps only its `time` half; #44 is new) and TEST-BENCH-PLAN.
