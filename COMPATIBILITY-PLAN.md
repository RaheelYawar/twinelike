# Compatibility Profiles — implementation plan

Design doc for the slice that turns `COMPATIBILITY.md`'s switch inventory into working machinery. Companion to `COMPATIBILITY.md` (the inventory itself) and `CLAUDE.md`'s Version policy section (the governing rules). Same role as `SAVE-LOAD-PLAN.md` played for the save/load slice: the procedure outlives the slice, because every future Harlowe major re-runs it.

> **Executed 2026-07-20.** All ten steps shipped; suite 2315 → 2390. Five corrections to the plan, found while auditing it against the code and worth carrying into the next major's run:
>
> 1. **`HarloweTokenizer` had no declared constructor at all**, so step 2's "the parameterless ctor delegates to `Latest`" meant *writing* both, not amending one.
> 2. **`TweeReader.Read` hoisted its tokenizer above the passage loop**, so step 5's pre-pass had to move that construction *after* it — otherwise the resolved profile arrives too late to matter, which is the exact failure the step exists to prevent.
> 3. **The HTML loader duplicates `ParseBodyToAst` inline** rather than calling it, so step 3 had two threading sites, not one.
> 4. **Step 5's stated precondition was unmet** — `TweeReaderTests` had no multiple-`StoryData` coverage. Added first, as step 0.
> 5. **Step 8 was safer than assumed**: `FeatureCoverageTests` asserts only "nothing threw", with no ground-truth diff, so the V3 leg needed no expected-output fork. The two runs differ in exactly one of ~60 passages — the comment passage — which is also the tightest evidence available that the switch has no unintended reach.
>
> One measurement changed the guidance rather than the plan: **a render-level probe is necessary but not sufficient.** Undoing each of row 1's three guards in turn failed 9, 6, and *2* facts respectively — the ScanText guard alone fragments the token stream while rendering byte-identically, so only the token-level assertions catch it. A switch whose guards can disagree about token shape without changing output needs an assertion at the layer it acts on.

## Context

The library promises per-major lock-in — *a story keeps the semantics of the Harlowe major it declares, indefinitely*. Today that promise is documentation only: `COMPATIBILITY.md` lists 11 known 3-vs-4 differences and nothing in the code acts on any of them.

That is not only a future-proofing gap. **It is a live rendering bug for every story that exists.** We adopted Harlowe 4.0's `--` comment markup, but 4.0 is unreleased, so 100% of real stories are 3.x, where `--` is ordinary prose. `RenderText("it was -- and remains -- fine")` currently returns `"it was "` — the em-dash idiom silently truncates the rest of the sentence. `CommentTests.DoubleDashProse_HidesBetweenDashes` pins this as deliberate; it is deliberate *against the wrong version*.

This slice builds the machinery and implements exactly one switch — `COMPATIBILITY.md` row 1, the comment markup — which fixes that bug for any story declaring a 3.x `format-version` (which real Twine exports always do). The remaining ten rows then land as small append-only additions, and the test machinery makes forgetting one a build failure rather than a lapse of discipline.

**Decisions taken up front:** default for absent/unrecognised `format-version` is newest known **plus a user-visible diagnostic**; scope is plumbing + the comment switch only (unset-variable and colour-tolerance deferred); `CommentTests` gets a parallel V3 section rather than parameterisation; the type is `HarloweProfile` with `.V3`/`.V4`.

## Design

**`HarloweProfile`** (new, `HarloweParser/HarloweProfile.cs`, alongside `Ordinals.cs`/`MacroNames.cs`): immutable public class, private ctor, get-only `bool` switch properties — for now just `CommentMarkup`. Static instances `V3`, `V4`, `Latest` (alias of the newest) and `SaveFormat` (see below). `static HarloweProfile Resolve(string formatVersion)` parses the leading integer major.

**Profile is computed, never cached** — this removes the desync class of bug entirely:

```csharp
private HarloweProfile _profileOverride;              // null = follow FormatVersion
public HarloweProfile Profile
{
  get { return _profileOverride ?? HarloweProfile.Resolve(FormatVersion); }
  set { _profileOverride = value; }
}
```

