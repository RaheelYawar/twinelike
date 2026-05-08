using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(history:)</c>. Returns an array of past passage names in visit order
  /// (oldest first), excluding the current passage. May contain duplicates if
  /// the player revisited a passage. Backed by
  /// <see cref="IEvaluationContext.History"/>; errors if the macro is invoked
  /// without a session context.
  /// </summary>
  public class HistoryMacro : IMacro
  {
    public string Name => "history";
    public int MinArgs => 0;
    public int MaxArgs => 0;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (context == null || context.EvaluationContext == null)
        return HarloweValue.OfError("(history:) is unavailable outside a story session");
      return context.EvaluationContext.History;
    }
  }
}
