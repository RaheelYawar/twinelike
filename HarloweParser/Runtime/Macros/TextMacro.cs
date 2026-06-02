using System.Collections.Generic;
using System.Text;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(text: "You have ", $hp, " HP")</c> → <c>"You have 10 HP"</c>. Coerces
  /// every argument to its <see cref="HarloweValue.ToHarloweString"/> form and
  /// concatenates them with no separator. Accepts zero or more arguments
  /// (<c>(text:)</c> → <c>""</c>). Only String, Number, Boolean, and Array
  /// values are allowed — a Datamap, Changer, Lambda, etc. is rejected, matching
  /// reference Harlowe's <c>either(String, Number, Boolean, Array, CodeHook)</c>
  /// signature (we have no CodeHook type). An Array argument stringifies to its
  /// comma-joined elements, so <c>(text: (a: 2, "Hot"))</c> is <c>"2,Hot"</c>.
  ///
  /// <para>Registered under <c>text</c>, <c>str</c>, and <c>string</c> —
  /// reference uses all three interchangeably; the documented <c>pos</c> example
  /// writes <c>(str: pos)</c>.</para>
  /// </summary>
  public class TextMacro : IMacro
  {
    private readonly string _name;

    public TextMacro() : this("text") { }
    public TextMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 0;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var sb = new StringBuilder();
      for (int i = 0; i < args.Count; i++)
      {
        var v = args[i];
        if (v.IsError) return v;
        switch (v.Kind)
        {
          case HarloweValueKind.String:
          case HarloweValueKind.Number:
          case HarloweValueKind.Bool:
          case HarloweValueKind.Array:
            sb.Append(v.ToHarloweString());
            break;
          default:
            return HarloweValue.OfError(
              $"({_name}:) only accepts strings, numbers, booleans, and arrays; argument {i + 1} is a {v.Kind}");
        }
      }
      return HarloweValue.OfString(sb.ToString());
    }
  }
}