`FormatVersion` stays a plain auto-property with no side-effecting setter, and there is no cache to fall out of step. This matches the file's own idiom — `GetParseErrors()` and `GetBrokenLinks()` both walk current state per call rather than snapshotting at load. It also makes the pattern already in the suite (`HarloweEditingTests.cs:562-579`: `new Harlowe()` → set `FormatVersion = "3.3.9"` → `AddPassage`) parse that passage as V3, which is the only defensible reading.

**The override must enter at the loader boundary, not as a post-load property.** Row 1 is a *tokenizer* switch, so by the time a caller can touch `story.Profile` every body is already lexed. Add `Harlowe(string htmlText, HarloweProfile)` and `TweeReader(HarloweProfile)`, each setting `_profileOverride` before any parsing. Without these the override has no effect on this slice's only switch, and the two-profile feature-coverage run is impossible.

**Threading is narrow.** Only the tokenizer consumes the switch; the *consuming* side (`HarloweBodyParser.ParseComment`, `HarloweExpressionParser.SkipComments`) is inert when the token is never emitted, so the parsers are untouched. `MacroContext` gains a `Profile` field, default-initialised exactly like `Rng` (`MacroContext.cs:33`) — that precedent proves a defaulted field costs zero churn across its 47 construction sites — so the runtime path exists for switch #2 and the test probe helper can be written once.

**Diagnostics — two shapes, answering different questions.** `Harlowe.Profile` is a fact ("what am I running under"). `GetCompatibilityNotices()` is the third sibling to `GetBrokenLinks()`/`GetParseErrors()`: computed on demand, never throws, empty in the nominal case, with `CompatibilityNotice` mirroring `ParseError`'s shape (public fields + get-only `Message` + `ToString`). Throwing is not viable — 93 `new Harlowe(` sites exist, 58 of them via the parameterless ctor that sets `FormatVersion = string.Empty`.

Four cases, not two:

| `FormatVersion` | Profile | Notice |
|---|---|---|
| absent (`""`) | newest | **Info** — the common case (hand-built and test stories) |
| recognised `3.x` / `4.x` | matching | none |
| below 3 (e.g. `2.1.0`) | **V3** | **Warning** — clamps down, see below |
| future major (`5.2.0`) or unparseable | newest | **Warning** — distinguish "capability gap" from "malformed" in the message |

The below-3 clamp is a small deliberate extension to the agreed "unrecognised → newest" default. `CLAUDE.md` says the promise "starts at 3.x", and a 2.x story's prose is exactly as likely to contain `--` em-dashes as a 3.x story's — sending it to V4 truncates that prose, sending it to V3 does not. One extra branch in `Resolve`, strictly safer.

**`SaveSerializer` pins, and not to `Latest`.** `SaveSerializer.Deserialise` re-lexes `"(v:" + source + ")"` where the source came from our own `HarloweValue.ToSource()` — engine-emitted, not author-written, so author compatibility policy has no bearing on it. Following the story would make save blobs silently re-lex under different rules when an author bumps `format-version` in Twine: data loss for zero benefit. Pin to a dedicated `HarloweProfile.SaveFormat` constant (today `== V4`), documented as *never follows the story; changing it requires a `SaveBlobVersion.Current` bump*. `Latest` is wrong here precisely because it moves. This makes `StorySession`'s two `MacroContext` sites (`:541` save, `:728` render) differ **deliberately**, with a comment, rather than by accident.

## Implementation steps

Each step leaves the suite green.

