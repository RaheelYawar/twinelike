# Harlowe Parser — C# Library

## Project Overview
A C# library (netstandard2.0) for parsing and running Twine/Harlowe interactive fiction stories. Targets game engines (Unity, Godot, etc.) as the primary consumer — they implement `IRenderOutput` to drive whatever text-rendering layer they have (TextMeshPro tags, BBCode, plain text, etc.). Accepts both Twine 2 HTML exports and Twee 3 source; emits Twee 3 back out.

## Build & Run
- **Framework:** Library targets `netstandard2.0` for maximum consumer reach (Unity 2018.1+, Godot 3/4, .NET Framework 4.6.1+, .NET 5+, Mono, Xamarin). Test project targets `net48`. Both are SDK-style csprojs.
- **Build:** `dotnet build harlowe-parser.sln`
- **Test:** `dotnet test harlowe-parser.sln`
- **Output:** Library DLL (`harlowe_parser.dll`)
- The library csproj sits at the repo root and uses `<DefaultItemExcludes>` to keep it from globbing the test folder.

## Architecture
- Entry points: `new Harlowe(htmlText)` for the Twine 2 HTML export, or `new TweeReader().Read(tweeText)` for the plain-text Twee 3 form. Both paths populate the same `Harlowe` story object. Outbound: `new TweeWriter().Write(story)` emits Twee 3 source (the only write format; HTML emit is out of scope, since Twine 2 is the natural HTML producer).
- Two parsing layers. **Layer 1 (host)** has two front-ends: HtmlAgilityPack extracts `<tw-storydata>` / `<tw-passagedata>` from HTML and HTML-entity-decodes the inner text via `HtmlEntity.DeEntitize`; `TweeReader` splits Twee source on `:: Name [tags] {position}` headers at column 0, special-cases `:: StoryTitle` and `:: StoryData` (with a hand-rolled `JsonReader`), and synthesizes sequential pids since Twee has none. **Layer 2 (Harlowe markup)** is shared: `HarloweTokenizer` → `HarloweBodyParser` (which delegates to `HarloweExpressionParser` at every `MacroOpen`) → `PassageBody` AST stored on `HarlowePassage.Ast`. `HarlowePassage.Body` (string) and `HarlowePassage.Branches` are derived from the AST by visitors. Both front-ends populate `HarlowePassage.RawBody` so `TweeWriter` can emit clean passages verbatim (lazy reserialization — only passages whose `IsDirty` flag is set re-canonicalize via `MarkupPrinter`).
- The AST splits node types between **body** (prose, hooks, links, command macros — `IBodyNode`) and **expression** (macro arguments — `IExpressionNode`). Same `(name: ...)` syntax can become either `MacroNode` or `MacroCallNode` depending on position, which is why the split exists. Both trees use the visitor pattern.
- The tokenizer (`HarloweTokenizer`) covers every `TokenType` and every Harlowe operator from the manual's precedence table — including multi-word operators (`is not`, `is in`, `is a`, `does not contain`, etc.) fused at lex time, the whitespace-sensitive `'s`, the digit-leading `2bind`, and the `-type` TypedVar suffix. It uses a mode stack of `Frame { Mode, ParenDepth }` (Body vs. Expression with per-frame paren depth so `)` is disambiguated as `ParenClose` vs `MacroClose`) and an `_inLink` flag that suppresses macro/hook markup inside `[[…]]`.
- Parsers live in `HarloweParser/Parsing/`. Both share a `TokenCursor` so the body parser can hand the cursor to the expression parser at every macro-arg list. `HarloweExpressionParser` is precedence-climbing using the manual's operator order (lower order = tighter binding); all binary ops are left-associative and `and`/`or` share order 13.
- Branch links use Harlowe's `[[display text->passage name]]` syntax. Because `HtmlEntity.DeEntitize` runs before tokenization, the tokenizer sees raw `->` rather than the `-&gt;` it would otherwise be encoded as.
- **Runtime layer** lives under `HarloweParser/Runtime/`. Pipeline: `ExpressionEvaluator` (an `IExpressionVisitor` returning `HarloweValue`) and `BodyRenderer` (an `IBodyVisitor` pushing through `IRenderOutput`) share a `MacroContext` (variable store, RNG, pending-goto flag, last-conditional flag, render-passage callback). `MacroRegistry` is a flat name→`IMacro` directory that also implements `IMacroInvoker` so the evaluator can dispatch nested `MacroCallNode`s. The `to`/`into` assignment operators are handled by the evaluator (mutate via `IVariableStore`); `(set:)`/`(put:)` are nearly no-op macros because the work has already happened by the time the registry sees them. `(if:)`/`(unless:)`/`(else:)` write `LastConditional` on the context; the body renderer reads it to decide hook rendering and resets it after any non-conditional macro so `(else:)` only pairs with the immediately preceding conditional. Errors propagate as `HarloweValue.Error` through the evaluator (every operator short-circuits) and exit through `IRenderOutput.Error` rather than thrown exceptions.
- All source lives in `HarloweParser/`; data models are simple public-field classes.

## Key Files
- `HarloweParser/Harlowe.cs` — Main entry point: metadata extraction, passage indexing, AST wiring. Editing API: story-level fields (`StoryName`, `StartNode`, `Creator`, `CreatorVersion`, `Ifid`, `Format`, `FormatVersion`, `StoryDataExtras`) have public setters; public parameterless ctor for from-scratch construction; `AddPassage(passage)` (auto-synthesizes pid when null/empty), `RemovePassage(name)`, `RenamePassage(oldName, newName)` (re-keys the lookup), and `Passages` enumerator.
- `HarloweParser/BodyVisitors.cs` — internal `BranchCollector` and `BodyTextRenderer` `IBodyVisitor` implementations used by both the HTML loader and `TweeReader` to derive `HarlowePassage.Branches` and `HarlowePassage.Body` from the AST. Each exposes a static one-shot helper (`Collect(ast)` / `Render(ast)`). Previously duplicated as private nested classes in `Harlowe.cs` and `TweeReader.cs`; centralized so a new body-AST node only needs one `Visit` override per visitor across the codebase.
- `HarloweParser/HarlowePassage.cs` — Passage model (Pid, Name, Body, Tags, Branches, Ast, RawBody, Position, IsDirty).
- `HarloweParser/Twee/` — Twee 3 read + write. `TweeReader` (header parsing + body routing through the existing tokenizer/body parser), `JsonReader` and `JsonWriter` (hand-rolled minimal JSON for the `:: StoryData` special passage; pretty-printed output; avoids dragging Newtonsoft/System.Text.Json into consumer projects), `MarkupPrinter` (`IBodyVisitor`+`IExpressionVisitor` walking AST back to canonical Harlowe markup; precedence-driven parens; smart-quoted strings with throw-on-both-quotes pending tokenizer escape support), and `TweeWriter` (story → Twee text; lazy reserialization via `HarlowePassage.IsDirty`; overlays typed StoryData fields onto `Harlowe.StoryDataExtras` so unknown JSON keys round-trip; re-escapes body lines starting with `::` to `\::`).
- `HarloweParser/Branch.cs` — Link model (Text, Name).
- `HarloweParser/Tokens/` — `ITokenizer`, `Token`, `TokenType`, and `HarloweTokenizer` (full implementation including all Harlowe operators, multi-word fusion, `'s`, `2bind`, `-type`).
- `HarloweParser/Parsing/` — `TokenCursor`, `IExpressionParser`/`HarloweExpressionParser` (precedence-climbing), `IBodyParser`/`HarloweBodyParser` (recursive descent with hook attachment via whitespace-skipping lookahead).
- `HarloweParser/Ast/Body/` — Body AST nodes (`MacroNode`, `HookNode`, `LinkNode`, `TextNode`, `NewlineNode`, `VariableNode`, `HtmlNode`, `ChangerChainNode`, `PassageBody`) with `IBodyVisitor`. `ChangerChainNode` carries an expression (typically a `+`-chain of `MacroCallNode`s or a `VariableRefNode`) plus an optional attached hook — the runtime evaluates the expression, applies as a Changer if the result is one, otherwise falls back to value-then-hook.
- `HarloweParser/Ast/Expression/` — Expression AST nodes (`LiteralNode`, `IdentifierNode`, `VariableRefNode`, `BinaryOpNode`, `UnaryOpNode`, `MacroCallNode`, `ArrayNode`, `DatamapNode`, `DatasetNode`) with `IExpressionVisitor`.
- `HarloweParser/Runtime/` — Complete v1 runtime. `HarloweValue`/`HarloweValueKind` (tagged union with `Error` variant), `IVariableStore`/`HarloweVariableStore` (story+temp namespaces, `it` slot, deep-copy snapshots), `ExpressionEvaluator` (visitor returning `HarloweValue`, error short-circuit on every operator), `IEvaluationContext` (`time`/`visits`/`passage` for the evaluator), `IMacroInvoker` (decouples evaluator from registry), `IMacro`/`MacroContext`/`MacroRegistry`, `IRenderOutput`/`BufferedRenderOutput`/`HtmlRenderOutput`, `BodyRenderer`, `RenderResult` (output DTO), `StyleSpec` (semantic styling description emitted by changers), and `StorySession` (top-level engine surface).
- `HarloweParser/Runtime/Macros/` — macro implementations, one class per file: `SetMacro`, `PutMacro`, `PrintMacro`, `IfMacro`, `ElseMacro`, `UnlessMacro`, `GotoMacro`, `DisplayMacro`, `RandomMacro`, `EitherMacro`, `AMacro`, `DmMacro`, `ModuloMacro`, `TextMacro`, `NumMacro`, `HistoryMacro`, plus the v2.1A changer macro `TextStyleMacro`. `StandardMacros.RegisterAll(registry)` wires them onto a fresh registry.
- `HarloweParser/Runtime/Changer.cs` — v2.1 changer primitive. Flat list of `StyleSpec` layers (semantic, engine-agnostic — bold/italic/underline/strikethrough flags + color/background/font/size value fields); `Compose(other)` concatenates; `Apply(output, renderHook)` emits one `IRenderOutput.PushStyle` per layer, runs the hook, then emits matching `PopStyle` calls. Engine integrations consume `PushStyle(StyleSpec)`/`PopStyle()` and translate to whatever their text renderer accepts (Unity TMP rich-text, Godot BBCode, ANSI, etc.).
- `HarloweParser/Runtime/StyleSpec.cs` — semantic styling description. Public-field class with named flags + value fields, structural equality, `IsEmpty` shortcut. Engine-agnostic shape — no HTML coupling.
- `HarloweParser/Runtime/HtmlRenderOutput.cs` — adapter `IRenderOutput` that wraps an inner output and translates `PushStyle`/`PopStyle` events into HTML tags. Bold/italic/underline/strike alone fold to short tags (`<b>`/`<i>`/`<u>`/`<s>`); any value field present collapses to a single `<span style="...">` carrying inline CSS. Static `EscapeAttribute` HTML-escapes user-supplied values before embedding into the attribute. Web consumers wrap their output in this; tests asserting HTML output route the same way.
- `HarloweParser.Tests/` — direct tests for tokenizer, expression parser, body parser, end-to-end HTML/Twee parsing, runtime (value/store/evaluator/registry/renderer/session), changer pipeline, macros (v1 set + `(text-style:)`), and Twee read/write/markup-print round-trips.
- `TestFiles/testFile.html` — sample Harlowe story (8 passages; some links target absent passages — useful for testing tolerant parsing).

