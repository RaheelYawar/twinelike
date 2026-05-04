using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(display: "Other Passage")</c>. Renders another passage's body inline
  /// at this point. Delegates to <see cref="MacroContext.RenderPassage"/>,
  /// wired up by the body renderer in sub-step 6. If the callback is missing,
  /// returns an in-prose error rather than crashing.
  /// </summary>
  public class DisplayMacro : IMacro
  {
    public string Name => "display";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      if (v.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"(display:) requires a String, got {v.Kind}");
      if (context.RenderPassage == null)
        return HarloweValue.OfError("(display:) has no passage renderer wired in");
      return context.RenderPassage(v.AsString);
    }
  }
}
