namespace Harlowe.Runtime
{
  /// <summary>
  /// Engine-facing callback API the body renderer pushes output through. Each
  /// method maps to one renderable unit; the host engine (Unity, Godot, a CLI,
  /// a test buffer) decides what to do with each — append to a text buffer,
  /// emit DOM nodes, drive a TTS engine, log, etc.
  ///
  /// <para>
  /// <b>Style channel.</b> Changer macros (<c>(text-style:)</c>, <c>(font:)</c>, etc.)
  /// emit semantic <see cref="PushStyle"/>/<see cref="PopStyle"/> events around
  /// the wrapped hook content rather than baking in HTML wrappers. Engines
  /// translate one <see cref="StyleSpec"/> to whatever their text renderer
  /// understands — HTML <c>&lt;span&gt;</c>, Unity TMP tags, Godot BBCode, ANSI.
  /// Wrap an inner <see cref="IRenderOutput"/> in <see cref="HtmlRenderOutput"/>
  /// for the HTML mapping out of the box.
  /// </para>
  ///
  /// <para>
  /// <b>Error channel.</b> The runtime never throws on the render hot path —
  /// instead, a <see cref="HarloweValueKind.Error"/> that reaches the renderer
  /// is delivered through <see cref="Error"/>. Engines may render errors as
  /// red inline text, route them to a debug log, or silence them entirely.
  /// This is the visible-to-author face of the in-prose error policy.
  /// </para>
  ///
  /// <para>
  /// <b>Interactive channel.</b> Click/hover macros wrap their target in a
  /// <see cref="BeginInteractive"/> / <see cref="EndInteractive"/> bracket
  /// carrying an <see cref="InteractiveRegion"/> id. Engines map this to their
  /// native interactive UI — Unity TMP <c>&lt;link&gt;</c>, Godot BBCode
  /// <c>[url]</c>, HTML <c>&lt;a&gt;</c> — and report user events back via
  /// <see cref="StorySession.DispatchEvent"/>.
  /// </para>
  /// </summary>
  public interface IRenderOutput
  {
    /// <summary>Plain prose text (already entity-decoded and post-macro).</summary>
    void Text(string content);

    /// <summary>Raw HTML pass-through from author-written inline HTML in passage source (e.g. <c>&lt;b&gt;</c>). Engine consumers that don't render HTML may drop or escape this channel.</summary>
    void Html(string rawHtml);

    /// <summary>A passage-to-passage navigation link with display text and target passage name.</summary>
    void Link(string text, string target);

    /// <summary>An in-prose error message produced by a failed expression or macro. Routed through this channel rather than thrown.</summary>
    void Error(string message);

    /// <summary>Open one styling layer. The renderer guarantees a matching <see cref="PopStyle"/> after the wrapped hook content. Multiple PushStyle calls before content nest from outermost (first) to innermost (last).</summary>
    void PushStyle(StyleSpec style);

    /// <summary>Close the most recently opened styling layer. Always paired with a prior <see cref="PushStyle"/> on the same render path.</summary>
    void PopStyle();

    /// <summary>Open one interactive region. The renderer guarantees a matching <see cref="EndInteractive"/> after the wrapped content. Engines wire the region's id into whatever native event surface their UI uses; the engine reports user events back via <see cref="StorySession.DispatchEvent"/>.</summary>
    void BeginInteractive(InteractiveRegion region);

    /// <summary>Close the most recently opened interactive region. Always paired with a prior <see cref="BeginInteractive"/> on the same render path.</summary>
    void EndInteractive();
  }
}
