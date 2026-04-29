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
1. **Body parser** — token stream → `PassageBody` (tree of `IBodyNode`s). First pass can swallow macro arg tokens into a flat `RawArgs` list on `MacroNode` so the body AST ships without the expression parser.
2. **Expression parser** — token stream → `IExpressionNode`. Replaces the flat `RawArgs` with proper `MacroCallNode` / `BinaryOpNode` / etc. Called from the body parser at every `MacroOpen`.
3. **Wire AST into `Harlowe`** — invoke parsers per passage; derive `Branches` from `LinkNode`s so the existing public API and its tests keep passing. Then retire the legacy `ParseBranches` and the `&#39;`-only `ParseBody`.
4. **Variable store + evaluator** — only after 1–3. This is what unblocks engine integration (`(set:)`, `(if:)`, `(print:)` actually doing something).

## Known TODOs
- The legacy `Harlowe.ParseBody` / `Harlowe.ParseBranches` are still the active body/branch parsers. They will be replaced by the AST pipeline above; keep `Branches` populated for backward compatibility — derive it from `LinkNode`s once the body parser lands.
- Passage tag parsing is stubbed out (`Tags = null`).
- `ParseBody` only decodes `&#39;` → `'`; other HTML entities are not handled.
- Metadata fields (`_storyName`, `_creator`, `_creatorVersion`) are private with no public accessors.
- Tokenizer string literals do not handle escape sequences (`\"`, `\\`); Harlowe doesn't appear to define them, but if a corpus needs them this is where to add support.
