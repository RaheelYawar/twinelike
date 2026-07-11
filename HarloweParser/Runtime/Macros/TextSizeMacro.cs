using System.Collections.Generic;
using System.Globalization;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(text-size: 1.5)[hook]</c>. Returns a <see cref="Changer"/> that sets
  /// <see cref="StyleSpec.FontSize"/> to a font-size multiplier. The numeric
  /// argument is stored as an <c>em</c>-suffixed string (so
  /// <c>(text-size: 1.5)</c> becomes <c>"1.5em"</c>) — the existing
  /// <see cref="StyleSpec.FontSize"/> contract is "any CSS length", so this
  /// stays compatible with hand-crafted specs that pass a percentage or
  /// pixel length. Registered under <c>text-size</c> and the alias <c>size</c>.
  /// </summary>
  public class TextSizeMacro : IMacro
  {
    private readonly string _name;

    public TextSizeMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var arg = args[0];
      if (arg.IsError) return arg;
      if (arg.Kind != HarloweValueKind.Number)
        return HarloweValue.OfError($"({_name}:) requires a Number, got {arg.Kind}");
      var size = arg.AsNumber.ToString("R", CultureInfo.InvariantCulture) + "em";
      return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { FontSize = size }));
    }
  }
}
