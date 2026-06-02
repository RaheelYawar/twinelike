using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(goto: "Some Passage")</c>. Records the requested target on
  /// <see cref="MacroContext.PendingGoto"/>. The body renderer reads that flag
  /// after each macro and aborts further node processing when it is set,
  /// then signals the session to navigate.
  ///
  /// <para>Validates that the target passage exists before recording the goto
  /// (via <see cref="MacroContext.PassageExists"/>); a missing target surfaces
  /// an in-prose error instead of silently navigating to a blank result, which
  /// matches reference Harlowe's <c>Passages.hasValid</c> check. When no story
  /// is wired (standalone renderer tests leave <c>PassageExists</c> null), the
  /// check is skipped and the goto is recorded unconditionally.</para>
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
      string target = v.AsString;
      if (context.PassageExists != null && !context.PassageExists(target))
        return HarloweValue.OfError($"I can't (goto:) to the passage '{target}' because it doesn't exist.");
      context.RequestGoto(target);
      return null;
    }
  }
}
