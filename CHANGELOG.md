# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Pre-`1.0.0` releases may make breaking changes between minor versions while the engine-integration surface stabilises.

## [Unreleased]

Save/load, an undo/redo timeline with reproducible randomness, the `(link:)` family, conditionals rebuilt as composable changers, string targets across the enchant/interaction macros, and inline text formatting. **Breaking:** hosts must rename the story class and rebuild — see *Changed*. `IRenderOutput` is unchanged, so existing adapters keep working.

### Added

- **Save / load.** `(save-game:)`, `(load-game:)`, `(saved-games:)` backed by a pluggable `ISaveStorage` host backend (in-memory default; Unity/Godot consumers plug their own) with IFID-namespaced slots. Values serialise as re-evaluable Harlowe source; loading is atomic and validating — a corrupt or version-newer blob errors without half-installing state.
- **Undo/redo timeline.** The session now runs on a past/present/future `Moment` timeline: `session.Redo()` joins `Undo()`, `visits`/`(history:)`/`turns` derive from per-turn visit trails (multiple `(goto:)`s in one turn appear as their trail), and a seedable RNG makes `(random:)`/`(either:)` reproduce across undo, redo, and save/load.
- **`(link:)` family.** `(link:)`/`(link-replace:)`, `(link-reveal:)`/`(link-append:)`, `(link-repeat:)`, `(link-rerun:)` changers plus the `(link-goto:)` and `(link-undo:)` commands. `(link-goto:)` emits the same flat link event as `[[...]]` syntax; `(link-undo:)` renders its alt text when there's no turn to undo.
- **Conditionals are changers.** `(if:)`/`(unless:)`/`(else-if:)`/`(else:)` now return changers, so they compose with styling (`(if: $x) + (text-style: "bold")[...]`), store in variables, and survive save/load — matching reference. An `(else:)` still pairs across an intervening `(set:)`/`(print:)`.
- **String targets + `via`-lambdas** for `(change:)`, `(enchant:)`, and the whole click/hover family — each occurrence of the string is targeted, and a `via` lambda can compute the changer per match (1-based `pos`).
- **Interaction commands and modes.** `(click-rerun:)` (re-armable), and the `-goto`/`-undo` command variants for `click`/`mouseover`/`mouseout` (e.g. `(click-goto: "target", "passage")`).
- **Inline text formatting.** `''bold''`, `//italic//`, `~~strike~~`, `^^sup^^`, `*em*` and `**strong**` markup, with bare URLs protected so their slashes never read as italics.
- **Verbatim text and horizontal rules.** Text between backticks prints exactly as written, and a line of three or more hyphens renders as `<hr>` through the `Html` channel.
- **Colour values.** Bare colour keywords (`red`, `navy`, `transparent`, …) and hex literals (`#a4e`, `#691212`) are now typed values, not identifiers or strings — as are the results of the new `(rgb:)`/`(rgba:)`/`(hsl:)`/`(hsla:)` macros. Colours mix with `+` (`red + white`), compare with `is`, expose `'s r`/`'s g`/`'s b`/`'s a`/`'s h`/`'s s`/`'s l` data names, survive save/load, and can be passed to `(text-colour:)` and `(background:)`. The LCH/OKLCH model — `(lch:)`, `(oklch:)`, `(mix:)`, `(complement:)`, the `lch` data name — plus `(gradient:)` are not implemented yet.
- **Maths macros.** `(round:)`, `(min:)`, `(max:)`, `(floor:)`, `(ceil:)`, `(trunc:)`, `(abs:)`, `(sign:)`.
- **String macros.** `(uppercase:)`, `(lowercase:)`, `(upperfirst:)`, `(lowerfirst:)`, `(substring:)`, `(words:)`, `(str-reversed:)`, `(str-repeated:)`, `(str-nth:)` (with `string-` aliases), all surrogate-pair-safe.
- **`(sorted:)` upgrades.** Sorts mixed numbers+strings (numbers first, matching reference's documented example), takes an optional leading `via` key-lambda with stable ordering for equal keys, and returns an empty array for zero values.
- **`RenderResult.ReplayTo(IRenderOutput)` and `Entry.ReplayTo(IRenderOutput)`.** Dispatch a stored render back through any adapter — the bridge from `RenderResult.Entries` to a streaming `IRenderOutput` such as `HtmlRenderOutput`.
- **Structured link labels, opt-in.** A link whose label carries styles, armed regions or spliced content can reach a host as a `BeginLink(target)`/`EndLink()` bracket by implementing the new optional `ILinkLabelOutput`; hosts walking `Entries` set `session.OutputFeatures = OutputFeatures.LinkLabels`. Other adapters need no change: the label flattens to one `Link`, dropping its styling, regions and HTML and moving any errors to just after the link.
- **Macro-name normalisation.** Names are case-, dash-, and underscore-insensitive (`(textstyle:)` ≡ `(text-style:)`), as in reference.
- **Compatibility profiles — a story keeps the semantics of the Harlowe major it declares.** `format-version` selects a profile at load; a host can override it with `new HarloweStory(html, HarloweProfile.V3)` or `new TweeReader(HarloweProfile.V3)` (the override belongs on the loader — some differences are lexical, so they're decided before any post-load property could be set). Absent, unparseable and pre-3.x versions run under Harlowe 3, so `--` in prose is never mistaken for a comment; a major newer than any implemented runs under the newest semantics. `story.GetCompatibilityNotices()` joins `GetParseErrors()`/`GetBrokenLinks()` as a third load-time report, describing anything unusual about the declared version. Saves are pinned to their own profile and never follow the story, so bumping `format-version` can't re-interpret existing save blobs.
- **Collapsing whitespace.** `{…}` and the unclosed `{=` reduce runs of whitespace in prose to single spaces and trim the span's edges. Text printed by a macro or variable keeps its whitespace, as in reference.
- **Unclosed hooks.** `[=` opens a hook that runs to the end of its container, alone, after a macro or variable, or with a name (`|name>[=`).
- **`...` spread.** Arrays and strings spread into any macro's arguments, including `(for: each _i, ...$list)`.
- **Printed strings render as markup.** `(print:)`, `$var` and string results from macros now render their markup, so a variable can hold bold text, a link or a macro call, as in reference. **For hosts:** a printed `<b>` now arrives through the `Html` channel instead of as escaped text, and a string that came from a player is interpreted too, so any macros in it run, including `(set:)` and `(goto:)`. `(verbatim-print:)`, the reference's way to print such text safely, isn't implemented yet.
- **Exponent number literals** (`1e3`, `2.5e-2`) parse and round-trip.
- **Host turn data.** `StorySession.TurnData` attaches an opaque string to the present turn, for the host's own per-turn record (what it displayed, what it derived). It travels with its turn through undo/redo, is discarded with an abandoned redo branch, and round-trips through save/load; `GetTimeline()` lists every turn's resting passage and data. Story code can't see it and the runtime never reads it. A save carrying turn data is stamped blob version 2; a session that never sets any still writes byte-identical version-1 blobs, so older builds keep loading them.
- **Custom macros.** `(macro:)` with typed and spread parameters, `(output:)`, `(output-data:)`, `(error:)`, `(partial:)`, and calls written `($name:)` or `(_name:)`. Macros and the commands they return survive undo and saves.
- **Code hooks.** `[markup]` inside a macro call is a value, as `(macro:)` needs. It prints as its markup and compares by source.
- **Typed variables.** `num-type $a` in `(set:)`, `(put:)`, `(move:)` and `(unpack:)` restricts what the variable can hold, including `const`. Restrictions on story variables follow undo and saves. A save holding one is stamped version 4, which older builds refuse.
- **`startup` passages** run ahead of the first turn's passage, which is where stories usually define their custom macros.
- **`(range:)` and `(passage:)`.** `(range:)` makes an inclusive run of whole numbers. `(passage:)` gives a datamap of a passage's `name`, `source` and `tags`.
- **Datasets.** `(ds:)`/`(dataset:)` hold unique values, with `+`, `-`, `contains`, `is in`, `matches`, `'s length`, `...` (which sorts them) and the `dataset` datatype. They survive undo and saves.
- **Hidden hooks and `(show:)`.** `|name)[…]`, `[…](name|`, `|name)[=` and `(hidden:)` keep a hook off the page until `(show:)` reveals it. It then runs with the temp variables from where it was written. A named hook hidden by a false condition can be shown too.
- **`(live:)` and `(stop:)`, driven by the host's clock.** **For hosts:** call `StorySession.Tick(elapsedMilliseconds)` once a frame. A `(live:)` hook renders once its delay has passed and again after every delay, replacing what it showed, until `(stop:)` runs in it. `Tick` returns the new page when a hook rendered, or null, and `HasLiveHooks` says whether there's anything to tick for.
- **`(after:)`.** Its hook renders once, on the first frame that `time` passes the delay. **For hosts:** call `Tick(elapsedMilliseconds, inputHeld)` instead, passing whether the player holds any key, mouse button or touch that frame, so an `(after:)` with a second number can be sped up as in reference.
- **Lists.** A line starting `* ` is a bulleted list item, and one starting `0. ` a numbered one. Repeating the marker (`**`, `0.0.`) nests the item deeper, and consecutive lines make one list. **For hosts:** a list arrives through the `Html` channel, as `<hr>` does, as `<ul>`, `<ol>`, `<li>` and their closing tags, each tag its own event and always balanced. `IBodyVisitor` gains `Visit(ListNode)`, which breaks any visitor written outside the library.
- **`(hide:)`.** Takes a hook, link or `?passage` off the page with its content as it is, and `(show:)` puts it back without running it again. Like reference, it can't have a changer attached.
- **Transitions.** `(transition:)`, `(transition-time:)`, `(transition-delay:)`, `(transition-skip:)`, `(transition-depart:)` and `(transition-arrive:)`, with their `t8n` short names. **For hosts:** they arrive as new `StyleSpec` fields, one innermost style layer per hook, added only when a transition is named or, around a passage link, a time, and `HtmlRenderOutput` writes them as `data-t8n` attributes. Playing them is up to the host. A `(transition:)` attached to a passage link is an error, as in reference, which wants `(transition-depart:)` or `(transition-arrive:)` there.
- **Time literals.** `2s` and `500ms` are numbers of milliseconds, and a number can start with its decimal point, as in `.5`.
- **`(cond:)` and `(passages:)`.** `(cond:)` picks the value after the first true condition. `(passages:)` gives every passage's `(passage:)` datamap in natural name order, optionally filtered by a `where` lambda.
- **`(dm-names:)`, `(dm-values:)` and `(dm-entries:)`**, with their `data-` and `datamap-` aliases. Each gives an array in the natural order of the datamap's names: the names, the values, or a `(dm: "name", …, "value", …)` per pair.
- **Number names in datamaps.** `(dm: 25, "Pikachu")'s (25)` works, as in real Harlowe, which also refuses a string beside the number it reads as, like `1` and `"1"`. A number name can be read but not set, and `(move:)` copies one without deleting it. Joining datamaps with `+` doesn't check names, so `1` and `"1"` can end up together, as they can in reference.
- **Host variable reads.** `StorySession.TryGetVariable(name, out value)` (with or without the `$`) and `GetVariableNames()` read story variables for the host. Reads are side-effect free and return deep copies, so they can't change story state, undo, or saves.

### Changed

- **Unset variables follow the story's Harlowe major.** In a Harlowe 3 story, a story variable that was never set reads as `0`, and so does `it` with nothing to refer to, as in real 3.3.9. Harlowe 4 stories keep the error, and temp variables error in both. A parenthesised URL like `(http://example.com)` stays prose.
- **`(move:)` matches real Harlowe.** Moving a whole variable now deletes the source, as Harlowe 3.3.9 and 4.0 do despite their manual, and moving something that can't be deleted, like `length`, just copies it. Undo and saves track the deletion. A save holding one is stamped version 3, which older builds refuse.
- **Temp variables follow real Harlowe more closely.** A `(set:)` writes to the nearest declaration rather than the outermost, and a click, hover or link hook keeps the temp variables it could see when it was armed, including a `(for:)` loop's.
- **Hooks attach the way real Harlowe attaches them.** Only a changer removes the whitespace before its hook, so `(print: 1) [x]` shows `1 x`. A macro that errors still renders its hook, so `(else:)[x]` with nothing to pair shows the error and `x`. Changers and booleans reach named hooks and past comments and `<br>`. A bare boolean prints nothing.
- **Changers attach to commands and links**, as in `(if: $key)[[Open->Door]]` and `(text-style: "bold")(print: $x)`. A `+` joins only changers, including ones in variables (`$a + (text-style: "bold")[x]`), and is ordinary text after anything else.
- **Built-in commands are values.** `(set: $c to (go-to: "x"))` stores the navigation and `$c` runs it, where it used to navigate at the `(set:)`. `[[links]]` work inside expressions. An assignment used as a value is an error.
- **Empty macro arguments are ignored**, so `(a:,1)` and `(a:1,,2)` work as in reference.
- **Unmatched hook markup stays in the prose.** A `[` with no `]`, a `]` with no `[`, and a `|name>` or `<name|` that isn't attached to a hook now print as written, as in reference, instead of vanishing or swallowing the text after them.
- **Paired markup crosses as it does in reference.** A closer pairs with the nearest open marker of its kind, even across an open hook or style, and whatever it crossed stays literal, so `''[x'']` is a bold `[x` followed by `]`. **For hosts:** `IBodyVisitor` gains `Visit` methods for `CollapsedNode`, `VerbatimNode` and `HorizontalRuleNode`.
- **Whitespace after a click or live tick follows reference.** What a click reveals, each render of a live hook and a `(show:)` collapse inside `{…}` on their own as they finish, and the rest of the page keeps its spacing, as in real Harlowe. A reveal can therefore leave a double or trailing space, which a browser shows as one.
- **An array given without `...` is one value.** The old auto-unpack convention is gone now that spread exists, matching reference. **Migration:** write `(for: each _x, ...$list)` and `(max: ...$list)` instead of passing the bare array.
- **Recursion limits cover printing and hooks.** `(display:)`, printed markup and each revision target's rendering share a per-render work budget, and `(display:)` and printing share one depth limit, so a passage that displays itself several times stops with an in-prose error instead of hanging. Page searches by `(replace:)`, `(enchant:)`, `(click:)` and `(link:)` have their own per-render limit for the same reason. `MaxDisplayDepth` now tops out at 128.
- **`(for:)` loops count toward the render's work budget**, since a nested loop over `(range:)` is short to write and enormous to run. One render can make a quarter of a million passes, then shows an in-prose error.
- **Hook names follow reference's rules.** They can hold digits and hyphens (`|1>[x]`, `?a-b`), and match ignoring case, `-` and `_`. A hook name is no longer data: storing one in a variable, or giving one to a macro that doesn't target hooks, such as `(a:)` or `(print:)`, is an in-prose error.
- **`passage` is no longer a keyword.** Real Harlowe has none, so a bare `passage` in an expression is an error. Use `(passage:)`.
- **`time` follows the host's clock.** It reads the time passed to `StorySession.Tick` since the passage was entered, so it's 0 while the passage first renders, as in reference, and doesn't move without `Tick`. It used to read a wall clock.
- **Revisions render into each target, as in reference.** `(replace:)`, `(append:)` and `(prepend:)` render their content into every target and then run it, rather than rendering once and copying. So a revision inside the content lands on top (`|a>[x](replace: ?a)[(replace: ?a)[y]z]` shows `y`), side effects, links and clicks happen once per target, a hook revised while it's still rendering keeps none of its later text (`(replace: ?passage)[x]y` shows `x`), and while the passage renders a revision only reaches what's before it. Clicks reveal the same way. `(for:)` now combines with a revision, running it once per pass. **For hosts:** `RenderNode.Clone` and `RenderNodes.CloneAll`, `CloneInto` and `Splice` are gone.
- **Saves write a datamap's names in reference's order**: natural order, with names that sort as equal, like `"1"` and `"1.0"`, reversed. A loaded datamap then holds its names in the order real Harlowe's would. They used to be in character order.
- **Datamap names are `DataName`s.** **For hosts:** `HarloweValue.AsDatamap` and `OfDatamap` use `Dictionary<DataName, HarloweValue>`, since a name can be a number. A string converts implicitly, so `map["name"]` still works. `contains` on a datamap now looks for any value, rather than erroring on one that isn't a string.
- **Changers are named by their main name**, in print and in error messages, so `(print: (colour: red))` shows `[A (text-colour:) changer]` and `(TEXT_STYLE:)` is `(text-style:)`, as in reference.
- **A stray `.` in a macro call is an error**, as in reference, where `(print: 1.)` used to print `1`.
- **`(num:)` reads `0x`, `0o` and `0b` integers**, as JavaScript does, so `(num: "0x11")` is 17.
- **A displayed passage is its own scope.** Temp variables it declares end with it, and its `(if:)`/`(else:)` no longer pair with the passage that displayed it, as in reference.
- **The story class is now `HarloweStory` (was `Harlowe`).** Sharing its namespace's name broke the documented quick-start: with `using Harlowe;`, `new Harlowe(...)` resolves to the namespace and fails to compile (CS0118). **Migration:** rename `Harlowe` to `HarloweStory` wherever it names the story type; the `Harlowe` namespace is unchanged.
- **`(background:)` / `(bg:)` now reads a plain string as an image URL, not a colour name.** Aligning with reference Harlowe: a value is a colour only when it's a colour *value* (`navy`, `#a4e`, `(rgb: …)`), a hex-shaped string, or a CSS function call (`"rgb(0,0,255)"`); anything else is an image path. **Migration: drop the quotes** — `(bg: "navy")` becomes `(bg: navy)`. (Stories written against real Harlowe already do this, since `(bg: "navy")` is an image path there too.) A gradient-shaped string now raises an explicit "not implemented" error instead of silently emitting CSS the browser drops.
- **Interactions re-resolve persistently.** Click/hover targets are re-matched against the full render tree after every render and dispatch (mirroring enchantments), so a `(click: ?b)` written before `|b>[...]` arms correctly and click-chains keep working across dispatches.
- **Plain `(click:)`/`(mouseover:)`/`(mouseout:)` reveal in place** — the attached hook appears at the macro's own position on trigger (reference behaviour), rather than replacing the target; composed styles land on the revealed content, not the armed region.
- **`?link` is a real target.** Styling and arming wrap around the link; `(replace:/append:/prepend: ?link)` render into its label; string targets match inside labels.
- **Composing incompatible changers errors** in-prose instead of silently dropping one side (a click or hover with a revision or `(for:)`), and a bare unattached changer in prose (e.g. `(if: $x)` with no hook) is an in-prose error, as in reference.
- **Consecutive text nodes coalesce** into single `Text` events — string-target matching works across what used to be node boundaries, and output granularity is coarser.
- **`--` is comment markup only for stories declaring Harlowe 4.** The `--` comment family shipped against Harlowe 4.0 semantics, which made the em-dash idiom (`it was -- and remains -- fine`) comment out the rest of the line for *every* story — and since 4.0 is unreleased, every real story is 3.x. Stories declaring a 3.x `format-version` now render such prose whole, and `5--3` is again `8`. Stories declaring 4.x, or declaring nothing, keep the comment behaviour.

### Fixed

- **A number literal too big for a double no longer stops a story loading on .NET Framework.** `1e999` threw out of the loader there. It now reads as Infinity, as it does in JavaScript and on .NET Core.
- **Printing a very deeply nested value no longer kills the host.** A loop wrapping a variable in `(a:)` tens of thousands of times overflowed the stack when it was printed. Past 256 levels it's now an in-prose error.
- **Twee round-trip data loss.** Passage names containing `[`/`]`/`{`/`}` now escape on write and unescape on read (per the Twee 3 spec); a leading UTF-8 BOM no longer hides the first passage; a content passage named `StoryTitle`/`StoryData` no longer collides with the metadata sigils; a story title starting with `::` round-trips.
- **`RenamePassage` rewrites inbound `[[...]]` links** across the story (all four link forms, `[[text|target]]` included), mirroring the Twine editor; macro string targets like `(goto: "old")` are left to the caller, as in Twine.
- **Undo, then a different in-passage choice, no longer corrupts history.** Clicking a `(link:)`/`(click:)` differently after undoing back to a passage, then moving on, left undo, redo and save/load reconstructing the *original* choice (a sword taken before the undo reappeared after a later undo or load). The re-played turn is now recorded from what was actually played, RNG position included.
- **An operator typo errors in place instead of blanking the passage.** `gt`, `eq`, `x`, `=>`, `or a` and malformed `#ff` colours made the whole passage a single error; now only the macro containing the typo errors and everything else renders, as in reference. A typo inside `--`-commented code is silent. Code driving `HarloweTokenizer` directly now gets a `TokenType.Error` token where it used to catch a `HarloweParseException`.
- **Interactive region ids are unique across the session.** They restarted at `r-0` on every render, so a late event for an earlier passage's region (a mouseout as old elements are torn down) fired whatever the new passage armed under the same id; such stale ids are now no-ops.
- **Editing a passage's `Body`/`RawBody` re-parses it.** Previously only Twee write-out saw the edit, while the runtime, `Branches`, `GetBrokenLinks` and `GetParseErrors` kept the old text. `RawBody` is now a property.
- **Numbers print as reference Harlowe prints them**, matching JavaScript: `0` for negative zero (was `-0` on .NET Core hosts), plain digits up to `1e21` and down to `0.000001`, and `1e+21`/`1e-7` exponent forms (was `1E+15`, `1E+17` or `1E-05`, depending on the host runtime).
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
