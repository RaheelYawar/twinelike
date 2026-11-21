# Render-output evolution plan

How the host-facing output contract grows without breaking hosts. Revised after two reviews.

## Problem

Every host implements `IRenderOutput`. In C#, adding a member to an interface breaks every class that implements it: a compile error (`CS0535`) for hosts that rebuild, a `TypeLoadException` for a host that swaps in a newer DLL without rebuilding. `netstandard2.0` rules out default interface methods, the language feature that would otherwise soften this.

It is already happening. 0.2.0 shipped eight members (`Text`, `Html`, `Link`, `Error`, `PushStyle`/`PopStyle`, `BeginInteractive`/`EndInteractive`). Unreleased work added `BeginLink`/`EndLink`, which breaks every 0.2.0 adapter, including the README's own "complete minimal adapter" (verified: `CS0535` on both members). The roadmap adds more host-facing structure (transitions, block markup such as headings, lists and rules, input controls), and the project's rule for it — "grow it additively" — is exactly what breaks hosts. `ISaveStorage`, the other host-implemented interface, has the same exposure.

This plan fixes the output contract only. 0.3.0 is still a breaking release: the `Harlowe` → `HarloweStory` rename breaks every host at source and binary level (verified against the real 0.2.0 DLL). After this work, a 0.2.0 host's migration is that rename and a rebuild, and its adapter stays as written.

## Options

1. **Abstract base class** (`RenderOutputBase` with a virtual no-op for every member; hosts inherit it instead of implementing the interface). The conventional .NET answer (`TextWriter`; the Framework Design Guidelines prefer classes for contracts that will evolve). Not recommended here:
   - It protects only hosts that inherit from it, and no host does today.
   - Unity views are `MonoBehaviour`s and Godot views are engine nodes; C# allows one base class, so the natural `class StoryView : MonoBehaviour, IRenderOutput` could not inherit it.
   - A virtual no-op ignores a new event, which for `BeginLink` renders the label as dead text. A template-method base could lower it instead, but the single-inheritance objection still stands.
2. **Status quo, freeze at 1.0.** Breaks hosts on every minor until then, and the roadmap continues past 1.0, so the problem returns.
3. **Fixed core plus optional capability interfaces, with fallback inside the library.** Recommended. A host opts into a feature by also implementing a small extra interface, which the library checks for with `as`.

## Design

**Rule: a host only ever receives event kinds it has opted into.**

- **Fixed core.** `IRenderOutput` is frozen at the eight channels 0.2.0 shipped and never gains a member again.
- **Each later channel is an optional feature:** a small capability interface carrying its methods, a flag in a new `[Flags] enum OutputFeatures`, and a *lowering* that expresses it through core channels for hosts that haven't opted in.
- **First feature: link labels.** `BeginLink`/`EndLink` move from `IRenderOutput` into `ILinkLabelOutput : IRenderOutput`; the flag is `OutputFeatures.LinkLabels`.
- **One opt-in per delivery path.** Anything delivered to an adapter is decided by the adapter's type. Only a host walking `RenderResult.Entries` has no adapter to inspect, so it alone sets `StorySession.OutputFeatures` (default `None`).

**One internal `OutputFeatureAdapter`** implements every capability interface and, per feature, forwards to the wrapped output when it has that interface and lowers otherwise. A separate wrapper per feature wouldn't work: a wrapper exposes only the interfaces it implements, so it would hide a host's support for any other feature.

Link-label lowering, from `BeginLink` to the matching `EndLink`:
- `Text` is buffered as received (nothing gets escaped twice), and inner links, bracketed or flat, join it with their targets dropped;
- `Html`, styles and interactive regions are suppressed;
- `Error`s are held in order.

`EndLink` then emits one `Link(text, target)` followed by the held errors, keeping the in-prose error policy. The text matches `RenderLinkNode.Text`. An empty label becomes `Link("", target)`, an unmatched `EndLink` is ignored, and `RenderResult.ReplayTo` closes a bracket left open at the end of a hand-built list so its label isn't lost.

A default session's `Entries` are flat, but each lowered `Link` entry privately keeps its label's events, so replaying it into an `ILinkLabelOutput` emits the full bracket without the flag. Keeping them on the entry rather than in a parallel list means a host's edits to `Entries`, such as filtering out errors, always apply to the replay. The label's errors stay separate entries after the link.