## Code Conventions
- **Namespace:** `Harlowe`
- **Indentation:** 2 spaces
- **Braces:** Allman style (opening brace on its own line)
- **Private fields:** `_camelCase` prefix
- **Public properties:** PascalCase; use expression-bodied members where concise (e.g., `=> _passages.Count`)
- **Data models:** Use public fields (not properties) for simple DTOs (`HarlowePassage`, `Branch`)
- **Null handling:** Return `null` or `string.Empty` for missing lookups, not exceptions
- **No LINQ usage** — the codebase uses explicit loops and Dictionary lookups

## Error Policy
In-prose errors, never exceptions. Mirrors Harlowe's authoring model: a single bad expression renders an inline error message at the spot it happened and the rest of the passage continues. `HarloweValue.Error` propagates through the evaluator (every operator short-circuits on it); when an Error value reaches the renderer it goes through `IRenderOutput.Error(message)` instead of being printed. No exceptions on the runtime hot path — engine integrations don't want `try/catch` around every render call.

## Known TODOs
- Tokenizer string literals do not handle escape sequences (`\"`, `\\`). Load-bearing for `MarkupPrinter`: a string containing both `"` and `'` cannot be round-tripped — the printer throws `HarloweParseException` rather than emit un-reparseable output. Once tokenizer escapes land, switch `MarkupPrinter.AppendStringLiteral` to always-double-quote with backslash escapes and drop the smart-quoting branch.
- `BodyTextRenderer` (in `BodyVisitors.cs`) drops `MacroNode` content from the rendered body string. Acceptable for current consumers; if a downstream needs macros in the rendered prose, add a printer that round-trips macros via the AST.

