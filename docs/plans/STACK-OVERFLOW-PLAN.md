# Stack overflow plan

Two kinds of story source still overflow the stack. A stack overflow can't be caught, so it ends the host process. The library's floor is a 512 KB stack (`MacroContext.MaxRenderNesting`'s measurement, and the tests' small-stack threads). The bench's stack suite also runs 256 and 160 KB.

## What crashes

Measured on the committed build, one process per shape at each stack size:

| Shape | 512 KB | 256 KB | 160 KB |
|---|---|---|---|
| `(print: 1 + 1 + …)`, 1,500 operands | crash | crash | crash |
| the same with `-`, `and`, `is`, `'s`, mixed `+ 1 * 1`, strings, arrays, in `(set:)`, in a lambda: 10,000 operands | crash while loading | crash while loading | crash while loading |
| a changer doubled 11 times (2,048 layers) | crash | crash | crash |
| a changer grown by one layer 5,000 times in a `(for:)` | crash | crash | crash |
| 1,500 changers joined inline, `(a)+(b)+…[x]` | ok | crash | crash |
| 120 nested hooks under a 32-layer changer | crash | crash | crash |
| a 32-layer changer on a passage that displays itself | ok | ok | crash |

`num-type num-type … _a` is a chain too, since `-type` is left-associative, and crashes the same way.

Fine at every size: 127 unary operators, `of` chains, nested macro calls and parentheses (the expression parser stops at 128 levels); lists, inline formatting, `{…}` and hooks, which the body parser bounds; one `(text-style:)` with 5,000 names, which is one layer; a 512-layer changer used by `(enchant:)`, `(change:)`, `(click:)`, `(show:)`, a link and a save.

**Operator chains.** The parser builds `a + b + c` in a loop, so the tree is as deep as the chain is long, and `ExpressionEvaluator`, `MarkupPrinter` and `BodyWalker` recurse once per operator. The collectors run while a story loads (`HarlowePassage.Branches` is collected at parse), so a longer chain crashes `AddPassage` and `TweeReader` before anything renders, measured at 4,000 operands on 512 KB and 2,500 on 256 KB. The HTML loader parses passages the same way.

**Style layers.** Each layer of a changer becomes its own `RenderStyleNode`, nested, and the passes after a render (`InteractionPass.StripWraps`, `RenderNodes.UnwrapWhere`) recurse through them. Hooks multiply it: every nested hook adds the changer's whole stack of layers. Doubling also runs away in memory: 25 doublings is 33 million patches at the `+`.

## The fix

**1. Operator chains are walked in a loop.** `ExpressionEvaluator.Visit(BinaryOpNode)`, `MarkupPrinter.Visit(BinaryOpNode)` and `BodyWalker.Visit(BinaryOpNode)` follow the left spine with a loop: collect the nodes down the left side, handle the bottom operand, then each node's operator and right side from the bottom up.

- In the evaluator the spine is every operator except `to`, `into` and `of`, which evaluate the right side first or never. The code that follows `Evaluate(node.Left)` today moves unchanged into `Combine(node, left)`, so every operator runs the same code, in the same order, with the same error short-circuit. `'s` and `-type` don't evaluate their right side, a property name and a variable name, and `Combine` keeps that. A `-type` whose type isn't an operator, such as a spread `...num`, stays a leaf evaluated whole, as now.
- `EvaluationDepth`, which `CustomMacroRunner` checks against `MaxEvaluationDepth` (400, below the 600 levels measured to fit 512 KB), stops counting spine levels, since they no longer use stack. A custom macro first in a chain of 500 is then no longer refused as too deep. That's the one intended change in behaviour.
- In the printer the spine continues while the left child prints without parentheses. A left child in parentheses recurses as now; parentheses come from source parentheses, which the parser caps.
- In `BodyWalker` the bottom operand is visited first and then each right side, the order the collectors see now. No collector overrides `Visit(BinaryOpNode)`.

Right sides still recurse. A left-associative operator's right side binds tighter, so nesting through right sides is bounded by the precedence levels, and `of`'s right-associative chain by the parser's 128.

A length limit would be simpler, but real Harlowe has none, and a limit that fits 512 KB with custom macros also running would be a few hundred operands.

**2. A changer's layers are one node.** `RenderStyleNode.Style` becomes `Styles`, outermost first. Wherever a changer wraps content in its k layers, one node holds all k: `Changer.RunStyles`, `Changer.RenderInPlace` and `ShowMacros.RevealHook` through a new `RenderTreeBuilder.PushStyles`, and `Changer.ApplyTo`, `Changer.WrapInStyles` and the interaction pass's armed styles, which build nodes directly. Inline formatting and other `IRenderOutput.PushStyle` callers keep one layer per node. Five things must hold:

- One `PopStyle` closes a `PushStyles`. Popping once per layer would close the hooks around it.
- No layers means no node, or a plain link label would flush as `BeginLink`/`EndLink`.
- `RunStyles` opens one node only on a `RenderTreeBuilder`; any other `IRenderOutput` still gets a `PushStyle` per layer.
- Every layer is copied onto the tree, as `PushStyle` copies one now, and a null layer stays tolerated.
- `RenderTreeFlusher` emits k `PushStyle` events, outermost first, the children and k `PopStyle`, in a loop, so hosts receive what they do now.

The tree then grows by one level per changer applied, which is what `MaxRenderNesting` and `MaxTargetedTreeDepth` already assume. Unwrapping by enchantment or region tag takes the whole node, whose layers all came from that one changer. `WalkCost` and `SpliceCost` count the node once, so walk budgets cost a many-layered changer less than now, and the page-depth limit takes more layered changers to reach.

