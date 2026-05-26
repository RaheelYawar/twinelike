using System.Collections.Generic;

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
  /// styling macros pass through verbatim. <see cref="Effects"/> carries the
  /// semantic effect names from variadic <c>(text-style:)</c> (mark, outline,
  /// blur, shudder, …) that don't have a primitive flag. Unset value fields
  /// are <c>null</c>; unset flags are <c>false</c>; <see cref="Effects"/> is
  /// always non-null but may be empty — the field is <c>readonly</c> and
  /// initialized to a fresh empty list at construction, so consumers can
  /// iterate it without null-guarding (mutate via <c>Add</c>/<c>Clear</c>;
  /// reassignment is a compile error). A consumer should ignore fields it
  /// doesn't render rather than treating them as errors — Harlowe macros may
  /// set styles the host engine has no equivalent for.</para>
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

    /// <summary>Background color, same format as <see cref="Color"/>. Set by <c>(background:)</c> when given a colour-shaped string.</summary>
    public string BackgroundColor;

    /// <summary>Background image URL. Set by <c>(background:)</c> when given a URL-shaped string. Engines without an image channel may drop this.</summary>
    public string BackgroundImage;

    /// <summary>Font family name, e.g. <c>"Times New Roman"</c>.</summary>
    public string FontFamily;

    /// <summary>Font size, typically a percentage (<c>"120%"</c>) or unit-bearing length (<c>"1.5em"</c>); kept as a string so unit choice is the macro's call, not the spec's.</summary>
    public string FontSize;

    /// <summary>Opacity in the range 0..1, or <c>null</c> if unset. Set by <c>(opacity:)</c>.</summary>
    public double? Opacity;

    /// <summary>Text alignment, or <c>null</c> if unset. Set by <c>(align:)</c>.</summary>
    public TextAlignment? Alignment;

    /// <summary>Effect names from variadic <c>(text-style:)</c> that don't have a primitive flag — order preserved for diagnostic clarity; engines may ignore unsupported entries. <c>readonly</c> so the non-null invariant is enforced at the type system: callers mutate via <c>Add</c>/<c>Clear</c>/index, never by reassigning the field.</summary>
    public readonly List<TextEffect> Effects = new List<TextEffect>();

    /// <summary>True iff every field is unset. A no-op spec; a renderer can skip emitting events for one.</summary>
    public bool IsEmpty =>
      !Bold && !Italic && !Underline && !Strikethrough &&
      Color == null && BackgroundColor == null && BackgroundImage == null &&
      FontFamily == null && FontSize == null &&
      Opacity == null && Alignment == null &&
      (Effects == null || Effects.Count == 0);

    public override bool Equals(object obj)
    {
      if (!(obj is StyleSpec other)) return false;
      if (Bold != other.Bold
          || Italic != other.Italic
          || Underline != other.Underline
          || Strikethrough != other.Strikethrough
          || Color != other.Color
          || BackgroundColor != other.BackgroundColor
          || BackgroundImage != other.BackgroundImage
          || FontFamily != other.FontFamily
          || FontSize != other.FontSize
          || Opacity != other.Opacity
          || Alignment != other.Alignment)
        return false;
      return EffectsEqual(Effects, other.Effects);
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
      h = (h * 397) ^ (BackgroundImage?.GetHashCode() ?? 0);
      h = (h * 397) ^ (FontFamily?.GetHashCode() ?? 0);
      h = (h * 397) ^ (FontSize?.GetHashCode() ?? 0);
      h = (h * 397) ^ Opacity.GetHashCode();
      h = (h * 397) ^ Alignment.GetHashCode();
      if (Effects != null)
      {
        h = (h * 397) ^ Effects.Count;
        for (int i = 0; i < Effects.Count; i++)
          h = (h * 397) ^ (int)Effects[i];
      }
      return h;
    }

    private static bool EffectsEqual(List<TextEffect> a, List<TextEffect> b)
    {
      int ac = a?.Count ?? 0;
      int bc = b?.Count ?? 0;
      if (ac != bc) return false;
      for (int i = 0; i < ac; i++)
        if (a[i] != b[i]) return false;
      return true;
    }

    /// <summary>
    /// Field-by-field copy. The copy's <see cref="Effects"/> is the fresh
    /// empty list the field initializer produced; items are appended one at a
    /// time so the copy and the original have independent lists. Used by the
    /// render tree when it clones a styling layer for splicing.
    /// </summary>
    public StyleSpec Clone()
    {
      var copy = new StyleSpec
      {
        Bold = Bold,
        Italic = Italic,
        Underline = Underline,
        Strikethrough = Strikethrough,
        Color = Color,
        BackgroundColor = BackgroundColor,
        BackgroundImage = BackgroundImage,
        FontFamily = FontFamily,
        FontSize = FontSize,
        Opacity = Opacity,
        Alignment = Alignment,
      };
      for (int i = 0; i < Effects.Count; i++) copy.Effects.Add(Effects[i]);
      return copy;
    }
  }
}
