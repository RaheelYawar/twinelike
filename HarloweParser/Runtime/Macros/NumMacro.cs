using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(num: "5")</c> → <c>5</c>. Converts a String to a Number using JS-style
  /// coercion (reference Harlowe uses JS <c>+expr</c>): an empty or
  /// whitespace-only string is <c>0</c>, leading/trailing whitespace is ignored,
  /// decimals / scientific notation (<c>"1e3"</c>) / a leading sign are accepted,
  /// and <c>"Infinity"</c>/<c>"-Infinity"</c> coerce to ±∞. Anything the whole
  /// string can't represent (letters, <c>"1.2.3"</c>, …) is an error. The
  /// argument must be a String — a Number is rejected (use <c>(str:)</c> for the
  /// reverse direction), matching reference's <c>[String]</c> signature.
  ///
  /// <para>Registered under both <c>num</c> and <c>number</c>. Unsigned
  /// <c>0x</c>/<c>0o</c>/<c>0b</c> integers convert too, as they do in JS.</para>
  /// </summary>
  public class NumMacro : IMacro
  {
    private readonly string _name;

    public NumMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      if (v.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"({_name}:) requires a String, got {v.Kind}");

      string s = v.AsString;
      if (JsNumbers.TryParse(s, out double n))
        return HarloweValue.OfNumber(n);
      return HarloweValue.OfError($"I couldn't convert the string \"{s}\" to a number.");
    }
  }
}
