using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(set: $x to 5, $y to 10)</c>. Each argument is an assignment
  /// (<c>VariableRefNode to expr</c>) whose evaluation already performed the
  /// store mutation in <see cref="ExpressionEvaluator"/>'s handling of the
  /// <c>to</c> operator. By the time this macro is invoked, the assignments
  /// are done — so <see cref="Invoke"/> just confirms there were no errors and
  /// produces no visible output.
  /// </summary>
  public class SetMacro : IMacro
  {
    public string Name => "set";
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
