# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Pre-`1.0.0` releases may make breaking changes between minor versions while the engine-integration surface stabilises.

## [Unreleased]

## [0.2.0] — 2026-06-08

A large correctness pass against reference Harlowe (the "divergence audit"), parse-error recovery so a single malformed passage no longer aborts a load, several new operators and macro aliases, and packaging changes. **Note:** the distributed DLL is now lower-case `twinelike.dll` — see *Changed*.

### Added

- **`(else-if:)` / `(elseif:)`** conditional macro.
- **Macro aliases.** `(loop:)` for `(for:)`, `(number:)` for `(num:)`, and `(str:)` / `(string:)` for `(text:)`. The `(text:)` family is now **variadic** — it joins all of its arguments.
- **Operators.** `%` modulo; polymorphic `+` (arrays, datamaps, booleans) and `-` (strings, arrays); `=` accepted as shorthand for `to` in `(set:)` / `(put:)` assignments.
- **Lambda `pos`** — a 1-indexed position identifier bound on each iteration.
- **`turn` / `turns`** identifiers in the evaluation context (count of passage transitions).
- **Parse-error recovery.** A malformed passage now renders an in-prose error and the rest of the story stays usable, instead of failing the whole load. Recovery is per-node, so well-formed siblings still parse, and the original source is retained so `TweeWriter` round-trips the broken passage verbatim.
- **Corrective hints for mistyped operators** — common misspellings and reversed forms surface a targeted parse error suggesting the right form.

### Changed

- **The distributed library DLL is now `twinelike.dll` (lower-case), previously `Twinelike.dll`.** Update anything that loads the assembly by filename (e.g. Unity `Assets/Plugins`). The NuGet package id (`Twinelike`) and the in-code namespace (`Harlowe.*`) are unchanged.
- **Hook-scoped temp variables.** `_temp` variables now live in a per-hook scope stack, and `set` walks outer-to-inner (an inner hook updates the outermost existing declaration), matching reference Harlowe; each `(for:)` iteration and lambda gets a fresh scope.
- **`of` is now right-associative**, matching reference precedence for chained `of` / `'s`.
- **Reference-aligned macro behaviour.** `(num:)` / `(number:)` use JS-style string coercion and reject a `number` argument; `(random:)` truncates fractional bounds instead of erroring; `(for:)` / `(loop:)` accept zero items; `(align:)` accepts arbitrary-length and off-centre arrows; `(background:)` trims its input and aligns its image-vs-colour heuristic with reference.
- **Stricter, spec-aligned validation** — these now raise an in-prose error where they were previously lenient: `(goto:)` to a non-existent passage, `(dm:)` with duplicate keys, and a stray `(else:)` with no preceding conditional.
- **Twee read/write uses an LF-only line-break model**, consistent with the Twee ecosystem.
- **Link/anchor output standardised to a single shape** — focusable, accessible anchors, with empty attributes for null regions.
- **No symbol package (`.snupkg`) is published.** ILRepack's assembly merge invalidates the PDB↔DLL signature match, so a symbol package fails nuget.org validation. Local Release builds still emit a PDB for your own debugging.

### Fixed

- **Loader crash on non-ASCII Unicode digits** — the tokenizer's number scan is now gated to ASCII digits.
- **Navigation-bug guard** — `PendingGoto` can no longer be mutated during the enchantment pass.

### Security

- **Hardened the CSS value validator.** It now blocks structural characters and disallowed keywords across Unicode (NFKC) equivalence forms, rejects unpaired surrogates that could bypass the NFKC defence, and trims `(background:)` input so stray whitespace can't smuggle malformed CSS.
- **Absolute depth ceiling on `(display:)`** recursion, complementing the existing cyclic-embedding guard.

## [0.1.1] — 2026-05-22

### Added

- **String-literal escape sequences in the tokenizer.** Decodes the JS-spec escape set (`\n`, `\r`, `\t`, `\b`, `\f`, `\v`, `\0`, `\\`, `\'`, `\"`, `\xHH`, `\uHHHH`) at lex time so the runtime sees the cooked value. Matches reference Harlowe via its JS evaluator; unknown escapes drop the backslash.
- **Round-trippable string literals through `MarkupPrinter`.** Always emits double-quoted strings with backslash escapes; the prior "throw on strings containing both `"` and `'`" limitation is gone.

### Fixed

