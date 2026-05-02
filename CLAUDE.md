# Harlowe Parser — C# Library

## Project Overview
A C# library that parses Twine/Harlowe interactive fiction stories exported as HTML. It extracts story metadata, passages, and branching links into structured objects.

## Build & Run
- **Framework:** Library targets `netstandard2.0` for maximum consumer reach (Unity 2018.1+, Godot 3/4, .NET Framework 4.6.1+, .NET 5+, Mono, Xamarin). Test project targets `net48`. Both are SDK-style csprojs.
- **Build:** `dotnet build harlowe-parser.sln`
- **Test:** `dotnet test harlowe-parser.sln`
- **Output:** Library DLL (`harlowe_parser.dll`)
- The library csproj sits at the repo root and uses `<DefaultItemExcludes>` to keep it from globbing the test folder.

## Architecture
- Entry point: `Harlowe` class constructor takes an HTML string and parses it.
- Two parsing layers. **Layer 1 (HTML host)** uses HtmlAgilityPack to extract `<tw-storydata>` / `<tw-passagedata>` elements and their attributes; the inner text is then HTML-entity-decoded via `HtmlEntity.DeEntitize`. **Layer 2 (Harlowe markup)** is the new pipeline: `HarloweTokenizer` → `HarloweBodyParser` (which delegates to `HarloweExpressionParser` at every `MacroOpen`) → `PassageBody` AST stored on `HarlowePassage.Ast`. `HarlowePassage.Body` (string) and `HarlowePassage.Branches` are now derived from the AST by visitors inside `Harlowe.cs`.
- The AST splits node types between **body** (prose, hooks, links, command macros — `IBodyNode`) and **expression** (macro arguments — `IExpressionNode`). Same `(name: ...)` syntax can become either `MacroNode` or `MacroCallNode` depending on position, which is why the split exists. Both trees use the visitor pattern.
- The tokenizer (`HarloweTokenizer`) covers every `TokenType` and every Harlowe operator from the manual's precedence table — including multi-word operators (`is not`, `is in`, `is a`, `does not contain`, etc.) fused at lex time, the whitespace-sensitive `'s`, the digit-leading `2bind`, and the `-type` TypedVar suffix. It uses a mode stack of `Frame { Mode, ParenDepth }` (Body vs. Expression with per-frame paren depth so `)` is disambiguated as `ParenClose` vs `MacroClose`) and an `_inLink` flag that suppresses macro/hook markup inside `[[…]]`.
- Parsers live in `HarloweParser/Parsing/`. Both share a `TokenCursor` so the body parser can hand the cursor to the expression parser at every macro-arg list. `HarloweExpressionParser` is precedence-climbing using the manual's operator order (lower order = tighter binding); all binary ops are left-associative and `and`/`or` share order 13.
- Branch links use Harlowe's `[[display text->passage name]]` syntax. Because `HtmlEntity.DeEntitize` runs before tokenization, the tokenizer sees raw `->` rather than the `-&gt;` it would otherwise be encoded as.
- All source lives in `HarloweParser/`; data models are simple public-field classes.

## Key Files
- `HarloweParser/Harlowe.cs` — Main entry point: metadata extraction, passage indexing, AST wiring. Hosts the `BranchCollector` and `BodyTextRenderer` visitors that derive `HarlowePassage.Branches` and `HarlowePassage.Body` from the AST.
- `HarloweParser/HarlowePassage.cs` — Passage model (Pid, Name, Body, Tags, Branches, Ast).
- `HarloweParser/Branch.cs` — Link model (Text, Name).
- `HarloweParser/Tokens/` — `ITokenizer`, `Token`, `TokenType`, and `HarloweTokenizer` (full implementation including all Harlowe operators, multi-word fusion, `'s`, `2bind`, `-type`).
- `HarloweParser/Parsing/` — `TokenCursor`, `IExpressionParser`/`HarloweExpressionParser` (precedence-climbing), `IBodyParser`/`HarloweBodyParser` (recursive descent with hook attachment via whitespace-skipping lookahead).
- `HarloweParser/Ast/Body/` — Body AST nodes (`MacroNode`, `HookNode`, `LinkNode`, `TextNode`, `NewlineNode`, `VariableNode`, `HtmlNode`, `PassageBody`) with `IBodyVisitor`.
- `HarloweParser/Ast/Expression/` — Expression AST nodes (`LiteralNode`, `IdentifierNode`, `VariableRefNode`, `BinaryOpNode`, `UnaryOpNode`, `MacroCallNode`, `ArrayNode`, `DatamapNode`, `DatasetNode`) with `IExpressionVisitor`.
- `HarloweParser.Tests/HarloweEndToEndTests.cs` — end-to-end tests against `testFile.html`, including AST-population checks.
- `HarloweParser.Tests/HarloweTokenizerTests.cs` — direct tokenizer tests covering every `TokenType`, mode transitions, line/column tracking, and the full operator surface (multi-word fusion, `'s`, `2bind`, `-type`, etc.).
- `HarloweParser.Tests/HarloweExpressionParserTests.cs` — direct expression-parser tests covering literals, identifiers, every operator level, precedence, grouping, unary prefixes, nested macro calls, and argument lists.
- `HarloweParser.Tests/HarloweBodyParserTests.cs` — direct body-parser tests covering plain content, macros (with/without attached hooks), hooks (anonymous/named), links, and mixed shapes.
- `TestFiles/testFile.html` — Sample Harlowe story (8 passages; some links target absent passages — useful for testing tolerant parsing).