| Consumer | Default | `OutputFeatures.LinkLabels` set |
|---|---|---|
| Walks `Entries` | flat `Link`, errors after it | `BeginLink`…`EndLink`, errors in place |
| Replays into a core adapter | flat `Link` | `RenderResult.ReplayTo` lowers; per-entry `Entry.ReplayTo` throws `NotSupportedException` naming both fixes |
| Replays into an `ILinkLabelOutput` adapter | bracket, errors after it | bracket, errors in place |

`RenderTreeFlusher.Flush` and `HtmlRenderOutput` decide by the output's type alone. `BufferedRenderOutput` and `RenderTreeBuilder` implement the interface. Core-typed render paths (`MacroContext.Output`, `Changer.Apply`, `Interaction.RenderDeferredHook`, `InlineDisplayPassage`, direct `BodyRenderer` hosts) stay `IRenderOutput`, because the body renderer only emits flat links and brackets arise at flush.

Lowering drops styling, armed regions and HTML inside a structured label, so a region armed inside a label can't fire for a core-only host. Such labels are rare (a revision, enchantment or string-target arm reaching into a link), and navigation, the essential half, survives. `Kind` keeps the two values added since 0.2.0, which a 0.2.0 host never sees unless it opts in. `ISaveStorage` follows the same rule: frozen at its four members and pinned by a test.

## Steps

1. **Library:** `OutputFeatures`, `ILinkLabelOutput`, `OutputFeatureAdapter`; remove the two members from `IRenderOutput` and retarget every doc reference; the flusher, `StorySession.OutputFeatures`, `RenderResult.ReplayTo` and `Entry.ReplayTo`; `BufferedRenderOutput`, `RenderTreeBuilder` and `HtmlRenderOutput` implement the interface, and `HtmlRenderOutput` null-checks its inner output.
2. **Existing tests:** retype `IRenderOutput` sinks that call the link members; session tests expecting brackets or regions inside a label opt in; `ChangerTests.CapturedOutput` gains the interface.
3. **New tests:**
   - guards: the README adapter verbatim, plus reflection pins on `IRenderOutput`, `ILinkLabelOutput` and `ISaveStorage`;
   - lowering per label content kind: text, style, interactive, HTML, error, nested and flat inner links, empty, unmatched `EndLink`, unclosed `BeginLink`;
   - every flag × adapter combination for the session and `RenderResult.ReplayTo`, plus `Flush` by adapter type;
   - end to end: a structured label through a default session into a core-only adapter stays navigable, `RenderResult.Text` is identical with the flag on or off, dispatch follows the setting, and host edits to `Entries` survive replay;
   - `Entry.ReplayTo` mismatch; `HtmlRenderOutput` forward or lower, escaped once, tag stack balanced.
4. **Docs:**
   - README: interface listing as the core eight, a short optional-features paragraph, replay via `RenderResult.ReplayTo`;
   - ARCHITECTURE: channel count, diagram, table and replay loop in "The IRenderOutput contract", plus the "new render channel" contributor row;
   - CLAUDE.md: the "grow it additively" pivot is replaced;
   - CHANGELOG: replace the "gained two members" entry with an opt-in link-labels entry, without implying 0.3.0 is non-breaking.

Out of scope: trimming the public surface (the next item), and designing the future features themselves. Each lands later as its own feature under this rule.

## Review

Two Muse Code reviews, both **approve with changes**, agreeing with rejecting the base class and with the rule. Adopted: precise lowering semantics with a test per label content kind, a whole-list replay that lowers, the full list of affected code, tests and stale docs, interface guards, an end-to-end compatibility test, and the opt-in rule per delivery path.

Settled differently:
- Errors inside a lowered label move after the link instead of being dropped.
- The separate fallback paths collapse into one adapter, which also resolves the second review's stacking concern.
- Style and interactive depth counters aren't needed, because suppression keys only on link depth and label content is balanced.
- `LinkLabels` and `ILinkLabelOutput` keep their names, following the plural-flags and singular-interface conventions.

Neither review caught that the first draft's "0.3.0 avoids the break" was false at release level. Checking the real 0.2.0 DLL did, and the Problem section now scopes the promise.

A Muse Code review of the implementation found no bugs, and its test and documentation gaps are fixed. Not adopted: a public lowering helper (an adapter without the interface already gets lowering for free, and the next item shrinks the public API) and a test that re-reads the README (the copy was compared once instead).
