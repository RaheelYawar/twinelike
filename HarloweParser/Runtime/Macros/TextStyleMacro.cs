using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(text-style: "name", ...)</c>. Variadic. Returns a <see cref="Changer"/>
  /// that installs one styling layer around an attached hook, with each
  /// recognised name contributing either a primitive flag on
  /// <see cref="StyleSpec"/> (<c>bold</c>/<c>italic</c>/<c>underline</c>/
  /// <c>strike</c>) or a <see cref="TextEffect"/> entry on
  /// <see cref="StyleSpec.Effects"/> (<c>mark</c>, <c>outline</c>, <c>blur</c>,
  /// <c>shudder</c>, …).
  ///
  /// <para>The sentinel <c>"none"</c> clears any style layers that were
  /// composed earlier on the same chain — emits a
  /// <see cref="ClearStylesPatch"/> in front of the layer this call adds, so
  /// <c>(text-style: "bold") + (text-style: "none", "italic")</c> produces
  /// italic only.</para>
  ///
  /// <para>Unknown names produce an in-prose error and the macro returns the
  /// error value; other names in the same call are not processed. Names are
  /// matched case-insensitively, with both <c>strike</c> and <c>strikethrough</c>
  /// (and other natural aliases) accepted.</para>
  /// </summary>
  public class TextStyleMacro : IMacro
  {
    public string Name => "text-style";
    public int MinArgs => 1;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var spec = new StyleSpec();
      bool reset = false;
      for (int i = 0; i < args.Count; i++)
      {
        var arg = args[i];
        if (arg.IsError) return arg;
        if (arg.Kind != HarloweValueKind.String)
          return HarloweValue.OfError($"(text-style:) requires String arguments, got {arg.Kind}");
        if (!Apply(arg.AsString, spec, ref reset))
          return HarloweValue.OfError($"(text-style:) does not recognise style '{arg.AsString}'");
      }

      if (reset)
      {
        return spec.IsEmpty
          ? HarloweValue.OfChanger(Changer.FromPatches(new ClearStylesPatch()))
          : HarloweValue.OfChanger(Changer.FromPatches(new ClearStylesPatch(), new StylePatch { Style = spec }));
      }
      return HarloweValue.OfChanger(Changer.FromStyle(spec));
    }

    /// <summary>
    /// Resolve one style name into either a flag bit on <paramref name="spec"/>,
    /// an effect entry on its effect list, or the reset sentinel. Returns
    /// <c>true</c> if the name was recognised. The reset case sets
    /// <paramref name="reset"/> rather than mutating the spec — the caller
    /// expresses it as a leading <see cref="ClearStylesPatch"/>.
    /// </summary>
    private static bool Apply(string name, StyleSpec spec, ref bool reset)
    {
      if (name == null) return false;
      var n = name.ToLowerInvariant();
      switch (n)
      {
        // Reset sentinel
        case "none": reset = true; return true;

        // Primitive flags
        case "bold": spec.Bold = true; return true;
        case "italic": spec.Italic = true; return true;
        case "underline": spec.Underline = true; return true;
        case "strike":
        case "strikethrough": spec.Strikethrough = true; return true;

        // Typographic effects
        case "mark": return AddEffect(spec, TextEffect.Mark);
        case "sup":
        case "superscript": return AddEffect(spec, TextEffect.Superscript);
        case "sub":
        case "subscript": return AddEffect(spec, TextEffect.Subscript);
        case "condense": return AddEffect(spec, TextEffect.Condense);
        case "expand": return AddEffect(spec, TextEffect.Expand);

        // Filter / shadow
        case "outline": return AddEffect(spec, TextEffect.Outline);
        case "shadow": return AddEffect(spec, TextEffect.Shadow);
        case "emboss": return AddEffect(spec, TextEffect.Emboss);
        case "smear": return AddEffect(spec, TextEffect.Smear);
        case "blur": return AddEffect(spec, TextEffect.Blur);
        case "blurrier": return AddEffect(spec, TextEffect.Blurrier);

        // Transforms
        case "mirror": return AddEffect(spec, TextEffect.Mirror);
        case "upside-down": return AddEffect(spec, TextEffect.UpsideDown);

        // Animations
        case "blink": return AddEffect(spec, TextEffect.Blink);
        case "fade-in-out": return AddEffect(spec, TextEffect.FadeInOut);
        case "shudder": return AddEffect(spec, TextEffect.Shudder);
        case "rumble": return AddEffect(spec, TextEffect.Rumble);
        case "sway": return AddEffect(spec, TextEffect.Sway);
        case "buoy": return AddEffect(spec, TextEffect.Buoy);
        case "fidget": return AddEffect(spec, TextEffect.Fidget);
      }
      return false;
    }

    private static bool AddEffect(StyleSpec spec, TextEffect effect)
    {
      spec.Effects.Add(effect);
      return true;
    }
  }
}
