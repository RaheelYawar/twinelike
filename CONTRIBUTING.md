# Contributing to Twinelike

Thanks for your interest. This is a small project — issues, PRs, and discussion are all welcome.

## Getting set up

```sh
git clone https://github.com/RaheelYawar/twinelike.git
cd twinelike
dotnet build Twinelike.sln
dotnet test  Twinelike.sln
```

The library targets `netstandard2.0` (Unity 2018.1+, Godot 3/4, .NET Framework 4.6.1+, .NET 5+, Mono, Xamarin). The test project targets `net48` and uses xUnit. No other prerequisites — `dotnet` 6+ is enough.

## Orientation

Before opening a non-trivial PR, skim:

- [`README.md`](./README.md) — public-facing overview, engine-integration story, feature matrix.
- [`ARCHITECTURE.md`](./ARCHITECTURE.md) — full architectural tour with diagrams. Split into integrator-facing (Part 1) and contributor-facing (Part 2) halves.
- [`CLAUDE.md`](./CLAUDE.md) — terse contributor notes: code conventions, file map, error policy. Originally written as context for AI assistants, but useful to humans too.
- [`TODO.md`](./TODO.md) — open bugs, deliberate divergences, and the candidate-next-slices roadmap. See also [`MACRO-DIVERGENCES.md`](./MACRO-DIVERGENCES.md) for per-macro behavioural gaps.

## Code conventions

The codebase is small and consistent. Match what's there:

- Namespace `Harlowe` (and sub-namespaces). The package name is `Twinelike` but the in-code surface is `Harlowe.*`.
- 2-space indentation, Allman braces.
- Private fields `_camelCase`. Public properties PascalCase. Expression-bodied members where they're concise.
- Public data models use **public fields**, not properties (see `HarlowePassage`, `Branch`, `StyleSpec`).
- **No LINQ.** The codebase uses explicit loops and `Dictionary` lookups throughout. This keeps allocation behaviour transparent and avoids surprises on AOT/IL2CPP targets.
- Return `null` or `string.Empty` for missing lookups rather than throwing.

## Error policy

The runtime never throws on the render hot path. Bad expressions, type errors, unknown macros all become `HarloweValue.Error` values that propagate through the evaluator and exit through `IRenderOutput.Error` — the rest of the passage keeps rendering. This mirrors Harlowe's own authoring model. When adding a macro, follow the existing pattern: validate arguments and return `HarloweValue.OfError("...")` rather than throwing.

## Testing

Every public surface is tested with xUnit. When adding a feature, add tests in the matching folder under `HarloweParser.Tests/`. Macros get tests in `Runtime/Macros/`; render-tree changes get tests in `Runtime/Rendering/`. New end-to-end behaviour goes in `HarloweEndToEndTests.cs`.

Some tests cover the engine-integration contract (`HtmlRenderOutput`, `IRenderOutput` event ordering); take care not to break the `PushStyle`/`PopStyle` and `BeginInteractive`/`EndInteractive` pairing invariants, as game-engine consumers rely on them.

## Reference impl

Harlowe is the spec we follow. When in doubt about author-facing behaviour, check the reference implementation — the [`Codaea/harlowe-branch-default-2`](https://github.com/Codaea/harlowe-branch-default-2) GitHub mirror of `branch/default` is fetchable via `gh api` (the canonical [Heptapod source](https://foss.heptapod.net/games/harlowe) is Anubis bot-walled; the old `modality/harlowe` mirror is Harlowe 1.x — don't use it). See CLAUDE.md for the fetch command and the unofficial-mirror caveat. The runtime sometimes simplifies versus the reference (e.g., string colour heuristics instead of a typed `Colour` value); call these out in the PR description so reviewers know it's intentional.

The internal docs (`CLAUDE.md`, `MACRO-DIVERGENCES.md`, `SAVE-LOAD-PLAN.md`) fetch reference Harlowe source from the `Codaea/harlowe-branch-default-2` GitHub mirror via `gh api`; nothing in the build or test pipeline depends on a local copy. If you prefer local grep access, download a `branch default` snapshot and drop it at `references/harlowe-branch-default.zip` — that file is **not** committed (Harlowe is a separate Zlib-licensed project, so `references/` is gitignored to keep a foreign codebase out of this MIT repo).

## Filing issues

Use the templates under `.github/ISSUE_TEMPLATE/` if you can. For bug reports, a minimal `.tw` or `.html` repro is worth a thousand words. The runtime's error-channel policy means many issues will already render an error in-prose — quoting that error usually pinpoints the layer to look at.
