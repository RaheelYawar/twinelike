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
  public class GotoMacro : IMacro, ICommandCheck
  {
    public string Name => "goto";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    HarloweValue ICommandCheck.Check(List<HarloweValue> args, MacroContext context)
      => CheckTarget(args[0], context);

    private static HarloweValue CheckTarget(HarloweValue target, MacroContext context)
    {
      if (target.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"(goto:) requires a String, got {target.Kind}");
      // "(goto:)" as the verb, matching reference's phrasing for the
      // (click-goto:) family ("I can't (click-goto:) the passage …").
      return context.MissingPassageError("(goto:)", target.AsString);
    }

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      var error = CheckTarget(v, context);
      if (error != null) return error;
      context.RequestGoto(v.AsString);
      return null;
    }
  }
}
