using System;
using System.Text;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A Harlowe colour value (reference's <c>ts/datatypes/colour.ts</c>), stored
  /// canonically as sRGB components <see cref="R"/>/<see cref="G"/>/<see cref="B"/>
  /// (0–255, possibly fractional — <c>(rgb:)</c> accepts fractional values) plus
  /// <see cref="A"/>lpha (0–1). HSL-created colours convert at construction, as
  /// in reference's constructor ("don't do the HSL to RGB conversion if the RGB
  /// values are already present"); LCH/OKLCH-created colours (which reference
  /// stores lazily) are not implemented yet — the <c>(lch:)</c>/<c>(oklch:)</c>
  /// macros and the <c>lch</c> data name are deferred.
  ///
  /// <para>Equality matches reference's <c>is()</c>: each RGB component within
  /// 1e-3, alpha exact. Mixing via <c>+</c> matches reference's <c>"+"</c>
  /// method: <c>min(round((l + r) * 0.6), 255)</c> per channel, alpha averaged.
  /// The HSL conversions are the CSSWG algorithms reference quotes
  /// ("https://drafts.csswg.org/css-color/#hsl-to-rgb").</para>
  /// </summary>
  public class ColourValue
  {
    public readonly double R;
    public readonly double G;
    public readonly double B;
    public readonly double A;

    /// <summary>
    /// Components are immutable, so a colour can be shared freely — stored,
    /// copied between variables, captured in a save — without any risk of an
    /// in-place edit aliasing through. (Reference clones on set precisely
    /// because its colours are mutable JS objects.)
    /// </summary>
    public ColourValue(double r, double g, double b, double a = 1)
    {
      R = r;
      G = g;
      B = b;
      A = a;
    }

    /// <summary>
    /// The built-in named colours (reference's colour table in
    /// <c>ts/datatypes/colour.ts</c>'s doc block — hues of <c>hsl(h, 0.8, 0.5)</c>).
    /// Matched case-insensitively, as in reference markup.
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<string, (double r, double g, double b, double a)> Named =
      new System.Collections.Generic.Dictionary<string, (double, double, double, double)>(StringComparer.OrdinalIgnoreCase)
      {
        { "red",         (0xe6, 0x19, 0x19, 1) },
        { "orange",      (0xe6, 0x80, 0x19, 1) },
        { "yellow",      (0xe5, 0xe6, 0x19, 1) },
        { "lime",        (0x80, 0xe6, 0x19, 1) },
        { "green",       (0x19, 0xe6, 0x19, 1) },
        { "cyan",        (0x19, 0xe5, 0xe6, 1) },
        { "aqua",        (0x19, 0xe5, 0xe6, 1) },
        { "blue",        (0x19, 0x7f, 0xe6, 1) },
        { "navy",        (0x19, 0x19, 0xe6, 1) },
        { "purple",      (0x7f, 0x19, 0xe6, 1) },
        { "magenta",     (0xe6, 0x19, 0xe5, 1) },
        { "fuchsia",     (0xe6, 0x19, 0xe5, 1) },
        { "white",       (0xff, 0xff, 0xff, 1) },
        { "black",       (0x00, 0x00, 0x00, 1) },
        { "grey",        (0x88, 0x88, 0x88, 1) },
        { "gray",        (0x88, 0x88, 0x88, 1) },
        { "transparent", (0x00, 0x00, 0x00, 0) },
      };

    /// <summary>True iff <paramref name="word"/> is a built-in colour name (case-insensitive).</summary>
    public static bool IsNamed(string word) => word != null && Named.ContainsKey(word);

    /// <summary>
    /// Build a colour from a lexed literal: a built-in name (<c>red</c>) or a
    /// hex form (<c>#a4e</c> / <c>#691212</c>). Returns null for anything else
    /// — the tokenizer only emits valid forms, so null means a caller bug.
    /// </summary>
    public static ColourValue FromLexeme(string lexeme)
    {
      if (lexeme == null) return null;
      if (Named.TryGetValue(lexeme, out var n))
        return new ColourValue(n.r, n.g, n.b, n.a);
      if (lexeme.Length > 0 && lexeme[0] == '#') return FromHex(lexeme);
      return null;
    }

    /// <summary>Parse <c>#fff</c> or <c>#ffffff</c> (with leading <c>#</c>). Null when malformed.</summary>
    public static ColourValue FromHex(string hex)
    {
      if (hex == null || hex.Length == 0 || hex[0] != '#') return null;
      string s = hex.Substring(1);
      if (s.Length == 3)
        s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
      if (s.Length != 6) return null;
      int val = 0;
      for (int i = 0; i < 6; i++)
      {
        int d = HexDigit(s[i]);
        if (d < 0) return null;
        val = (val << 4) | d;
      }
      return new ColourValue((val >> 16) & 0xFF, (val >> 8) & 0xFF, val & 0xFF);
    }

    private static int HexDigit(char c)
    {
      if (c >= '0' && c <= '9') return c - '0';
      if (c >= 'a' && c <= 'f') return c - 'a' + 10;
      if (c >= 'A' && c <= 'F') return c - 'A' + 10;
      return -1;
    }

    /// <summary>
    /// Build from HSL (reference's <c>HSLToRGB</c>, the CSSWG hsl-to-rgb
    /// algorithm). <paramref name="h"/> is degrees (caller pre-wraps to
    /// 0–359), <paramref name="s"/>/<paramref name="l"/>/<paramref name="a"/>
    /// are 0–1 fractions.
    /// </summary>
    public static ColourValue FromHsl(double h, double s, double l, double a = 1)
    {
      double Component(double n)
      {
        double k = (n + h / 30) % 12;
        double m = s * Math.Min(l, 1 - l);
        return l - m * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1));
      }
      return new ColourValue(
        JsRound(Component(0) * 255),
        JsRound(Component(8) * 255),
        JsRound(Component(4) * 255),
        a);
    }

    /// <summary>
    /// JavaScript's <c>Math.round</c> — half away from zero for the positive
    /// values used here — which every rounding step in reference's
    /// <c>colour.ts</c> goes through. C#'s <see cref="Math.Round(double)"/> is
    /// banker's rounding, so it would disagree on an exact <c>.5</c> with an odd
    /// integer part (<c>126.5</c> → 126 rather than 127) and silently shift a
    /// component by one against reference.
    /// </summary>
    private static double JsRound(double x) => Math.Floor(x + 0.5);

    /// <summary>
    /// Round and wrap a hue angle into 0–359, reference's <c>(hsl:)</c> rule
    /// (<c>h = round(h) % 360</c>, then <c>+= 360</c> if negative): "you can give
    /// any kind of hue number to (hsl:) … a value of 380 will become 20. This
    /// allows you to cycle through hues easily by providing a steadily
    /// increasing variable."
    /// </summary>
    public static double WrapHue(double h)
    {
      h = JsRound(h) % 360;
      return h < 0 ? h + 360 : h;
    }

    /// <summary>
    /// This colour's HSL form (reference's <c>RGBtoHSL</c>, the CSSWG
    /// rgb-to-hsl algorithm): hue a whole number of degrees 0–359,
    /// saturation/lightness 0–1 fractions.
    /// </summary>
    public (double h, double s, double l) ToHsl()
    {
      double r = R / 255, g = G / 255, b = B / 255;
      double maxVal = Math.Max(r, Math.Max(g, b)), minVal = Math.Min(r, Math.Min(g, b));
      double h = 0, s = 0, l = (minVal + maxVal) / 2;
      double d = maxVal - minVal;
      if (d != 0)
      {
        s = (l == 0 || l == 1) ? 0 : (maxVal - l) / Math.Min(l, 1 - l);
        if (maxVal == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (maxVal == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h *= 60;
      }
      if (h >= 360) h -= 360;
      return (JsRound(h), s, l);
    }

    /// <summary>
    /// Additive blend, reference's <c>"+"</c>: per-channel
    /// <c>min(round((l + r) * 0.6), 255)</c>, alpha averaged.
    /// </summary>
    public ColourValue Mix(ColourValue other) => new ColourValue(
      Math.Min(JsRound((R + other.R) * 0.6), 0xFF),
      Math.Min(JsRound((G + other.G) * 0.6), 0xFF),
      Math.Min(JsRound((B + other.B) * 0.6), 0xFF),
      (A + other.A) / 2);

    /// <summary>Reference's <c>is()</c> RGB branch: components within 1e-3, alpha exact.</summary>
    public bool EqualsColour(ColourValue other)
    {
      if (other == null) return false;
      return Math.Abs(other.R - R) < 1e-3
        && Math.Abs(other.G - G) < 1e-3
        && Math.Abs(other.B - B) < 1e-3
        && other.A == A;
    }

    /// <summary>Hash on rounded components — consistent with epsilon equality for all but boundary-straddling values; equality is the source of truth.</summary>
    public override int GetHashCode()
    {
      int h = (int)Math.Round(R);
      h = (h * 397) ^ (int)Math.Round(G);
      h = (h * 397) ^ (int)Math.Round(B);
      h = (h * 397) ^ A.GetHashCode();
      return h;
    }

    public override bool Equals(object obj) => obj is ColourValue c && EqualsColour(c);

    /// <summary>CSS form, reference's <c>toCSSString()</c> RGB branch: <c>rgba(r, g, b, a)</c>.</summary>
    public string ToCssString()
    {
      var sb = new StringBuilder("rgba(");
      sb.Append(HarloweValue.FormatNumber(R)).Append(", ");
      sb.Append(HarloweValue.FormatNumber(G)).Append(", ");
      sb.Append(HarloweValue.FormatNumber(B)).Append(", ");
      sb.Append(HarloweValue.FormatNumber(A)).Append(')');
      return sb.ToString();
    }

    /// <summary>
    /// Save/load source form: <c>transparent</c> at zero alpha (as in
    /// reference's <c>toSource()</c>), else an exact <c>(rgb: …)</c> call —
    /// RGB is our canonical storage, so this round-trips losslessly where
    /// reference's named/HSL heuristics may not.
    /// </summary>
    public string ToSource()
    {
      if (A == 0) return "transparent";
      var sb = new StringBuilder("(rgb:");
      sb.Append(HarloweValue.FormatNumber(R)).Append(',');
      sb.Append(HarloweValue.FormatNumber(G)).Append(',');
      sb.Append(HarloweValue.FormatNumber(B));
      if (A != 1) sb.Append(',').Append(HarloweValue.FormatNumber(A));
      return sb.Append(')').ToString();
    }

    /// <summary>
    /// Data-name access, reference's <c>getProperty</c>: <c>r</c>/<c>g</c>/<c>b</c>
    /// (0–255), <c>a</c> (0–1), <c>h</c> (whole degrees), <c>s</c>/<c>l</c>
    /// (0–1). Null for unknown names (<c>lch</c>/<c>oklch</c> included — those
    /// forms are deferred); the caller reports the error.
    /// </summary>
    public HarloweValue GetProperty(string name)
    {
      switch (name)
      {
        case "r": return HarloweValue.OfNumber(R);
        case "g": return HarloweValue.OfNumber(G);
        case "b": return HarloweValue.OfNumber(B);
        case "a": return HarloweValue.OfNumber(A);
        case "h": case "s": case "l":
        {
          var hsl = ToHsl();
          return HarloweValue.OfNumber(name == "h" ? hsl.h : name == "s" ? hsl.s : hsl.l);
        }
      }
      return null;
    }
  }
}
