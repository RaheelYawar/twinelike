# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Pre-`1.0.0` releases may make breaking changes between minor versions while the engine-integration surface stabilises.

## [Unreleased]

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

[Unreleased]: https://github.com/RaheelYawar/twinelike/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/RaheelYawar/twinelike/releases/tag/v0.1.0
