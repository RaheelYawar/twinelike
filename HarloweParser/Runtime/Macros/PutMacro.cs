using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(put: 5 into $x)</c>. Mirror of <see cref="SetMacro"/> for the
  /// <c>into</c> assignment direction. Like <c>set</c>, the assignment is
  /// already done by the time the args reach this macro — the evaluator's
  /// <c>into</c> handler did the work.
  /// </summary>
  public class PutMacro : IMacro
  {
    public string Name => "put";
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
