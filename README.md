# Twinelike

[![CI](https://github.com/RaheelYawar/twinelike/actions/workflows/ci.yml/badge.svg)](https://github.com/RaheelYawar/twinelike/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Twinelike.svg)](https://www.nuget.org/packages/Twinelike/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/RaheelYawar/twinelike/blob/main/LICENSE)

A C# library for parsing and running [Twine/Harlowe](https://twinery.org/) interactive fiction, designed to embed inside game engines — Unity, Godot, anything that runs .NET, Mono, or IL2CPP.

It accepts Twine 2 HTML exports and Twee 3 source, parses the full Harlowe markup language, evaluates author-written macros at runtime, and surfaces rendered content through an engine-agnostic `IRenderOutput` interface — plain text, navigation links, semantic styles, and interactive regions. Implement that interface against your engine's text renderer (TextMeshPro, RichTextLabel, raw HTML, plain console) and you have a working interactive story.

**Status:** Targets `netstandard2.0`, so it drops into Unity 2018.1+, Godot 3 & 4, .NET Framework 4.6.1+, .NET 5+, Mono, and Xamarin. Tracks Harlowe 3.3.9 semantics, audited against the 4.0 development branch; when 4.0 ships, a story keeps the semantics of the major version it declares (see `COMPATIBILITY.md`).

---

## Quick start

```csharp
using Harlowe;
using Harlowe.Runtime;
using Harlowe.Twee; // only needed if you're loading Twee 3 source

// Load a Twine 2 HTML export, or use new TweeReader().Read(tweeText) instead.
var story = new Harlowe(File.ReadAllText("story.html"));

// Wrap it in a session — this is the stateful object you drive.
var session = new StorySession(story);

// Render the current passage. Every navigation method returns one of these.
var result = session.Render();

Console.WriteLine(result.Text);
// Output:
//   You stand in a clearing. There is a path north.

// The player clicked a link in your UI; advance.
var next = session.Goto("Forest");

// Or, they clicked an interactive region — pass the id back:
var after = session.DispatchEvent(regionIdFromYourUi);

// Roll back to the previous passage:
if (session.Undo()) {
  session.Render();
}
```

`RenderResult` carries the rendered content as both a flat `.Text` string and a list of typed `.Entries` (Text, Link, PushStyle, BeginInteractive, …). Walk the entries in your engine's renderer, or call `entry.ReplayTo(output)` to stream them through an adapter implementing the `IRenderOutput` interface below.

## Engine integration

Implement `IRenderOutput` against whatever your engine uses to display text. The interface is small and stable:

```csharp
public interface IRenderOutput
{
  // Prose text — already entity-decoded, post-macro evaluation.
  void Text(string content);

  // Raw author-written inline HTML (e.g. <b>hello</b>). Drop, escape,
  // or pass through depending on whether your engine wants HTML at all.
  void Html(string rawHtml);

  // A passage navigation link whose label is plain text. Wire your UI's
  // click handler to session.Goto(target).
  void Link(string text, string target);

  // A navigation link whose label carries structure a flat string can't
  // express (styled or spliced content). The label arrives as ordinary
  // events between the bracket pair; render both shapes as the same
  // navigable link.
  void BeginLink(string target);
  void EndLink();

  // An in-prose error (bad expression, type mismatch, unknown macro).
  // The runtime never throws on the render hot path — errors come
  // through this channel instead.
  void Error(string message);

  // Bracketing semantic styles. StyleSpec carries flags (Bold, Italic,
  // Underline, Strikethrough, Superscript), value fields (Color, BackgroundColor,
  // BackgroundImage, FontFamily, FontSize, Opacity, Alignment), and a list
  // of named Effects (Mark, Outline, Shadow, Blur, Shudder, Blink, ...).
  // PushStyle is always paired with a matching PopStyle; nesting is
  // well-formed.
  void PushStyle(StyleSpec style);
  void PopStyle();

  // Bracketing interactive regions. region.Id is opaque — pass it back
  // to session.DispatchEvent() when the user interacts. region.Kind is
  // Click / MouseOver / MouseOut.
  void BeginInteractive(InteractiveRegion region);
  void EndInteractive();
}
```

### Mapping to common engines

The bracketing primitives (`PushStyle`/`PopStyle`, `BeginInteractive`/`EndInteractive`) intentionally line up with how inline-tag rich-text systems already work:

| Engine | `PushStyle` (Bold) | `BeginInteractive` | Dispatch wiring |
|---|---|---|---|
| Unity TextMeshPro | `<b>...</b>` | `<link="region.Id">...</link>` | `_onLinkClicked` → `session.DispatchEvent(linkId)` |
| Godot RichTextLabel | `[b]...[/b]` | `[url=region.Id]...[/url]` | `meta_clicked` → `session.DispatchEvent(meta)` |
| HTML / web | `<b>...</b>` | `<a data-region-id="region.Id">...</a>` | use `HtmlRenderOutput` (built in) |
| CLI / plain text | ANSI bold | numbered hotkey | manual prompt → dispatch |

For web/HTML consumers, replay a render's entries through the built-in `HtmlRenderOutput` and it translates semantic events into HTML tags automatically — no manual mapping.

### A complete minimal adapter

```csharp
public class ConsoleOutput : IRenderOutput
{
  public void Text(string content)             => Console.Write(content);
  public void Html(string rawHtml)             => Console.Write(rawHtml);
  public void Link(string text, string target) => Console.Write($"[{text} -> {target}]");
  public void Error(string message)            => Console.Write($"<<error: {message}>>");

  public void PushStyle(StyleSpec style) {
    if (style.Bold)   Console.Write("\x1b[1m");
    if (style.Italic) Console.Write("\x1b[3m");
  }
  public void PopStyle() => Console.Write("\x1b[0m");

  public void BeginInteractive(InteractiveRegion region) => Console.Write("[");
  public void EndInteractive()                           => Console.Write("]");
}
```

## Supported Harlowe features

✓ shipped &middot; ⚠ partial &middot; ✗ not yet

### Language

| Feature | Status |
|---|---|
| Variables (`$story`, `_temp`) and full expression grammar | ✓ |
| Colour values: `red`, `#a4e`, `(rgb:)`/`(hsl:)`, `+` mixing, `'s r`/`'s h` data names | ✓ |
| Every operator from the Harlowe 3.3.9 precedence table | ✓ |
| Property access (`'s`, `of`, `its`) | ✓ |
| Ordinal indexing (`1st`, `last`, `Nthlast`) | ✓ |
| Hooks: anonymous `[…]`, `\|name>[…]`, `[…]<name\|` | ✓ |
| Twine links: `[[text->target]]`, `[[target<-text]]`, bare `[[name]]` | ✓ |
| Inline text styling: `''bold''`, `//italic//`, `~~strike~~`, `^^sup^^` (Markdown `*em*`/`**strong**` pending) | ⚠ |
| Lambdas: `where`, `via`, `making`, `each` (incl. implicit `it`) | ✓ |
| Hook references: `?name`, `?passage`, `?page`, `?link` (+ ordinal narrowing) | ✓ |
| `(goto:)` with multi-step undo & redo | ✓ |
| Inline `<html>` passthrough in passage bodies | ✓ |
| String escape sequences (`\n`/`\r`/`\t`/`\\`/`\"`/`\xHH`/`\uHHHH`, etc.) | ✓ |
| `when` lambda clause | ✗ (reserved for `(event:)`) |

### Macros

| Macro family | Status |
|---|---|
| `(set:)`, `(put:)`, `(move:)`, `(print:)`, `(display:)` — incl. property assignment (`(set: $arr's 1st to 5)`, `'s`/`of` chains, computed accessors, the `random` data name) | ✓ |
| `(unpack:)` — array/datamap destructuring, nested patterns, `(move:)` pattern destinations (`(p:)`/datatype/rest patterns pending) | ⚠ |
| `(if:)`, `(unless:)`, `(else-if:)`, `(else:)` | ✓ |
| `(random:)`, `(either:)`, `(history:)` | ✓ |
| `(save-game:)`, `(load-game:)`, `(saved-games:)` | ✓ |
| `(a:)`, `(dm:)`, `(modulo:)`, `(text:)`, `(num:)` | ✓ |
| `(rgb:)`, `(rgba:)`, `(hsl:)`, `(hsla:)` (`lch`/`oklch`/`mix`/`complement` pending) | ⚠ |
| `(round:)`, `(min:)`, `(max:)`, `(floor:)`, `(ceil:)`, `(trunc:)`, `(abs:)`, `(sign:)` (`sqrt`/`pow`/`log`/trig pending) | ⚠ |
| `(uppercase:)`, `(lowercase:)`, `(upperfirst:)`, `(lowerfirst:)`, `(substring:)`, `(words:)`, `(str-reversed:)`, `(str-repeated:)`, `(str-nth:)` | ✓ |
| `(find:)`, `(all-pass:)`, `(some-pass:)`, `(none-pass:)`, `(altered:)` | ✓ |
| `(for:)`, `(folded:)`, `(rotated-to:)`, `(sorted:)` | ✓ |
| `(text-style:)` — full name set incl. mark, outline, shadow, blur, mirror, shudder, blink, fade-in-out, … (variadic, with `"none"` reset) | ✓ |
| `(text-color:)` / `(text-colour:)` / `(color:)` / `(colour:)` | ✓ |
| `(background:)` / `(bg:)` — colour value, hex string, or image url | ✓ |
| `(font:)`, `(text-size:)` / `(size:)`, `(opacity:)`, `(align:)` | ✓ |
| `(border:)`, `(border-colour:)`, `(border-size:)`, `(corner-radius:)`, `(rotate:)` | ✗ |
| `(hover-style:)`, `(line-style:)`, `(char-style:)`, `(link-style:)` | ✗ |
| `(replace:)`, `(append:)`, `(prepend:)` | ✓ |
| `(change:)`, `(enchant:)` — hook-name or string targets, `via`-lambdas | ✓ |
| `(click:)` / `-replace` / `-append` / `-prepend` / `(click-rerun:)` / `(click-goto:)` / `(click-undo:)` | ✓ |
| `(mouseover:)` and `-replace`/`-append`/`-prepend`/`-goto`/`-undo` variants | ✓ |
| `(mouseout:)` and `-replace`/`-append`/`-prepend`/`-goto`/`-undo` variants | ✓ |
| `(link:)`, `(link-replace:)`, `(link-reveal:)`, `(link-append:)`, `(link-repeat:)`, `(link-rerun:)`, `(link-goto:)`, `(link-undo:)` | ✓ |
| `(link-reveal-goto:)`, `(link-show:)`, `(cycling-link:)`, `(link-fullscreen:)`, `(link-storylet:)` | ✗ |
| `(live:)`, `(event:)`, `(trigger:)` | ✗ |
| `(t8n:)`, `(transition:)`, transition modifiers | ✗ |
| Custom `(macro:)`, `(output:)` | ✗ |
| Storylets, `...` spread | ✗ |

### Storage

| Feature | Status |
|---|---|
| Twine 2 HTML import | ✓ |
| Twee 3 read & write | ✓ |
| Programmatic editing (`AddPassage`/`RemovePassage`/`RenamePassage` with inbound-link rewrite) | ✓ |
| Broken-link report (`GetBrokenLinks()` — `[[…]]` *and* `(goto:)`/`(display:)`/… targets, with line + column) | ✓ |
| Parse-error report (`GetParseErrors()` — every passage that didn't parse, with line + column) | ✓ |
| Lazy reserialization — clean passages round-trip byte-for-byte | ✓ |
| Save / load via pluggable `ISaveStorage` backend (IFID-namespaced slots) | ✓ |
| Reproducible `(random:)`/`(either:)` across undo, redo, and save/load (seedable RNG) | ✓ |

Story writers: a story that uses only ✓ features will play unchanged. ⚠ rows mean the macro is recognised but only a subset of arguments work. ✗ macros produce an in-prose error rather than crashing — surrounding content keeps rendering.

## Twee 3 example

A minimal story this library happily parses and runs:

```
:: StoryTitle
The Clearing

:: StoryData
{
  "ifid": "00000000-0000-0000-0000-000000000001",
  "format": "Harlowe",
  "format-version": "3.3.8",
  "start": "Clearing"
}

:: Clearing
You stand in a |spot>[clearing]. There is a path north.
(click: ?spot)[A breeze stirs the grass.]

[[Go north->Forest]]

:: Forest
The forest is dark and full of |secret>[secrets].
(enchant: ?secret, (text-style: "italic"))

[[Back->Clearing]]
```

Loaded with:

```csharp
var story = new TweeReader().Read(File.ReadAllText("clearing.tw"));
var session = new StorySession(story);
```

## Build & test

```sh
dotnet build Twinelike.sln
dotnet test  Twinelike.sln
```

Library targets `netstandard2.0`. Test project multi-targets `net48` + `net8.0` (xUnit). Both TFMs exercise the same code; CI runs `net8.0` on Linux.

To produce the distributable DLL:

```sh
# Release build merges HtmlAgilityPack into twinelike.dll via ILRepack
# (Debug builds skip the merge for fast dev cycles).
dotnet build Twinelike.sln -c Release
# → bin/Release/netstandard2.0/twinelike.dll  (self-contained, ~270 KB)

# Or for a clean publish folder:
dotnet publish Twinelike.csproj -c Release -o ./dist/Twinelike
# → dist/Twinelike/twinelike.dll

# Or for the NuGet package:
dotnet pack Twinelike.csproj -c Release -o ./dist
# → dist/Twinelike.0.2.0.nupkg
```

Drop the produced `twinelike.dll` into Unity's `Assets/Plugins/` or reference it from any .NET project — no other DLLs required.

## Architecture

Two parsing layers, then a runtime. Briefly:

- **Layer 1 — host.** Either `Harlowe(htmlText)` (HtmlAgilityPack pulls `<tw-storydata>` / `<tw-passagedata>` and HTML-entity-decodes the inner text) or `TweeReader().Read(tweeText)` (header splits on `:: Name [tags] {position}` at column 0). Both produce the same `Harlowe` story object.
- **Layer 2 — Harlowe markup.** Shared between both front-ends: `HarloweTokenizer` → `HarloweBodyParser` (which hands off to `HarloweExpressionParser` at every macro) → `PassageBody` AST.
- **Runtime.** `StorySession` owns the variable store, macro registry, and render-tree state. `BodyRenderer` walks an AST into a `RenderTreeBuilder` (an `IRenderOutput`-shaped tree-of-nodes); `RenderTreeFlusher` replays the finished tree as the flat event stream your `IRenderOutput` receives. Revision macros (`(replace:)`, `(append:)`, `(prepend:)`) mutate the tree in place; enchantment macros (`(change:)`, `(enchant:)`) re-wrap matched nodes; interaction macros (`(click:)` family) wrap targets in `InteractiveRegion` brackets and register handlers that fire on `DispatchEvent`.

For implementation depth — file layout, the descriptor-patch changer model, design rationale, conventions — see [`CLAUDE.md`](https://github.com/RaheelYawar/twinelike/blob/main/CLAUDE.md).

## Dependencies

None at runtime. The shipped `twinelike.dll` is a single self-contained assembly — [HtmlAgilityPack](https://www.nuget.org/packages/HtmlAgilityPack) (used by the Twine 2 HTML loader) is merged into it at build time via ILRepack with its types internalized, so it doesn't propagate as a NuGet dependency and doesn't conflict if your project already references HAP for its own purposes.

## Errors are inline, not exceptional

The runtime never throws on the render hot path. A bad expression renders an inline error message at the spot it happened (delivered through `IRenderOutput.Error`) and the rest of the passage keeps rendering — mirroring Harlowe's authoring model, where one broken macro doesn't take down the whole story. Engine integrations don't need `try`/`catch` around every render call.

### Catching problems before the player does

Inline errors only fire when the player *reaches* them. A typo'd passage name — or an outright syntax error — down an unplayed branch stays invisible right up until it ships. Two load-time reports find them all up front; call them once when you load the story and show the results to whoever is building it.

```csharp
var story = new Harlowe(File.ReadAllText("story.html"));

foreach (var problem in story.GetParseErrors())
    Debug.LogError(problem.Message);
    // In passage 'Doomed' (line 1, column 15): use 'is' instead of 'eq'.
    // The whole passage failed to parse and won't render.

foreach (var problem in story.GetBrokenLinks())
    Debug.LogWarning(problem.Message);
    // In passage 'Start' (line 4, column 14): (goto:) points to
    // the passage 'Dragon Lair', which doesn't exist.
```

Both DTOs also break out their fields — `PassageName` / `Line` / `Column` / `Detail` / `Target` / … — if you'd rather build an inspector row than print the message.

**`GetParseErrors()`** exists precisely *because* the loaders are tolerant: a passage that won't parse doesn't abort the load, it gets a synthetic error stub and keeps quiet. `IsWholePassage` tells you whether the whole passage is dead or the parser recovered around one bad construct and the rest still works.

**`GetBrokenLinks()`** covers both the `[[…]]` syntax and a literal passage name given to `(goto:)`, `(display:)`, `(link-goto:)`, or the `(click-goto:)` family — including calls nested in hooks and expressions, so a dead `(goto:)` inside an `(if:)` branch is found. A *computed* target (`(goto: $next)`) can't be checked statically and is skipped; those still surface as inline errors at render time.

Links themselves fail safe: a `[[…]]` whose target doesn't exist renders its label as plain prose and emits **no link event at all**, so it can't be clicked into a passage that isn't there.

## License

[MIT](https://github.com/RaheelYawar/twinelike/blob/main/LICENSE). Use it in commercial games, open-source projects, anything — just keep the copyright notice with the source.
