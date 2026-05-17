namespace Harlowe.Runtime
{
  /// <summary>
  /// One semantic text-effect layer the variadic <c>(text-style:)</c> macro
  /// can install on a <see cref="StyleSpec"/>'s <see cref="StyleSpec.Effects"/>
  /// list. The names mirror Harlowe's <c>(text-style:)</c> name set minus the
  /// four typographic styles that already have dedicated flags on
  /// <see cref="StyleSpec"/> (<c>bold</c>, <c>italic</c>, <c>underline</c>,
  /// <c>strike</c>/<c>strikethrough</c>) — those route to the flags rather than
  /// here so a flag-only spec can still take the short-tag HTML path.
  ///
  /// <para>Effects are semantic, not CSS: a renderer picks how to realise each.
  /// <see cref="HtmlRenderOutput"/> maps them to <c>text-shadow</c> /
  /// <c>transform</c> / <c>animation</c> recipes; a Unity TMP integration may
  /// implement a subset and silently drop the rest; a plain-text consumer may
  /// drop them all. Renderers should ignore unknown or unsupported effects
  /// rather than error.</para>
  /// </summary>
  public enum TextEffect
  {
    // Typographic
    Mark,
    Superscript,
    Subscript,
    Condense,
    Expand,

    // Filter / shadow
    Outline,
    Shadow,
    Emboss,
    Smear,
    Blur,
    Blurrier,

    // Transforms
    Mirror,
    UpsideDown,

    // Animations (require a tick loop on the engine)
    Blink,
    FadeInOut,
    Shudder,
    Rumble,
    Sway,
    Buoy,
    Fidget
  }
}
