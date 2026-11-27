using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(display: "Other Passage")</c>. Renders another passage's body inline
  /// at this point. Delegates to <see cref="MacroContext.RenderPassage"/>,
  /// which the <see cref="StorySession"/> wires before each render. If the
  /// callback is missing (e.g. running the renderer outside a session in a
  /// test), returns an in-prose error rather than crashing.
  ///
  /// <para><b>Body vs. expression position.</b> In prose,
  /// <see cref="MacroContext.Output"/> is the active render sink and the
  /// displayed passage renders directly into it, so links, errors and styles
  /// reach the parent output. In an expression, as in
  /// <c>(set: $x to (display: "P"))</c>, the evaluator makes a command instead
  /// and this runs when the command reaches prose. A call with no sink, through
  /// <c>(partial:)</c>, captures the passage into a buffer and returns its text,
  /// without the structured events.</para>
  /// </summary>
  public class DisplayMacro : IMacro, ICommandCheck
  {
    public string Name => "display";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    HarloweValue ICommandCheck.Check(List<HarloweValue> args, MacroContext context)
    {
      var name = args[0];
      if (name.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"(display:) requires a String, got {name.Kind}");
      if (context.PassageExists != null && !context.PassageExists(name.AsString))
        return HarloweValue.OfError($"passage '{name.AsString}' not found");
      return null;
    }

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      if (v.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"(display:) requires a String, got {v.Kind}");
      if (context.RenderPassage == null)
        return HarloweValue.OfError("(display:) has no passage renderer wired in");

      if (context.Output != null)
        return context.RenderPassage(v.AsString, context.Output);

      var buf = new BufferedRenderOutput();
      var r = context.RenderPassage(v.AsString, buf);
      if (r.IsError) return r;
      return HarloweValue.OfString(buf.Text);
    }
  }
}
