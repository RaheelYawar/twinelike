# Harlowe Parser — C# Library

## Project Overview
A C# library that parses Twine/Harlowe interactive fiction stories exported as HTML. It extracts story metadata, passages, and branching links into structured objects.

## Build & Run
- **Framework:** Library targets .NET Framework 4.0 (TFM `net40`); test project targets .NET Framework 4.8 (TFM `net48`). Both are SDK-style csprojs. Use the canonical short TFMs (`net40`, not `net4.0`) — Rider's surface-heuristics build path fails to resolve the dotted alias.
- **Build:** `dotnet build harlowe-parser.sln`
- **Test:** `dotnet test harlowe-parser.sln`
- **Output:** Library DLL (`harlowe_parser.dll`)
- The library csproj sits at the repo root and uses `<DefaultItemExcludes>` to keep it from globbing the test folder.

## Architecture
- Entry point: `Harlowe` class constructor takes an HTML string and parses it
- Uses **HtmlAgilityPack** to parse the DOM and extract `<tw-storydata>` / `<tw-passagedata>` elements
- Branch links use Harlowe's `[[display text->passage name]]` syntax (HTML-encoded as `-&gt;`)
- All source lives in `HarloweParser/`; data models are simple public-field classes

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
- Passage tag parsing is stubbed out (`Tags = null`)
- Body parsing only decodes `&#39;` → `'`; other HTML entities are not handled
- Metadata fields (`_storyName`, `_creator`, `_creatorVersion`) are private with no public accessors
