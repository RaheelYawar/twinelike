using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(move: $arr's 1st into $var)</c>. A variant of <c>(put:)</c> that
  /// deletes its source after copying it — the array position is spliced out,
  /// the datamap key removed, the string character excised, and a whole
  /// variable (<c>(move: $p into $q)</c>) removed. That last part follows real
  /// Harlowe 3.3.9 and the 4.0 specs rather than the (move:) article, which
  /// says "Moving from variable to variable … won't cause $p to be deleted". A
  /// source that isn't stored in a variable at all is an in-prose error. As with
  /// <see cref="SetMacro"/>/<see cref="PutMacro"/>, the work happens during
  /// argument evaluation (<see cref="ExpressionEvaluator.EvaluateMacroArgument"/>
  /// routes a (move:) <c>into</c> argument through the copy-then-delete
  /// path), so <see cref="Invoke"/> just confirms there were no errors and
  /// produces no visible output.
  /// </summary>
  public class MoveMacro : IMacro
  {
    public string Name => "move";
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
