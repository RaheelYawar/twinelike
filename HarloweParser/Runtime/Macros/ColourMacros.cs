using System;
using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(rgb: r, g, b, [a])</c> — a colour from red/green/blue components
  /// (0–255, fractional allowed) and an optional alpha (0–1). Registered under
  /// <c>rgb</c> and the alias <c>rgba</c>, matching reference's
  /// <c>[`rgb`,`rgba`]</c> registration in <c>ts/macrolib/values.ts</c> with its
  /// <c>[numberRange(0,255) ×3, optional(percent)]</c> signature.
  /// </summary>
  public class RgbMacro : IMacro
  {
    private readonly string _name;

    public RgbMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 3;
    public int MaxArgs => 4;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var r = ColourArgs.Component(_name, args[0], "red");
      if (r.IsError) return r;
      var g = ColourArgs.Component(_name, args[1], "green");
      if (g.IsError) return g;
      var b = ColourArgs.Component(_name, args[2], "blue");
      if (b.IsError) return b;
      var a = ColourArgs.Alpha(_name, args, 3);
      if (a.IsError) return a;

      return HarloweValue.OfColour(
        new ColourValue(r.AsNumber, g.AsNumber, b.AsNumber, a.AsNumber));
    }
  }

  /// <summary>
  /// <c>(hsl: h, s, l, [a])</c> — a colour from a hue angle in degrees plus
  /// saturation/lightness percentages (0–1) and an optional alpha (0–1).
  /// Registered under <c>hsl</c> and the alias <c>hsla</c>, matching
  /// reference's <c>[`hsl`,`hsla`]</c> registration with its
  /// <c>[Number, percent, percent, optional(percent)]</c> signature.
  ///
  /// <para>Hue takes <em>any</em> number and is silently rounded and wrapped
  /// into 0–359 (reference: "a value of 380 will become 20 … This allows you to
  /// cycle through hues easily by providing a steadily increasing variable"),
  /// so only saturation, lightness, and alpha are range-checked.</para>
  /// </summary>
  public class HslMacro : IMacro
  {
    private readonly string _name;

    public HslMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 3;
    public int MaxArgs => 4;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args[0].Kind != HarloweValueKind.Number)
        return HarloweValue.OfError($"({_name}:) hue must be a number, got {args[0].Kind}");
      double h = args[0].AsNumber;
      if (double.IsNaN(h) || double.IsInfinity(h))
        return HarloweValue.OfError($"({_name}:) hue must be a finite number");

      var s = ColourArgs.Percent(_name, args[1], "saturation");
      if (s.IsError) return s;
      var l = ColourArgs.Percent(_name, args[2], "lightness");
      if (l.IsError) return l;
      var a = ColourArgs.Alpha(_name, args, 3);
      if (a.IsError) return a;

      // Reference rounds then wraps the hue into 0..359 rather than erroring.
      return HarloweValue.OfColour(
        ColourValue.FromHsl(ColourValue.WrapHue(h), s.AsNumber, l.AsNumber, a.AsNumber));
    }
  }

  /// <summary>
  /// Shared argument validation for the colour-constructing macros. Each helper
  /// returns the validated <see cref="HarloweValueKind.Number"/> or an in-prose
  /// error naming the component, so callers short-circuit on
  /// <see cref="HarloweValue.IsError"/>.
  /// </summary>
  internal static class ColourArgs
  {
    /// <summary>An RGB component: a number in 0–255 (fractional allowed, per reference's 3.2 relaxation).</summary>
    public static HarloweValue Component(string macro, HarloweValue v, string which)
    {
      if (v.Kind != HarloweValueKind.Number)
        return HarloweValue.OfError($"({macro}:) {which} must be a number, got {v.Kind}");
      double n = v.AsNumber;
      if (double.IsNaN(n) || n < 0 || n > 255)
        return HarloweValue.OfError(
          $"({macro}:) {which} must be a number between 0 and 255, got {HarloweValue.FormatNumber(n)}");
      return v;
    }

    /// <summary>A 0–1 fraction (reference's `percent` type).</summary>
    public static HarloweValue Percent(string macro, HarloweValue v, string which)
    {
      if (v.Kind != HarloweValueKind.Number)
        return HarloweValue.OfError($"({macro}:) {which} must be a number, got {v.Kind}");
      double n = v.AsNumber;
      if (double.IsNaN(n) || n < 0 || n > 1)
        return HarloweValue.OfError(
          $"({macro}:) {which} must be a number between 0 and 1, got {HarloweValue.FormatNumber(n)}");
      return v;
    }

    /// <summary>
    /// The optional trailing alpha at <paramref name="index"/>: a 0–1 fraction,
    /// defaulting to fully opaque when absent.
    /// </summary>
    public static HarloweValue Alpha(string macro, List<HarloweValue> args, int index)
    {
      if (args.Count <= index) return HarloweValue.OfNumber(1);
      return Percent(macro, args[index], "alpha");
    }
  }
}