## Code Conventions
- **Namespace:** `Harlowe`
- **Indentation:** 2 spaces
- **Braces:** Allman style (opening brace on its own line)
- **Private fields:** `_camelCase` prefix
- **Public properties:** PascalCase; use expression-bodied members where concise (e.g., `=> _passages.Count`)
- **Data models:** Use public fields (not properties) for simple DTOs (`HarlowePassage`, `Branch`)
- **Null handling:** Return `null` or `string.Empty` for missing lookups, not exceptions
- **No LINQ usage** — the codebase uses explicit loops and Dictionary lookups

## Roadmap
The remaining pipeline, in dependency order:
1. ~~**Tokenizer extension**~~ — **DONE.** Tokenizer now emits one token per logical Harlowe operator: `%` removed, `...` added, `WordOperators` expanded (`in`, `a`, `matches`, `where`, `when`, `via`, `making`, `each`, `its`, `bind`), multi-word fusion (`is not`/`is in`/`is a`/`is not in`/`is not a`/`does not contain`/`does not match`) via lookahead in `TryFuseMultiWordOperator`, `'s` via `TryScanPossessive` (whitespace-sensitive, requires preceding value-like token), `2bind` via `TryScanTwoBind` (digit-leading special case), `-type` via `TryScanTypeSuffix`. 32 new unit tests cover positive and negative cases.
2. ~~**Expression parser**~~ — **DONE.** `HarloweExpressionParser` (in `HarloweParser/Parsing/`) implements precedence climbing over a shared `TokenCursor`, using the manual's "Operators and order-of-operations" precedence table. `BinaryOps` and `UnaryPrefixOps` dictionaries map every operator string to its order. All binary ops are left-associative; `and`/`or` share order 13. Public surface: `ParseExpression(cursor)` and `ParseArgumentList(cursor)`. New AST node `IdentifierNode` was added to `Ast/Expression/` (with a corresponding `IExpressionVisitor.Visit` overload) so bare identifiers like `name`, `it`, `time` aren't conflated with string literals. 39 unit tests in `HarloweExpressionParserTests.cs` cover literals, variables, every operator level, precedence interactions, grouping, unary prefixes, `'s`/`of`/`its`/`-type`/`...`/`bind`/`2bind`, nested macro calls, and argument lists (empty / multiple / trailing comma).
3. ~~**Body parser**~~ — **DONE.** `HarloweBodyParser` (in `HarloweParser/Parsing/`) walks the token stream once, dispatching each token to its body-AST node. Macro arguments are populated directly by `IExpressionParser.ParseArgumentList` — no transitional `RawArgs` shim. Hook attachment uses `SkipBodyWhitespace` to peek past whitespace-only `Text` and `Newline` tokens, so `(if: $x)[hi]`, `(if: $x) [hi]`, and `(if: $x)\n[hi]` all attach the hook. Named hooks: left-anchored `[content]<name|` (consumed in `ParseHookContents`) and right-anchored `|name>[content]` (consumed in `ParseRightAnchoredHook`). Links handle all three forms (`[[t]]`, `[[t->n]]`, `[[n<-t]]`). 22 unit tests in `HarloweBodyParserTests.cs` cover plain content, variables, HTML passthrough, macros, hook attachment (immediate / spaced / newline / blocked by other text), nested hooks, named hooks (left and right), all link forms, and a mixed end-to-end shape.
4. ~~**Wire AST into `Harlowe`**~~ — **DONE.** `Harlowe.cs` now runs each `<tw-passagedata>` body through `HtmlEntity.DeEntitize` → `HarloweTokenizer` → `HarloweBodyParser`, populating `HarlowePassage.Ast`. `HarlowePassage.Branches` is derived by a `BranchCollector` visitor that recurses through hooks and macro-attached hooks. `HarlowePassage.Body` is rendered by a `BodyTextRenderer` visitor (text + newlines + variables-with-sigil + HTML, links and macros omitted). The legacy `ParseBranches` and `&#39;`-only `ParseBody` are retired. New end-to-end tests confirm the AST is populated, the `LinkNode`/`Branch` derivation is correct, and full HTML-entity decoding (not just `&#39;`) is in effect.
5. **Variable store + evaluator** — only after 1–4. This is what unblocks engine integration (`(set:)`, `(if:)`, `(print:)` actually doing something).

## Known TODOs
- Passage tag parsing is stubbed out (`Tags = null`).
- Metadata fields (`_storyName`, `_creator`, `_creatorVersion`) are private with no public accessors.
- Tokenizer string literals do not handle escape sequences (`\"`, `\\`); Harlowe doesn't appear to define them, but if a corpus needs them this is where to add support.
- `BodyTextRenderer` in `Harlowe.cs` drops `MacroNode` content entirely from the rendered body string. The legacy parser left raw macro source in the body. Acceptable for current consumers (tests only check absence of link markup and entities), but if a downstream needs macros in the rendered prose, add a printer that round-trips macros via the AST.
