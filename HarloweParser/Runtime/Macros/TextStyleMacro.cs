using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(text-style: "name")[hook]</c>. Returns a <see cref="Changer"/> that
  /// wraps an attached hook in the HTML element for the named style.
  /// Recognised style names in 2.1A: <c>"bold"</c>, <c>"italic"</c>,
  /// <c>"underline"</c>. The 2.1B slice will broaden the set
  /// (<c>strike</c>/<c>mark</c>/<c>superscript</c>/<c>subscript</c>) and add
  /// the other styling changers (<c>(text-color:)</c>, <c>(font:)</c>, etc.).
  /// Unknown style names return an in-prose error.
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
        case "bold": return HarloweValue.OfChanger(Changer.FromHtml("<b>", "</b>"));
        case "italic": return HarloweValue.OfChanger(Changer.FromHtml("<i>", "</i>"));
        case "underline": return HarloweValue.OfChanger(Changer.FromHtml("<u>", "</u>"));
        default: return HarloweValue.OfError($"(text-style:) does not recognise style '{arg.AsString}'");
      }
    }
  }
}
