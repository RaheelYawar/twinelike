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
- Two parsing layers. **Layer 1 (HTML host)** uses HtmlAgilityPack to extract `<tw-storydata>` / `<tw-passagedata>` elements and their attributes. **Layer 2 (Harlowe markup)** parses each passage's inner text — currently a string-level shortcut in `Harlowe.cs` (`ParseBody`, `ParseBranches`), being replaced by the new tokenizer + AST pipeline under `HarloweParser/Tokens/` and `HarloweParser/Ast/`.
- The AST splits node types between **body** (prose, hooks, links, command macros — `IBodyNode`) and **expression** (macro arguments — `IExpressionNode`). Same `(name: ...)` syntax can become either `MacroNode` or `MacroCallNode` depending on position, which is why the split exists. Both trees use the visitor pattern.
- The tokenizer is mode-stack-based (Body vs. Expression) so nested macros in argument lists tokenize correctly.
- Branch links use Harlowe's `[[display text->passage name]]` syntax (HTML-encoded as `-&gt;`).
- All source lives in `HarloweParser/`; data models are simple public-field classes.

## Key Files
- `HarloweParser/Harlowe.cs` — Main parser: metadata extraction, passage parsing, branch parsing
- `HarloweParser/HarlowePassage.cs` — Passage model (Pid, Name, Body, Tags, Branches)
- `HarloweParser/Branch.cs` — Link model (Text, Name)
- `HarloweParser/Tokens/` — Tokenizer interface and `HarloweTokenizer` skeleton (mode-stack design; body implementation deferred)
- `HarloweParser/Ast/Body/` — Passage-body AST nodes (`MacroNode`, `HookNode`, `LinkNode`, etc.) with `IBodyVisitor`
- `HarloweParser/Ast/Expression/` — Expression AST nodes used inside macro arg lists with `IExpressionVisitor`
- `HarloweParser.Tests/` — xUnit test project (end-to-end tests against `testFile.html`)
- `TestFiles/testFile.html` — Sample Harlowe story (8 passages; some links target absent passages — useful for testing tolerant parsing)

## Code Conventions
- **Namespace:** `Harlowe`
- **Indentation:** 2 spaces
- **Braces:** Allman style (opening brace on its own line)
- **Private fields:** `_camelCase` prefix
- **Public properties:** PascalCase; use expression-bodied members where concise (e.g., `=> _passages.Count`)
- **Data models:** Use public fields (not properties) for simple DTOs (`HarlowePassage`, `Branch`)
- **Null handling:** Return `null` or `string.Empty` for missing lookups, not exceptions
- **No LINQ usage** — the codebase uses explicit loops and Dictionary lookups

## Known TODOs
- **`HarloweTokenizer.Tokenize` body is deferred** — only the interface and mode-stack skeleton exist. Implementing it is the next step; the body parser, expression parser, and evaluator all sit on top of it.
- The legacy `Harlowe.ParseBody` / `Harlowe.ParseBranches` will be replaced by the tokenizer/AST pipeline once it's in place. Keep `Branches` populated for backward compatibility — derive it from `LinkNode`s in the new AST.
- Passage tag parsing is stubbed out (`Tags = null`).
- Body parsing only decodes `&#39;` → `'`; other HTML entities are not handled.
- Metadata fields (`_storyName`, `_creator`, `_creatorVersion`) are private with no public accessors.
