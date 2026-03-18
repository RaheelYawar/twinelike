# Harlowe Parser — C# Library

## Project Overview
A C# library that parses Twine/Harlowe interactive fiction stories exported as HTML. It extracts story metadata, passages, and branching links into structured objects.

## Build & Run
- **Framework:** .NET Framework 4.0 (SDK-style csproj)
- **Build:** `dotnet build` or open `harlowe-parser.sln` in Visual Studio/Rider
- **Output:** Library DLL (`harlowe_parser.dll`)
- **No unit test project exists yet.** The only test artifact is `TestFiles/DeathTrip.html`.

## Architecture
- Entry point: `Harlowe` class constructor takes an HTML string and parses it
- Uses **HtmlAgilityPack** to parse the DOM and extract `<tw-storydata>` / `<tw-passagedata>` elements
- Branch links use Harlowe's `[[display text->passage name]]` syntax (HTML-encoded as `-&gt;`)
- All source lives in `HarloweParser/`; data models are simple public-field classes

## Key Files
- `HarloweParser/Harlowe.cs` — Main parser: metadata extraction, passage parsing, branch parsing
- `HarloweParser/HarlowePassage.cs` — Passage model (Pid, Name, Body, Tags, Branches)
- `HarloweParser/Branch.cs` — Link model (Text, Name)
- `TestFiles/DeathTrip.html` — Sample Harlowe story (19 passages)

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
