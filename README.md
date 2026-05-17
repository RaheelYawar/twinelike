# Twinelike

A C# library for parsing and running [Twine/Harlowe](https://twinery.org/) interactive fiction, designed to embed inside game engines — Unity, Godot, anything that runs .NET, Mono, or IL2CPP.

It accepts Twine 2 HTML exports and Twee 3 source, parses the full Harlowe markup language, evaluates author-written macros at runtime, and surfaces rendered content through an engine-agnostic `IRenderOutput` interface — plain text, navigation links, semantic styles, and interactive regions. Implement that interface against your engine's text renderer (TextMeshPro, RichTextLabel, raw HTML, plain console) and you have a working interactive story.

**Status:** Targets `netstandard2.0`, so it drops into Unity 2018.1+, Godot 3 & 4, .NET Framework 4.6.1+, .NET 5+, Mono, and Xamarin. Tracks Harlowe 3.3.8.

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

`RenderResult` carries the rendered content as both a flat `.Text` string and a list of typed `.Entries` (Text, Link, PushStyle, BeginInteractive, …). Pass the entries to your engine's renderer, or use the `IRenderOutput` interface below to receive them as a stream.

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

  // A passage navigation link. Wire your UI's click handler to
  // session.Goto(target).
  void Link(string text, string target);

  // An in-prose error (bad expression, type mismatch, unknown macro).
  // The runtime never throws on the render hot path — errors come
  // through this channel instead.
  void Error(string message);

  // Bracketing semantic styles. StyleSpec carries flags (Bold, Italic,
  // Underline, Strikethrough), value fields (Color, BackgroundColor,
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

For web/HTML consumers, wrap your output in `HtmlRenderOutput` and it translates semantic events into HTML tags automatically — no manual mapping.

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
| Every operator from the Harlowe 3.3.8 precedence table | ✓ |
| Property access (`'s`, `of`, `its`) | ✓ |
| Ordinal indexing (`1st`, `last`, `Nthlast`) | ✓ |
| Hooks: anonymous `[…]`, `\|name>[…]`, `[…]<name\|` | ✓ |
| Twine links: `[[text->target]]`, `[[target<-text]]`, bare `[[name]]` | ✓ |
| Lambdas: `where`, `via`, `making`, `each` (incl. implicit `it`) | ✓ |
| Hook references: `?name`, `?passage`, `?page`, `?link` (+ ordinal narrowing) | ✓ |
| `(goto:)` with multi-step undo | ✓ |
| Inline `<html>` passthrough in passage bodies | ✓ |
| String escape sequences inside literals | ✗ (known limitation) |
| `when` lambda clause | ✗ (reserved for `(event:)`) |

### Macros

| Macro family | Status |
|---|---|
| `(set:)`, `(put:)`, `(print:)`, `(display:)` | ✓ |
| `(if:)`, `(unless:)`, `(else:)` | ✓ |
| `(random:)`, `(either:)`, `(history:)` | ✓ |
| `(a:)`, `(dm:)`, `(modulo:)`, `(text:)`, `(num:)` | ✓ |
| `(find:)`, `(all-pass:)`, `(some-pass:)`, `(none-pass:)`, `(altered:)` | ✓ |
| `(for:)`, `(folded:)`, `(rotated-to:)`, `(sorted:)` | ✓ |
| `(text-style:)` — full name set incl. mark, outline, shadow, blur, mirror, shudder, blink, fade-in-out, … (variadic, with `"none"` reset) | ✓ |
| `(text-color:)` / `(text-colour:)` / `(color:)` / `(colour:)` | ✓ |
| `(background:)` / `(bg:)` (colour or image url) | ✓ |
| `(font:)`, `(text-size:)` / `(size:)`, `(opacity:)`, `(align:)` | ✓ |
| `(border:)`, `(border-colour:)`, `(border-size:)`, `(corner-radius:)`, `(rotate:)` | ✗ |
| `(hover-style:)`, `(line-style:)`, `(char-style:)`, `(link-style:)` | ✗ |
| `(replace:)`, `(append:)`, `(prepend:)` | ✓ |
| `(change:)`, `(enchant:)` | ✓ hook-name targets (string targets pending) |
| `(click:)` / `(click-replace:)` / `(click-append:)` / `(click-prepend:)` | ✓ |
| `(mouseover:)` and `-replace`/`-append`/`-prepend` variants | ✓ |
| `(mouseout:)` and `-replace`/`-append`/`-prepend` variants | ✓ |
| `(link:)`, `(link-replace:)`, `(link-reveal:)`, `(link-goto:)` | ✗ |
| `(live:)`, `(event:)`, `(trigger:)` | ✗ |
| `(t8n:)`, `(transition:)`, transition modifiers | ✗ |
| Custom `(macro:)`, `(output:)` | ✗ |
| Storylets, `(unpack:)`, `...` spread, property assignment | ✗ |

### Storage

| Feature | Status |
|---|---|
| Twine 2 HTML import | ✓ |
| Twee 3 read & write | ✓ |
| Programmatic editing (`AddPassage`/`RemovePassage`/`RenamePassage`) | ✓ |
| Lazy reserialization — clean passages round-trip byte-for-byte | ✓ |

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
dotnet build harlowe-parser.sln
dotnet test  harlowe-parser.sln
```

Library targets `netstandard2.0`. Test project targets `net48` (uses xUnit).

## Architecture

Two parsing layers, then a runtime. Briefly:

- **Layer 1 — host.** Either `Harlowe(htmlText)` (HtmlAgilityPack pulls `<tw-storydata>` / `<tw-passagedata>` and HTML-entity-decodes the inner text) or `TweeReader().Read(tweeText)` (header splits on `:: Name [tags] {position}` at column 0). Both produce the same `Harlowe` story object.
- **Layer 2 — Harlowe markup.** Shared between both front-ends: `HarloweTokenizer` → `HarloweBodyParser` (which hands off to `HarloweExpressionParser` at every macro) → `PassageBody` AST.
- **Runtime.** `StorySession` owns the variable store, macro registry, and render-tree state. `BodyRenderer` walks an AST into a `RenderTreeBuilder` (an `IRenderOutput`-shaped tree-of-nodes); `RenderTreeFlusher` replays the finished tree as the flat event stream your `IRenderOutput` receives. Revision macros (`(replace:)`, `(append:)`, `(prepend:)`) mutate the tree in place; enchantment macros (`(change:)`, `(enchant:)`) re-wrap matched nodes; interaction macros (`(click:)` family) wrap targets in `InteractiveRegion` brackets and register handlers that fire on `DispatchEvent`.

For implementation depth — file layout, the descriptor-patch changer model, design rationale, conventions — see [`CLAUDE.md`](./CLAUDE.md).

## Dependencies

- [HtmlAgilityPack](https://www.nuget.org/packages/HtmlAgilityPack) 1.11.46 — only used by the Twine 2 HTML loader. Stories shipped as Twee run with no external dependencies.

## Errors are inline, not exceptional

The runtime never throws on the render hot path. A bad expression renders an inline error message at the spot it happened (delivered through `IRenderOutput.Error`) and the rest of the passage keeps rendering — mirroring Harlowe's authoring model, where one broken macro doesn't take down the whole story. Engine integrations don't need `try`/`catch` around every render call.

## License

[MIT](./LICENSE). Use it in commercial games, open-source projects, anything — just keep the copyright notice with the source.
