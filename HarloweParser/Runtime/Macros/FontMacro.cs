using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(font: "Times New Roman")[hook]</c>. Returns a <see cref="Changer"/>
  /// that sets <see cref="StyleSpec.FontFamily"/> on its wrapped hook. The
  /// value is a font family name passed through unmodified — engines decide
  /// how to resolve it (CSS font-family, TMP font asset name, etc.).
  /// </summary>
  public class FontMacro : IMacro
  {
    public string Name => "font";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var arg = args[0];
      if (arg.IsError) return arg;
      if (arg.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"(font:) requires a String, got {arg.Kind}");
      var invalid = StyleValueValidator.Validate("font", arg.AsString);
      if (invalid != null) return invalid;
      return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { FontFamily = arg.AsString }));
    }
  }
}
