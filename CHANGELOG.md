# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Pre-`1.0.0` releases may make breaking changes between minor versions while the engine-integration surface stabilises.

## [Unreleased]

Save/load, an undo/redo timeline with reproducible randomness, the `(link:)` family, conditionals rebuilt as composable changers, string targets across the enchant/interaction macros, and inline text formatting. **Note:** `IRenderOutput` gained two members — see *Changed*.

### Added

- **Save / load.** `(save-game:)`, `(load-game:)`, `(saved-games:)` backed by a pluggable `ISaveStorage` host backend (in-memory default; Unity/Godot consumers plug their own) with IFID-namespaced slots. Values serialise as re-evaluable Harlowe source; loading is atomic and validating — a corrupt or version-newer blob errors without half-installing state.
- **Undo/redo timeline.** The session now runs on a past/present/future `Moment` timeline: `session.Redo()` joins `Undo()`, `visits`/`(history:)`/`turns` derive from per-turn visit trails (multiple `(goto:)`s in one turn appear as their trail), and a seedable RNG makes `(random:)`/`(either:)` reproduce across undo, redo, and save/load.
- **`(link:)` family.** `(link:)`/`(link-replace:)`, `(link-reveal:)`/`(link-append:)`, `(link-repeat:)`, `(link-rerun:)` changers plus the `(link-goto:)` and `(link-undo:)` commands. `(link-goto:)` emits the same flat link event as `[[...]]` syntax; `(link-undo:)` renders its alt text when there's no turn to undo.
- **Conditionals are changers.** `(if:)`/`(unless:)`/`(else-if:)`/`(else:)` now return changers, so they compose with styling (`(if: $x) + (text-style: "bold")[...]`), store in variables, and survive save/load — matching reference. An `(else:)` still pairs across an intervening `(set:)`/`(print:)`.
- **String targets + `via`-lambdas** for `(change:)`, `(enchant:)`, and the whole click/hover family — each occurrence of the string is targeted, and a `via` lambda can compute the changer per match (1-based `pos`).
- **Interaction commands and modes.** `(click-rerun:)` (re-armable), and the `-goto`/`-undo` command variants for `click`/`mouseover`/`mouseout` (e.g. `(click-goto: "target", "passage")`).
- **Inline text formatting.** `''bold''`, `//italic//`, `~~strike~~`, `^^sup^^` markup, with bare URLs protected so their slashes never read as italics. Markdown `*em*`/`**strong**` are still pending.
- **Colour values.** Bare colour keywords (`red`, `navy`, `transparent`, …) and hex literals (`#a4e`, `#691212`) are now typed values, not identifiers or strings — as are the results of the new `(rgb:)`/`(rgba:)`/`(hsl:)`/`(hsla:)` macros. Colours mix with `+` (`red + white`), compare with `is`, expose `'s r`/`'s g`/`'s b`/`'s a`/`'s h`/`'s s`/`'s l` data names, survive save/load, and can be passed to `(text-colour:)` and `(background:)`. The LCH/OKLCH model — `(lch:)`, `(oklch:)`, `(mix:)`, `(complement:)`, the `lch` data name — plus `(gradient:)` are not implemented yet.
- **Maths macros.** `(round:)`, `(min:)`, `(max:)`, `(floor:)`, `(ceil:)`, `(trunc:)`, `(abs:)`, `(sign:)`.
- **String macros.** `(uppercase:)`, `(lowercase:)`, `(upperfirst:)`, `(lowerfirst:)`, `(substring:)`, `(words:)`, `(str-reversed:)`, `(str-repeated:)`, `(str-nth:)` (with `string-` aliases), all surrogate-pair-safe.
- **`(sorted:)` upgrades.** Sorts mixed numbers+strings (numbers first, matching reference's documented example), takes an optional leading `via` key-lambda with stable ordering for equal keys, and returns an empty array for zero values.
- **`Entry.ReplayTo(IRenderOutput)`.** Dispatch a stored render entry back through any adapter — the bridge from `RenderResult.Entries` to a streaming `IRenderOutput` such as `HtmlRenderOutput`.
- **Macro-name normalisation.** Names are case-, dash-, and underscore-insensitive (`(textstyle:)` ≡ `(text-style:)`), as in reference.
- **Compatibility profiles — a story keeps the semantics of the Harlowe major it declares.** `format-version` selects a profile at load; a host can override it with `new Harlowe(html, HarloweProfile.V3)` or `new TweeReader(HarloweProfile.V3)` (the override belongs on the loader — some differences are lexical, so they're decided before any post-load property could be set). Absent or unrecognised versions run under the newest semantics; a pre-3.x version clamps to Harlowe 3. `story.GetCompatibilityNotices()` joins `GetParseErrors()`/`GetBrokenLinks()` as a third load-time report, describing anything unusual about the declared version. Saves are pinned to their own profile and never follow the story, so bumping `format-version` can't re-interpret existing save blobs.
- **Exponent number literals** (`1e3`, `2.5e-2`) parse and round-trip.

### Changed

- **`(background:)` / `(bg:)` now reads a plain string as an image URL, not a colour name.** Aligning with reference Harlowe: a value is a colour only when it's a colour *value* (`navy`, `#a4e`, `(rgb: …)`), a hex-shaped string, or a CSS function call (`"rgb(0,0,255)"`); anything else is an image path. **Migration: drop the quotes** — `(bg: "navy")` becomes `(bg: navy)`. (Stories written against real Harlowe already do this, since `(bg: "navy")` is an image path there too.) A gradient-shaped string now raises an explicit "not implemented" error instead of silently emitting CSS the browser drops.
- **`IRenderOutput` gained `BeginLink(string target)` / `EndLink()`.** A link whose label carries structure (styles, armed regions, spliced content) now arrives as this bracket pair with the label flowing through the ordinary channels; plain-label links keep the flat `Link` event. Existing implementations must add the two members.
- **Interactions re-resolve persistently.** Click/hover targets are re-matched against the full render tree after every render and dispatch (mirroring enchantments), so a `(click: ?b)` written before `|b>[...]` arms correctly and click-chains keep working across dispatches.
- **Plain `(click:)`/`(mouseover:)`/`(mouseout:)` reveal in place** — the attached hook appears at the macro's own position on trigger (reference behaviour), rather than replacing the target; composed styles land on the revealed content, not the armed region.
- **`?link` is a real target.** Styling and arming wrap around the link; `(replace:/append:/prepend: ?link)` splice into its label; string targets match inside labels.
- **Composing incompatible changers errors** in-prose instead of silently dropping one side, and a bare unattached changer in prose (e.g. `(if: $x)` with no hook) is an in-prose error, as in reference.
- **Consecutive text nodes coalesce** into single `Text` events — string-target matching works across what used to be node boundaries, and output granularity is coarser.
- **`--` is comment markup only for stories declaring Harlowe 4.** The `--` comment family shipped against Harlowe 4.0 semantics, which made the em-dash idiom (`it was -- and remains -- fine`) comment out the rest of the line for *every* story — and since 4.0 is unreleased, every real story is 3.x. Stories declaring a 3.x `format-version` now render such prose whole, and `5--3` is again `8`. Stories declaring 4.x, or declaring nothing, keep the comment behaviour.

### Fixed

- **Twee round-trip data loss.** Passage names containing `[`/`]`/`{`/`}` now escape on write and unescape on read (per the Twee 3 spec); a leading UTF-8 BOM no longer hides the first passage; a content passage named `StoryTitle`/`StoryData` no longer collides with the metadata sigils; a story title starting with `::` round-trips.
- **`RenamePassage` rewrites inbound `[[...]]` links** across the story (all three link forms), mirroring the Twine editor; macro string targets like `(goto: "old")` are left to the caller, as in Twine.
- **Parser error recovery no longer swallows valid content.** A parse error after a macro's argument list closed (e.g. `(set:)` misused in a changer chain) used to eat the next well-formed macro; recovery now consumes exactly the closing parens the broken construct owes, and nested malformed macros keep their full source span for round-tripping.
- **Malformed macro argument lists** surface an in-prose parse error instead of silently dropping tokens (and previously detaching the following hook).
- **`it` binds to the assignment target** in `(set: $x to it + 1)`.
- **Entity-encoded HTML attributes** (`&quot;` etc.) in Twine exports decode before parsing, so affected passages and links resolve.
- **Multi-story HTML archives** parse the first story's passages only, instead of mixing passages across stories.
- **String operations are code-point-aware** — `length`, indexing, and slicing treat astral characters (emoji, etc.) as single characters.
- **Unterminated `[[`** degrades to a hook opener so the rest of the passage still parses.
- **`is (not $x)` round-trips** — the printer preserves the parens that keep it from re-lexing as `is not`.
- **Error messages format numbers invariantly** (no `2,5` on comma-decimal locales), and error values passed as macro arguments propagate from a single central gate instead of being masked by per-macro type errors.
- **Data keys named after operators are readable.** `$dm's a` read the `a` word-operator instead of the key; a name in property position is now always a property name (matching how reference lexes it).

### Security

- **Recursion depth caps everywhere user input can nest** — expression parser, body parser (hooks), value deep-copy, and the `:: StoryData` JSON reader — converting potential uncatchable stack-overflow crashes on adversarial input into in-prose errors.
- **Save-blob hardening.** Version/seed gates reject integer-overflow bypasses; deserialisation is sandboxed so a tampered blob can't mutate live session state on a failed load.

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