1. **`HarloweProfile` + `Resolve`** (S). No consumers yet. Unit-test `Resolve` across absent / `3.x` / `4.x` / below-3 / future / garbage — the only tests in this slice that can go red on their own.
2. **Tokenizer ctor overload + all three guards, atomically** (S) — `HarloweTokenizer.cs` body-mode emit `:130-139`, expression-mode emit `:1126-1136`, and the `ScanText` prose-run break `:1225`. The parameterless ctor delegates to `Latest`, so all ~59 `new HarloweTokenizer()` test sites are untouched. **Guard all three or none:** flipping `ScanText` without the dispatch sites (or vice versa) yields position-dependent comment semantics, the worst possible failure to debug — put a cross-reference comment at each site. Add ~4 tokenizer-level V3 facts here. Free win: with the expression-mode emit suppressed, `5--3` falls through to two `-` operators → `5 - -3` = 8, exactly the required V3 behaviour.
3. **`Harlowe.Profile`** computed property (M) — de-`static` `ParseBodyToAst` (`:182`) and `HydratePassageFromBody` (`:161`) so re-parses follow the story's profile; thread into the HTML loader (`:653-654`). Keep `MakeParseErrorAst`/`DecorateParseErrors`/`EnsureWholeStubOriginalSource` static — `TweeReader` calls them as `Harlowe.Xxx`.
4. **Loader overrides** (S) — `Harlowe(html, profile)`, `TweeReader(profile)`. Unblocks steps 7-8; do not defer.
5. **`TweeReader` StoryData pre-pass** (M, riskiest) — `Read` is single-pass in source order, so a `:: StoryData` appearing after passages currently lexes earlier passages under the wrong profile. Materialise `SplitPassages` into a `List<PassageBlock>` **once** (it is a lazy `yield` iterator that re-walks lines and rebuilds strings on every enumeration, so a naive double-`foreach` doubles all Twee string work), apply **all** StoryData blocks in the pre-pass preserving current last-wins order and the per-block discard-on-throw, then `continue` past them in the main loop. Applying only the first while the main loop's last-wins sets `FormatVersion` from the last would leave metadata and profile permanently disagreeing. Leave `pendingStartName` resolution alone. **Check `TweeReaderTests` covers StoryData-after-passages and multiple-StoryData before restructuring; add those tests first if not.**
6. **`MacroContext.Profile`** (S) — default-initialised; `StorySession:728` ← `_story.Profile`, `:541` ← `HarloweProfile.SaveFormat` with the explanatory comment.
7. **Test machinery + parallel V3 `CommentTests` section** (L by line count, S by risk) — see below.
8. **Feature-coverage harness** (S) — fix `TestFiles/feature-coverage.twee:8` from `"3.3.9"` to `"4.0.0"`, then run the harness under both profiles.
9. **`GetCompatibilityNotices()` + `CompatibilityNotice`** (M) — independent of 1-8; merges into step 1 if the below-3 clamp is settled first.
10. **Docs** (S) — below.

Sequencing constraints: 2 before 3 (guards must exist before anything selects V3); 4 before 7 and 8; 5 is independent of 2-4 but should precede 8 so the Twee harness runs on final code; 9 can float.

## Tests

**`CompatibilityProfileTests.cs` (new) — the machinery that replaces discipline.** Reflection for *enumeration*, behavioural probe for *verification* — the same split as the existing drift guard at `BrokenLinkTests.cs:328-351` (enumerate at runtime, assert a behavioural consequence per entry, name the fix site in the failure message). Only the enumeration source differs, since `HarloweProfile` has no registry to walk. Reflection is new to this test project, and belongs in a non-shipped assembly rather than as permanent public API on the NuGet package.

- `SwitchNames()` reflects public **instance** `bool` properties (`BindingFlags.Instance` excludes the `V3`/`V4`/`Latest`/`SaveFormat` statics automatically).
- A `Dictionary<string, Func<HarloweProfile, string>> Probes` maps each switch to source rendered under a given profile; the `RenderUnder` helper sets both the tokenizer profile and `MacroContext.Profile`, so one signature covers parse-time and runtime switches.
- **Fact 1 — set equality in both directions** between `SwitchNames()` and `Probes.Keys`: catches a switch added with no probe *and* a stale probe left behind after a rename.
- **Fact 2 — `[Theory]` + `[MemberData]`, one case per switch:** `Assert.NotEqual(probe(V3), probe(V4))`. This is what catches a switch declared but wired to nothing — a probe that does nothing can no longer reach green. (`[MemberData]` precedent: `MulberryRngTests.cs:22-38`.)
- **Fact 3 — pin the concrete values**, so "differs" for the wrong reason is caught: V4 → `"it was "`, V3 → `"it was -- and remains -- fine"`.

**`CommentTests.cs` parallel V3 section** — the 38 existing facts stay untouched (parameterless helpers keep meaning "newest"). Add five profile-taking helper overloads (`Tokenize`/`Parse`/`RenderRaw`/`RenderText`/`RoundTrip`) plus ~30 inverted facts. Roughly 8 are profile-invariant and need no twin: HTML comments (`<!-- -->` exists in both majors), `Tokenize_SingleHyphen_StaysText`, and `[[a--b]]` (already exempt — `ScanBody` short-circuits to `ScanLinkContent` when `_inLink`, a separate scanner that never reaches the `-` arm).

