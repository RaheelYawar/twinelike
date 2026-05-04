namespace Harlowe.Runtime
{
  /// <summary>
  /// Engine-facing callback API the body renderer pushes output through. Each
  /// method maps to one renderable unit; the host engine (Unity, Godot, a CLI,
  /// a test buffer) decides what to do with each — append to a text buffer,
  /// emit DOM nodes, drive a TTS engine, log, etc.
  ///
  /// <para>
  /// <b>Error channel.</b> The runtime never throws on the render hot path —
  /// instead, a <see cref="HarloweValueKind.Error"/> that reaches the renderer
  /// is delivered through <see cref="Error"/>. Engines may render errors as
  /// red inline text, route them to a debug log, or silence them entirely.
  /// This is the visible-to-author face of the in-prose error policy.
  /// </para>
  /// </summary>
  public interface IRenderOutput
  {
    /// <summary>Plain prose text (already entity-decoded and post-macro).</summary>
    void Text(string content);

    /// <summary>Raw HTML pass-through (e.g. inline <c>&lt;b&gt;</c> from passage source). The engine decides whether to render or escape.</summary>
    void Html(string rawHtml);

    /// <summary>A passage-to-passage navigation link with display text and target passage name.</summary>
    void Link(string text, string target);

    /// <summary>An in-prose error message produced by a failed expression or macro. Routed through this channel rather than thrown.</summary>
    void Error(string message);
  }
}