- **Render-tree node cloning now deep-copies mutable fields** (and the builder runs an imbalance check) so revision and enchantment splices no longer share state between targets.
- **Datamap rendering uses alphabetical key order** for stable, comparable output.
- **`AddPassage` hydrates the AST** for passages constructed from a body string.

### Security

- **HTML-escape `<`, `>`, `&` in text output** to prevent unintended HTML rendering or injection through author prose.
- **`StyleValueValidator` for CSS injection protection.** Style-value macros (`(text-color:)`, `(background:)`, `(font:)`, etc.) validate their inputs to prevent raw CSS payloads reaching the HTML adapter's inline `style="..."` attribute. Composed style layers are now tracked per region for clean teardown after interactions.
- **Recursion limit on `(display:)`** to safely terminate cyclic embedding.
- **`to` / `into` assignment restricted to the top-level positions of `(set:)` / `(put:)` argument lists.** Sub-expressions now surface `HarloweParseException` at parse time, eliminating the prior evaluation-time state-leak hazard.
- **Twee tag values escape special characters** when emitted by `TweeWriter`, preventing malformed headers when round-tripping author-supplied tags.

## [0.1.0] — 2026-05-16

First public release. Tracks Harlowe 3.3.8 to the extent listed in the [README feature matrix](./README.md#supported-harlowe-features).

### Added

- **Full styling-changer macro set.** Variadic `(text-style:)` over the entire Harlowe name set including `mark`, `outline`, `shadow`, `emboss`, `blur`, `blurrier`, `smear`, `mirror`, `upside-down`, and the animation effects (`blink`, `fade-in-out`, `shudder`, `rumble`, `sway`, `buoy`, `fidget`). The `"none"` sentinel clears prior composed style layers per Harlowe spec.
- **Discrete styling macros.** `(text-color:)` / `(text-colour:)` / `(color:)` / `(colour:)`, `(background:)` / `(bg:)` (colour-or-image dispatch by heuristic), `(font:)`, `(text-size:)` / `(size:)`, `(opacity:)` (0..1), `(align:)` (Harlowe arrow syntax).
- **Interaction macros.** `(click:)`, `(mouseover:)`, `(mouseout:)` and their `-replace` / `-append` / `-prepend` variants. Hook-name targets only; string targets pending. New `BeginInteractive` / `EndInteractive` channel on `IRenderOutput` for engine integrations.
- **Enchantment macros.** `(change:)` (one-shot) and `(enchant:)` (persistent). Idempotent re-application across dispatches.
- **Revision macros.** `(replace:)`, `(append:)`, `(prepend:)` — hook-name and literal-string targets.
- **Hook references.** `?name`, `?passage`, `?page`, `?link`, with ordinal narrowing (`?cake's 1st`).
- **Lambda-consuming macros.** `(find:)`, `(all-pass:)`, `(some-pass:)`, `(none-pass:)`, `(altered:)`, `(for:)`, `(folded:)`, `(rotated-to:)`, `(sorted:)` — `where`, `via`, `making`, `each` clauses.
- **Render tree layer.** Addressable, mutable representation of rendered content sitting between `BodyRenderer` and `IRenderOutput`, enabling revision and enchantment macros to target already-rendered nodes.
- **Twee 3 round-trip.** `TweeReader` and `TweeWriter`, with lazy reserialization (`HarlowePassage.IsDirty`) so clean passages round-trip byte-for-byte.
- **Programmatic story editing.** `AddPassage`, `RemovePassage`, `RenamePassage`, plus public setters on story-level metadata.
- **`StorySession` engine surface.** `Render`, `Goto`, `Undo`, `DispatchEvent`. Top-level entry point for engine integrations.

### Known limitations

- `(text-style:)` off-centre alignment variants (`==><==`, etc.) error rather than rendering.
- String-target `(click:)` / `(enchant:)` not yet implemented (hook-name targets work).
- `(link:)`, `(live:)`, `(event:)`, `(trigger:)`, `(t8n:)` / transitions, custom `(macro:)` / `(output:)`, storylets, and `(unpack:)` / `...` spread are not yet implemented; they emit an "unknown macro" in-prose error.
- Tokenizer string literals do not handle escape sequences. A string containing both `"` and `'` cannot be round-tripped through `MarkupPrinter`.

[Unreleased]: https://github.com/RaheelYawar/twinelike/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/RaheelYawar/twinelike/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/RaheelYawar/twinelike/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/RaheelYawar/twinelike/releases/tag/v0.1.0