**`FeatureCoverageTests`** → `[Theory]` over both profiles, ~60 passages each, doubling crash-policy coverage for a handful of lines. **The V3 leg must go through the step-4 `TweeReader(profile)` override** — setting `story.Profile` after `Read` would prove nothing, since the bodies are already lexed. Parameterise the fixed report filename (`FeatureCoverageTests.cs:77`) so the two runs don't clobber each other.

**Two fixtures silently become V3 and must be documented, not "fixed":** `TestFiles/testFile.html:12` declares `3.3.9` (verified safe — its only `--` occurrences sit inside embedded engine JS, past the passage bodies) and feeds 19 call sites via `TestFixture.LoadTestFile()`. Note in `TestFixture`'s docstring that it is now a V3 fixture — which is correct; it is a real 3.3.9 Twine export.

## Docs

- **`COMPATIBILITY.md`** — row 1's "Ours" column becomes **both (profile switch)**; add a status line that the machinery now exists. Correct row 4 while here: `it's` likely already works incidentally (`Identifier("it")` + `Operator("'s")`), and the `'s`-with-spaces half is **not a boolean** — the whitespace check at `HarloweTokenizer.cs:1001` doubles as the string-literal disambiguator, so `(print: $a 's name')` currently lexes `'s name'` as a StringLiteral. That row needs a designed disambiguation rule, not a flag; record it so nobody plans it as one.
- **`CLAUDE.md`** — Version policy: the slice is no longer "deferred until 4.0 ships"; state what shipped, the default-plus-diagnostic rule, and the loader-boundary override. Add `HarloweProfile.cs` to Key Files; update the test count.
- **`TODO.md`** — scope the compatibility-profiles entry down to the ten remaining rows.
- **`README.md`** — one line that a story keeps its declared major's semantics.

## Verification

`dotnet build Twinelike.sln` + `dotnet test Twinelike.sln` (both TFMs) after every step. End-to-end, beyond the suite:

1. Load a story declaring `format-version 3.3.9` containing `it was -- and remains -- fine`; assert it renders **whole** — the bug this slice exists to fix.
2. The same body under a story declaring `4.0.0`; assert it still truncates to `"it was "`.
3. `5--3` prints `8` under V3, `5` under V4.
4. A Twee file with `:: StoryData` placed **after** its passages resolves the profile correctly (the step-5 restructure).
5. A story with no `format-version` runs under newest and reports one Info notice from `GetCompatibilityNotices()`; a `5.2.0` story reports a Warning.
6. Save→load round-trip still works after the `SaveFormat` pinning.

**Note the risk profile:** this change class cannot produce a red test, only silent behaviour drift — the entire `--`-inside-a-string surface across the test project is 40 occurrences, all inside `CommentTests.cs`. That is exactly why the step-7 machinery is the real deliverable, not the switch.

## Risks & decisions taken

- **Residual by design:** a story with no `format-version` still gets newest, so metadata-less 3.x stories keep truncating at `--`. That follows the agreed default; real Twine exports always declare a version, so the exposure is hand-built stories.
- **Stale ASTs:** changing `FormatVersion` after passages are loaded does not re-parse them. True under every option considered; documented on the property. Deliberately *not* making the setter re-parse — a metadata assignment silently re-tokenising the whole story is more surprising than the stale rule.
- **`feature-coverage.twee:8` → `4.0.0` is a bug fix, not a behaviour change:** `build-feature-coverage-html.js:87` already hardcodes `format-version="4.0.0"` into the generated ground truth, so that harness has always been a V4 artifact and the twee's `3.3.9` was dead metadata this feature would otherwise promote into a silent semantic switch.
- **Reflection enters the test project** for the first time, deliberately, and only there.
- **Deferred, with reasons recorded:** the unset-variable switch (row 2 — needs `BodyRenderer`'s duplicate error sites at `:86-91`/`:133-138`, plus decisions about `(move:)`'s delete tail and property-write roots); colour tolerance (row 3 — we already implement the V3 rule correctly, so it only buys future V4 support, and 4.0's "all data values within 0.01" would make *number* equality tolerant, a much larger semantic question).
