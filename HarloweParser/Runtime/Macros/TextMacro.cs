using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(text: 5)</c> → <c>"5"</c>. Coerces its single argument to its
  /// <see cref="HarloweValue.ToHarloweString"/> form and returns the result
  /// as a <see cref="HarloweValueKind.String"/>. Useful for forcing string
  /// concatenation: <c>"You have " + (text: $hp) + " HP"</c>.
  ///
  /// <para>Registered under both <c>text</c> and the alias <c>str</c> —
  /// reference Harlowe uses both names interchangeably; the documented
  /// <c>pos</c> example writes <c>(str: pos)</c>.</para>
  /// </summary>
  public class TextMacro : IMacro
  {
    private readonly string _name;

    public TextMacro() : this("text") { }
    public TextMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      return HarloweValue.OfString(v.ToHarloweString());
    }
  }
}