`RenderStyleNode` is public, and a custom macro can reach the tree through `MacroContext.LiveRoot`, so this is a breaking change to the render tree, noted for hosts.

**3. A changer holds at most 1,024 patches.** 1,024 is allowed and 1,025 isn't. `ExpressionEvaluator.ComposeChangers`, which both the `+` operator and inline `(a)+(b)` chains use, returns an in-prose error past it; an inline chain reports it as it reports a right side that isn't a changer. `BodyRenderer`'s attachment of a changer to a custom command (`attached.Compose(command.Changer)`) checks first and skips the command with the error, as it skips one it can't run. `Changer.CanCompose` does the counting. The public `Changer.Compose` doesn't check: the limit is for what a story can build, and a host composing in code picks its own sizes.

A patch, not a changer, is what's counted, since each costs a step of every descriptor build and a piece of the saved source: `(text-style: "none", "bold")` is two. A saved changer re-evaluates its source when it loads, so a save holding more than 1,024, which an older build could write, fails to load, all or nothing as any failed load does.

This is about memory and time rather than stack. 1,024 is ten doublings, far beyond any written composition, enough for a story that adds a style on every visit, and between the 512 layers measured fine and the 2,048 that crashed everywhere.

## Not in scope

Recorded in `TODO.md`:

- Values that grow without bound: a string doubled 30 times throws `OutOfMemoryException` out of `Render`, and an array doubled 30 times takes eight seconds.
- Output size: a 1,024-layer changer enchanting thousands of matches emits millions of events. The same holds today for a 512-layer one.
- Render recursion under 512 KB: about 110 nested hooks that each apply a changer, `(if: true)[` as much as `(text-style:)[`, overflow 160 KB while they render. The passes after a render walk 512 levels on 160 KB, so it's the render's own recursion, which this plan doesn't change and which fits 512 KB.
- An `IExpressionVisitor` outside the library, or a hand-built tree of left-nested `of`, still recurses per level.

## Steps

Each is verified before the next. "The bench" is `node tools/bench/verify.mjs`: every count must hold, and since hosts receive the same events, it must report no result changed since the run on the committed build. "All three sizes" is the measuring harness's shapes in their own processes, which step 4 moves into the stack suite.

1. Operator chains, `-type` included. Tests at 512 KB that the chain shapes render, load, report broken links and write to Twee and back; that mixed chains group, short-circuit and print as before; that operands run left to right (a custom macro logging each call); that `it` reads through a chain; that references in a chain are collected in source order; and that a custom macro deep in a chain isn't too deep. **Done when** every chain shape runs at all three sizes and the tests and the bench pass.
2. One node per changer. Tests that a composed changer is one node and flushes a push and a pop per layer, that `PushStyles` copies each layer and opens nothing for none, that many-layered changers render at 512 KB through hooks, nested hooks, `(enchant:)` of nested hooks, `(change:)`, `(click:)`, a link and `(show:)`, that an enchantment is reapplied rather than doubled after a click, that an armed region loses all its layers when clicked, and that thousands of two-layer changers on one hook still stop at the page-depth limit. Their events are compared with the committed build's. **Done when** every changer shape runs at all three sizes except those in the 160 KB render-recursion item above, the events match, and the tests and the bench pass.
3. The patch limit, with tests either side of 1,024 through `+`, an inline chain and a custom command; that two-patch changers reach it at half the doublings; that 30 doublings stop quickly; that a saved 1,024-patch changer loads; and that a save past the limit doesn't, leaving the session as it was. **Done when** they pass with the bench.
4. The new shapes in the bench's stack suite (`tools/Twinelike.Probe/Stack.cs`), `verify --all`, and the docs: `ARCHITECTURE.md` (changers, render tree, limits), `MACRO-DIVERGENCES.md` #35 (the patch limit), `CHANGELOG.md` (the fix, the render-tree change for hosts, old saves, and the known limitation replaced) and `TODO.md`.
5. A Muse review of the code, then its confirmation of the fixes.

## Result

Implemented as above. Every shape in the table, and the `-type` chain, now runs at all three sizes, except nested changer-applying hooks at 160 KB, which the scope covers. All 4,701 tests pass on net48 and net8.0, in Debug and Release. The bench reports the same counts and no result changed since the committed build's run. Fuzzing of 4 seeds × 200,000 stories finds no exception or hang, the stack suite's 64 shapes finish on 512, 256 and 160 KB, and the walks and specs are unchanged. 32 cases of layered changers meeting every pass, plus chains with side effects, `it` and `-type`, give the same events on the committed build and this one, line for line. The one difference found is the intended one: a custom macro first in a 500-operator chain now runs instead of being refused.

Running the net48 tests repeatedly turned up an older problem. `NestedRenderLimitTests.DeepHooksInsideRecursion_FitASmallThreadStack`, 120 nested `(for:)` hooks with the parser on top for a printed string, needs about 450 KB of its 512 KB thread in a Debug build, on the committed build as on this one, and on .NET Framework about one run in 200 overflowed and took the test process down; alongside the new chain tests, about one full run in three did. A crash dump named it. Release needs about 390 KB, the same on both builds, so the test keeps 512 KB in Release, which CI runs, and has 1 MB in Debug. Ten full net48 runs then passed. That's the render-recursion item in the scope above: the heaviest chain per level uses three quarters of the floor even in Release.

Muse reviewed the plan (not ready: the `-type` chain, the 160 KB attribution, the limit's details and the tests, all since answered), then the code (ready, with wording and test assertions to tighten), and confirmed those fixes: **ready**.
