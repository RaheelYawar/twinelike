# Harlowe Version Compatibility

**Status: machinery shipped 2026-07-20; one switch implemented (row 1), ten still inventory.** `HarloweProfile` selects a profile from the story's declared `format-version` (host-overridable at the loader), and `CompatibilityProfileTests` reflects over the switch properties to demand a behavioural probe per switch — so a switch declared but wired to nothing fails the build. This file stays the append-only record of every point where Harlowe majors deliberately differ. The governing policy — per-major lock-in, append-only profiles, what does and doesn't earn a switch — is the "Version policy" section of `CLAUDE.md`; the implementation procedure, which outlives this slice because every future major re-runs it, is `COMPATIBILITY-PLAN.md`.

**Add a row the moment a version difference is found**, even if nothing is implemented on either side. A missing switch is invisible; an empty row is not.

## Rules

- **Append-only.** Existing rows are never rewritten to suit a new major — a new major adds a *column*, and every existing row gets an explicit value in it.
- **Per major, not per minor.** Twine binds a story to the newest version within its major, so V3 means "the last audited 3.x", currently **3.3.9**. A 3.2-authored story has been running under 3.3.9 rules in Twine for years.
- **Our own bugs are fixed under every profile.** Only differences reference *intended* between majors earn a switch. Reference's own bugfixes are not switches either (see Non-switches below) — the exception is a bug reference deliberately left unfixed in the older major for compatibility, which is a real behavioural difference.
- **New-in-N macros stay registered under older profiles.** Under real 3.x an unknown macro was an in-prose error, so no shipped 3.x story can depend on its absence, and lock-in promises unchanged *existing* behaviour, not withheld capability. Deliberate, and slightly more liberal than reference.
- **Baseline:** V3 = Harlowe 3.3.9 (last 3.x release). V4 = the pinned 4.0-unstable node in `CLAUDE.md`, re-pinned to 4.0 final on release.

## Switches

"Ours" is the side currently implemented. `—` means the feature is unimplemented here, so the switch costs nothing yet.

| # | Switch | V3 (≤3.3.9) | V4 | Ours | Reference |
|---|---|---|---|---|---|
| 1 | `--` comment markup | none: prose `--` is literal, `5--3` is `5 - (-3)` | comments out the next element | **both** (`HarloweProfile.CommentMarkup`) | `comment` rule, `ts/markup/patterns.ts` |
| 2 | Unset story variable read | defaults to `0` | error | **V4** | 4.0 Alterations → Coding |
| 3 | Colour `is` tolerance | RGB within 1e-3, alpha exact | all data values within 0.01 | **V3** | `colour.ts` `is()`; 4.0 Alterations → Macros |
| 4 | `'s` with surrounding spaces (`$a 's 1st`); `it's` as `its` synonym | rejected | accepted | see note | 4.0 Alterations → Coding |
| 5 | `any` array/dataset data name | works (renamed `some` in 3.3.0, alias kept) | removed, conflicts with the `any` datatype | — | 4.0 Compatibility |
| 6 | `(mix:)` colour model | LCH | OKLCH by default; optional leading model string | — | 4.0 Alterations → Macros |
| 7 | `(complement:)` colour model | LCH | OKLCH | — | 4.0 Alterations → Macros |
| 8 | `(lch:)` maximum C | 132 | 150 | — | 4.0 Alterations → Macros |
| 9 | Colour `oklch` data name | absent | present, alongside `lch` | — | 4.0 Alterations → Coding |
| 10 | Measurement datatype in `(text-size:)`, `(border-size:)`, `(corner-radius:)`, `(text-indent:)`, `(box:)`, `(scroll:)`, … | number-based "scale" arguments | CSS-style measurements | **V3** for `(text-size:)`; rest unimplemented | 4.0 Additions → Coding; `MACRO-DIVERGENCES.md` #7 |
| 11 | `[=` unclosed hook "punch-through" in headers/footers | broken, left unfixed for compatibility | fixed | — (column markup unimplemented) | 4.0 Bugfixes → Macros |

Row 4 is two halves and **only one is a switch**, measured 2026-07-20. `it's` already lexes as `its` incidentally, in both profiles — `(print: it's 1st)` gives `Identifier(it)` + `Operator('s)`, because `TryScanPossessive` accepts a preceding `Identifier`. The spaced-`'s` half is not a boolean: the same whitespace check that rejects `$a 's 1st` doubles as the string-literal disambiguator, so today that input lexes `'s 1st)` as a **StringLiteral** which swallows the macro's closing paren (`(print: $a 's name')` likewise yields `StringLiteral(s name)`). Accepting spaced `'s` therefore needs a designed disambiguation rule — how to tell a possessive from a quote — not a flag. Recorded so nobody plans it as one.

Row 10 detail, for whenever it is implemented: units are `px`, `em`, `rem`, `Lh` (direct CSS equivalents) plus `w` and `h` (CSS percentages that also declare which dimension they apply to). Measurements support arithmetic — added and subtracted with each other (`2em - 10px`), multiplied and divided by numbers (`50px * 2`) — so the value type needs operator support, not just parsing.

## Non-switches

Reference bugfixes between majors, fixed under **all** profiles per the rules above. Listed so nobody re-litigates them:

- `(print: < 4)` inferring a `0` on the left of a comparison (4.0 errors).
- `2 + ` not erroring with a missing right operand (4.0 errors).
- Dataset `is` being order-dependent, so `(ds: 2, 3)` ≠ `(ds: 3) + (ds: 2)` (4.0 compares order-independently).

## Watch items

Not switches, but 4.0 changes that touch subsystems large enough to need a plan before the profile slice:

- **Save files.** 4.0 rewrites save storage onto IndexedDB *specifically because it breaks backwards compatibility* (3.3.8 release note), and is likely to add save-file macros such as export-to-file. We are insulated from the storage half — `ISaveStorage` is host-backed and serialization is source-based, not localStorage-bound — but save *semantics* and the macro surface may move, and save/load is one of the larger subsystems here.
- **The `user` keyword** (4.0 addition): a datamap of browser/environment properties (`width`, `height`, `orientation`, `motion`, `contrast`, `hover`). Browser-shaped, so a game-engine host would need to supply the values; worth a deliberate decision rather than a default port.
