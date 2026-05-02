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
- Two parsing layers. **Layer 1 (HTML host)** uses HtmlAgilityPack to extract `<tw-storydata>` / `<tw-passagedata>` elements and their attributes. **Layer 2 (Harlowe markup)** parses each passage's inner text — currently still served by the string-level shortcut in `Harlowe.cs` (`ParseBody`, `ParseBranches`), being replaced by the new tokenizer + AST pipeline under `HarloweParser/Tokens/` and `HarloweParser/Ast/`.
- The AST splits node types between **body** (prose, hooks, links, command macros — `IBodyNode`) and **expression** (macro arguments — `IExpressionNode`). Same `(name: ...)` syntax can become either `MacroNode` or `MacroCallNode` depending on position, which is why the split exists. Both trees use the visitor pattern.
- The tokenizer is **complete** (`HarloweTokenizer`) and covers every `TokenType`. It uses a mode stack of `Frame { Mode, ParenDepth }` — Body vs. Expression mode plus a per-frame paren-depth so a closing `)` can be disambiguated as `ParenClose` (grouping) vs `MacroClose` (pop the frame). A `_inLink` flag suppresses macro/hook markup inside `[[…]]` so link content scans as plain text plus `LinkArrowRight`/`LinkArrowLeft`/`LinkClose`.
- Branch links use Harlowe's `[[display text->passage name]]` syntax (HTML-encoded as `-&gt;`).
- All source lives in `HarloweParser/`; data models are simple public-field classes.

## Key Files
- `HarloweParser/Harlowe.cs` — Main parser: metadata extraction, passage parsing, branch parsing
- `HarloweParser/HarlowePassage.cs` — Passage model (Pid, Name, Body, Tags, Branches)
- `HarloweParser/Branch.cs` — Link model (Text, Name)
- `HarloweParser/Tokens/` — `ITokenizer`, `Token`, `TokenType`, and `HarloweTokenizer` (full implementation). Frame-stack mode dispatch lives here.
- `HarloweParser/Ast/Body/` — Passage-body AST nodes (`MacroNode`, `HookNode`, `LinkNode`, etc.) with `IBodyVisitor`. **Not yet populated** — no parser produces these.
- `HarloweParser/Ast/Expression/` — Expression AST nodes used inside macro arg lists with `IExpressionVisitor`. **Not yet populated.**
- `HarloweParser.Tests/HarloweEndToEndTests.cs` — end-to-end tests against `testFile.html`.
- `HarloweParser.Tests/HarloweTokenizerTests.cs` — direct tokenizer tests covering every `TokenType`, mode transitions (macro vs. grouping paren), and line/column tracking.
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
1. **Tokenizer extension** — cover the full Harlowe operator surface so the expression parser has every token it needs. All fusion happens at lex time so the parser sees one token per logical operator. Specifically:
   - Drop `%` from the symbol scanner — Harlowe has no modulo operator (it's the macro `(modulo:)`).
   - Add `...` (spread, precedence 6).
   - Add the missing single-word operators to `WordOperators`: `in`, `a`, `matches`, `where`, `when`, `via`, `making`, `each`, `its`, `bind`, `2bind`.
   - **Multi-word operator fusion in the lexer** via lookahead, emitting one `Operator` token per logical operator: `is not`, `is in`, `is not in`, `is a`, `is not a`, `does not contain`, `does not match`. Lookahead is bounded (max 3 words for `does not contain`) and skips intervening whitespace; if the suffix doesn't match, fall back to the shorter operator (e.g. `is $x` stays as `Operator("is")`).
   - **`'s` (precedence 3) as a lex-time operator** with the "no whitespace allowed before it" rule. Only fuse when `'` is immediately preceded by an identifier/variable/`)`/`]` with no intervening whitespace; otherwise `'` falls through to string-literal scanning. This means `'` is no longer unconditionally a string-literal opener — the scanner inspects the prior token first.
   - **`-type` (TypedVar suffix, precedence 14) as a lex-time operator** via lookahead: when `-` is immediately followed by the literal word `type`, emit a single `Operator("-type")` token. Otherwise `-` stays as binary subtraction or unary minus.
2. **Expression parser** — token stream → `IExpressionNode`. Precedence-climbing using the official Harlowe precedence table from the manual's "Operators and order-of-operations" appendix (lower order number = tighter binding). Notable quirks: `and` and `or` share precedence 13 (left-associative at the same level); `( )` grouping is precedence 1; unary `not`/`+`/`-` and `...`/`bind` are at 5–6.
3. **Body parser** — token stream → `PassageBody` (tree of `IBodyNode`s). Built standalone with its own unit tests (not wired into `Harlowe.cs` yet). On `MacroOpen`, delegates to the expression parser to fill `MacroNode.Arguments` directly — no transitional `RawArgs` shim. Hook attachment: a `HookOpen` immediately following a `MacroNode` becomes `MacroNode.AttachedHook` rather than a sibling.
4. **Wire AST into `Harlowe`** — invoke parsers per passage; derive `Branches` from `LinkNode`s so the existing public API and its tests keep passing. Then retire the legacy `ParseBranches` and the `&#39;`-only `ParseBody`.
5. **Variable store + evaluator** — only after 1–4. This is what unblocks engine integration (`(set:)`, `(if:)`, `(print:)` actually doing something).

## Known TODOs
- The legacy `Harlowe.ParseBody` / `Harlowe.ParseBranches` are still the active body/branch parsers. They will be replaced by the AST pipeline above; keep `Branches` populated for backward compatibility — derive it from `LinkNode`s once the body parser lands.
- Passage tag parsing is stubbed out (`Tags = null`).
- `ParseBody` only decodes `&#39;` → `'`; other HTML entities are not handled.
- Metadata fields (`_storyName`, `_creator`, `_creatorVersion`) are private with no public accessors.
- Tokenizer string literals do not handle escape sequences (`\"`, `\\`); Harlowe doesn't appear to define them, but if a corpus needs them this is where to add support.
- Tokenizer currently emits `Operator("%")` but Harlowe has no `%` operator — to be removed in step 1 of the Roadmap.
- `WordOperators` set is incomplete relative to the Harlowe precedence table — to be expanded in step 1 of the Roadmap.
