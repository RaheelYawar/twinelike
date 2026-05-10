using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(text-style: "name")[hook]</c>. Returns a <see cref="Changer"/> that
  /// emits one semantic styling layer around an attached hook. Recognised
  /// style names: <c>"bold"</c>, <c>"italic"</c>, <c>"underline"</c>. The
  /// broader styling set (<c>strike</c>/<c>mark</c>/etc.) lands with the next
  /// styling slice. Unknown style names return an in-prose error.
  ///
  /// <para>The macro emits a <see cref="StyleSpec"/> with the relevant flag
  /// set rather than baking in HTML — engine integrations decide how to
  /// render it. <see cref="HtmlRenderOutput"/> maps the named flags back to
  /// the canonical HTML tags (<c>&lt;b&gt;</c>/<c>&lt;i&gt;</c>/<c>&lt;u&gt;</c>).</para>
  /// </summary>
  public class TextStyleMacro : IMacro
  {
    public string Name => "text-style";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var arg = args[0];
      if (arg.IsError) return arg;
      if (arg.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"(text-style:) requires a String, got {arg.Kind}");

      switch (arg.AsString)
      {
        case "bold": return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { Bold = true }));
        case "italic": return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { Italic = true }));
        case "underline": return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { Underline = true }));
        default: return HarloweValue.OfError($"(text-style:) does not recognise style '{arg.AsString}'");
      }
    }
  }
}