## Roadmap

**Shipped** (full implementation details in git history):
- **v1** — runtime baseline: `HarloweValue`, `HarloweVariableStore`, `ExpressionEvaluator`, `MacroRegistry` + 16 v1 macros, `IRenderOutput`/`BufferedRenderOutput`, `BodyRenderer`, `StorySession`.
- **v1.1** — `'s`/`of`/`its` property access; multi-step undo (`Stack<SessionSnapshot>`); `(history:)` macro.
- **v1.2** — ordinal indexing (`1st`/`Nth`/`last`/`Nthlast` on arrays + strings).
- **v1.3** — Twee 3 read/write parity with HTML; `MarkupPrinter` (canonical body emit with precedence-driven parens, smart-quoted strings); `TweeWriter` with lazy reserialization (clean passages emit `RawBody` byte-for-byte, dirty passages re-canonicalize); public editing API (`AddPassage`/`RemovePassage`/`RenamePassage` + ctor/setters).
- **v2.1A / v2.1A.1** — Changer foundation: `HarloweValueKind.Changer`, `Changer.Compose`/`Apply`, `(text-style:)` macro, `BodyRenderer` changer-with-hook intercept; `ChangerChainNode` for body-position composition (`(m1)+(m2)[hook]`) and stored-changer-then-hook (`$var[hook]`).
- **v2.2** — semantic render events: refactored `Changer` to carry `List<StyleSpec>` instead of HTML wrapper strings. `IRenderOutput` gained `PushStyle(StyleSpec)`/`PopStyle()` channels. New `HtmlRenderOutput` adapter translates events back to HTML for web consumers. Engine-agnostic shape so Unity TMP / Godot BBCode / etc. can map directly.

**736 tests, all passing.**

**Next: v2.3 — lambdas + lambda-consuming macros.** See `v2.3-lambdas-plan.md` for sub-slices, AST/value model, parser strategy, and open design questions. Ships `(find:)`/`(altered:)`/`(folded:)`/`(all-pass:)`/`(some-pass:)`/`(none-pass:)`/`(rotated-to:)`/`(sorted:)`/`(for:)` plus the lambda foundation. Defers HookName-based revision macros, event/live system, and custom `(macro:)` to later slices.

**Further out:**
- More styling changers — `(text-color:)`/`(background:)`/`(font:)`/`(text-size:)` plus the broader `(text-style:)` named set. Drop-in on the StyleSpec model.
- HookName + revision macros — `(click:)` family (7 variants), `(change:)`, `(enchant:)`. Need named-hook targeting + click event dispatch.
- Event/live system — `(event:)`, `(live:)`, `(trigger:)`. Need async/live re-evaluation.
- Custom `(macro:)` + `(output:)`. Explicitly advanced per Harlowe docs; lower priority for typical stories.
- Storylets, dataset evaluation, property assignment (`(set: $person's name to "Bob")`), `(unpack:)` destructuring.
