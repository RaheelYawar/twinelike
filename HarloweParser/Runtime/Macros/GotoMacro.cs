using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(goto: "Some Passage")</c>. Records the requested target on
  /// <see cref="MacroContext.PendingGoto"/>. The body renderer reads that flag
  /// after each macro and aborts further node processing when it is set,
  /// then signals the session to navigate.
  /// </summary>
  public class GotoMacro : IMacro
  {
    public string Name => "goto";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      if (v.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"(goto:) requires a String, got {v.Kind}");
      context.RequestGoto(v.AsString);
      return null;
    }
  }
}
