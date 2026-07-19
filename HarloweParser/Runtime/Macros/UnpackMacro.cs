using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(unpack: $array into (a: $x, $y))</c>. Destructuring assignment: the
  /// destination is a literal <c>(a:)</c>/<c>(dm:)</c> pattern whose variable
  /// (and property-chain) positions receive the matching source values, whose
  /// nested patterns recurse, and whose plain-value positions must equal the
  /// source position exactly. String <c>(p:)</c> patterns and datatype/rest
  /// positions need the unimplemented Datatype/Pattern value types and error
  /// pointedly. As with the rest of the assignment family, the work happens
  /// during argument evaluation (<see cref="ExpressionEvaluator.EvaluateMacroArgument"/>
  /// routes an (unpack:) <c>into</c> argument through the pattern walk), so
  /// <see cref="Invoke"/> just confirms there were no errors and produces no
  /// visible output.
  /// </summary>
  public class UnpackMacro : IMacro
  {
    public string Name => "unpack";
    public int MinArgs => 1;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      for (int i = 0; i < args.Count; i++)
      {
        if (args[i].IsError) return args[i];
      }
      return null;
    }
  }
}
