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
  /// <para><b>Body vs. expression position.</b> When called as a command from
  /// body position, <see cref="MacroContext.Output"/> is the active render
  /// sink and the displayed passage renders directly into it — links, errors,
  /// and style events propagate to the parent output rather than being
  /// flattened to a string snapshot. When called from expression position
  /// (e.g. inside another macro's args, as in <c>(set: $x to (display: "P"))</c>),
  /// <see cref="MacroContext.Output"/> is null and the displayed passage is
  /// captured into a private buffer; the returned String is the buffer's
  /// concatenated text. Structured events on the buffer are dropped in the
  /// expression-position path — a known limitation until a proper Command
  /// value kind lands.</para>
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

      if (context.Output != null)
        return context.RenderPassage(v.AsString, context.Output);

      var buf = new BufferedRenderOutput();
      var r = context.RenderPassage(v.AsString, buf);
      if (r.IsError) return r;
      return HarloweValue.OfString(buf.Text);
    }
  }
}
