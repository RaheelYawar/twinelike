namespace Harlowe.Runtime
{
  /// <summary>
  /// Semantic description of one styling layer — emitted through
  /// <see cref="IRenderOutput.PushStyle"/> by <see cref="Changer.Apply"/> when a
  /// changer wraps a hook. Engine integrations translate this to whatever
  /// their text renderer accepts: HTML <c>&lt;span&gt;</c> for the web,
  /// TextMeshPro tags for Unity, BBCode for Godot, ANSI escapes for a
  /// terminal, and so on.
  ///
  /// <para>Named-flag fields cover the canonical typographic styles; value
  /// fields carry user-supplied strings (color names, font families) that
  /// styling macros pass through verbatim. Unset value fields are
  /// <c>null</c>; unset flags are <c>false</c>. A consumer should ignore
  /// fields it doesn't render rather than treating them as errors — Harlowe
  /// macros may set styles the host engine has no equivalent for.</para>
  ///
  /// <para>Equality is structural so changers that compose to the same
  /// styling stack compare equal regardless of construction path.</para>
  /// </summary>
  public class StyleSpec
  {
    public bool Bold;
    public bool Italic;
    public bool Underline;
    public bool Strikethrough;

    /// <summary>CSS-style color string (e.g. <c>"red"</c>, <c>"#ff8800"</c>, <c>"rgb(255,0,0)"</c>). Unparsed — engines map as-is.</summary>
    public string Color;

    /// <summary>Background color, same format as <see cref="Color"/>.</summary>
    public string BackgroundColor;

    /// <summary>Font family name, e.g. <c>"Times New Roman"</c>.</summary>
    public string FontFamily;

    /// <summary>Font size, typically a percentage (<c>"120%"</c>) or unit-bearing length (<c>"1.5em"</c>); kept as a string so unit choice is the macro's call, not the spec's.</summary>
    public string FontSize;

    /// <summary>True iff every field is unset. A no-op spec; a renderer can skip emitting events for one.</summary>
    public bool IsEmpty =>
      !Bold && !Italic && !Underline && !Strikethrough &&
      Color == null && BackgroundColor == null &&
      FontFamily == null && FontSize == null;

    public override bool Equals(object obj)
    {
      if (!(obj is StyleSpec other)) return false;
      return Bold == other.Bold
          && Italic == other.Italic
          && Underline == other.Underline
          && Strikethrough == other.Strikethrough
          && Color == other.Color
          && BackgroundColor == other.BackgroundColor
          && FontFamily == other.FontFamily
          && FontSize == other.FontSize;
    }

    public override int GetHashCode()
    {
      int h = 17;
      h = (h * 397) ^ Bold.GetHashCode();
      h = (h * 397) ^ Italic.GetHashCode();
      h = (h * 397) ^ Underline.GetHashCode();
      h = (h * 397) ^ Strikethrough.GetHashCode();
      h = (h * 397) ^ (Color?.GetHashCode() ?? 0);
      h = (h * 397) ^ (BackgroundColor?.GetHashCode() ?? 0);
      h = (h * 397) ^ (FontFamily?.GetHashCode() ?? 0);
      h = (h * 397) ^ (FontSize?.GetHashCode() ?? 0);
      return h;
    }
  }
}
